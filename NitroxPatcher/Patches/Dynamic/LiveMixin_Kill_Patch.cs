using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LiveMixin_Kill_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((LiveMixin t) => t.Kill(default));

    /// <summary>
    /// How many times (every 0.25 s) to check whether a killed object got destroyed.
    /// </summary>
    private const int MAX_DESTRUCTION_CHECKS = 12;

    /// <summary>
    /// A vanished object only counts as destroyed by its kill while the local player is within this distance (meters) of where it died.
    /// Further away, the object most likely vanished because its cell unloaded, which is no death and must not remove it for the other players.
    /// </summary>
    private const float MAX_PLAYER_DISTANCE = 60f;

    public static void Postfix(LiveMixin __instance)
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        // We don't broadcast if we don't have objectId or if the object is whitelisted,
        // in which case kill broadcast is managed differently (the vehicles and the Cyclops have their own patches; the Scout Drone has none, it is announced here like any object that is destroyed on death)
        if (!__instance.TryGetNitroxId(out NitroxId objectId) ||
            (Resolve<LiveMixinManager>().IsWhitelistedUpdateType(__instance) && !__instance.GetComponent<ScoutDrone>()))
        {
            return;
        }
        
        // Some objects don't have destroyOnDeath but we still need to broadcast the death
        // (because the destruction is managed by another script)
        if (__instance.destroyOnDeath || Resolve<LiveMixinManager>().ShouldBroadcastDeath(__instance))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(objectId));
            return;
        }

        // Harvestable plants and coral cut with the knife or the Prawn drill are killed without destroyOnDeath and get destroyed by a script reacting
        // to OnKill: only the cutter's copy would vanish. Whatever gets destroyed shortly after its kill is announced, whichever script destroys it.
        // Creatures are left out: their corpses and cooked fish have their own pipeline (and a cooked fish keeps the creature's id).
        // (A second Kill call on the same object, which happens, must not start a second watch: it would announce twice.)
        if (!__instance.GetComponent<CreatureDeath>() && WatchedIds.Add(objectId))
        {
            CoroutineHost.StartCoroutine(BroadcastDestructionAfterKill(__instance.gameObject, objectId, __instance.transform.position));
        }
    }

    private static readonly HashSet<NitroxId> WatchedIds = [];

    private static IEnumerator BroadcastDestructionAfterKill(GameObject gameObject, NitroxId objectId, Vector3 killPosition)
    {
        try
        {
            yield return WatchForDestruction(gameObject, objectId, killPosition);
        }
        finally
        {
            WatchedIds.Remove(objectId);
        }
    }

    private static IEnumerator WatchForDestruction(GameObject gameObject, NitroxId objectId, Vector3 killPosition)
    {
        WaitForSeconds interval = new(0.25f);
        for (int i = 0; i < MAX_DESTRUCTION_CHECKS; i++)
        {
            yield return interval;
            // Unity's null: the object was destroyed
            if (!gameObject)
            {
                // The player may have moved on in the meantime (or been moved, e.g. by a respawn): then the cell holding the object unloaded and it was never killed off
                if (Player.main && Vector3.Distance(Player.main.transform.position, killPosition) <= MAX_PLAYER_DISTANCE)
                {
                    Resolve<IPacketSender>().Send(new EntityDestroyed(objectId));
                }
                else
                {
                    Log.Debug($"[{nameof(LiveMixin_Kill_Patch)}] Killed object {objectId} vanished while the player was far from where it died, assuming its cell unloaded");
                }
                yield break;
            }
        }

        // R3c: some harvestables are killed but never destroyed. The coral shell plate stays standing at 0 hp until its cell reloads, nobody else would hear of it and the server would
        // bring it back at full health. It stays standing for the cutter (hitting it again does nothing), the others lose it now.
        if (gameObject && gameObject.TryGetComponent(out LiveMixin liveMixin) && liveMixin.health <= 0f && StaysStandingAtZeroHealth(gameObject))
        {
            Log.Debug($"[{nameof(LiveMixin_Kill_Patch)}] {CraftData.GetTechType(gameObject)} {objectId} is still standing at 0 hp, announcing it as gone");
            Resolve<IPacketSender>().Send(new EntityDestroyed(objectId));
        }
    }

    /// <summary>
    /// Kinds that survive their own death and are known to do so (found with the harness' res-kill-matrix); everything else that stands after a kill is left alone, it may be a
    /// base piece, a vehicle or a script's own state and has its own pipeline.
    /// </summary>
    private static bool StaysStandingAtZeroHealth(GameObject gameObject)
    {
        return CraftData.GetTechType(gameObject) == TechType.CoralShellPlate;
    }
}
