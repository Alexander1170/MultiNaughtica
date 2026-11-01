using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// For some reason, when a creature attacks a Cyclops, its main rigidbody is sometimes not properly detected by the raycasts.
/// Thus we change the target check (when it's a Cyclops) to also check if the collider is part of the target cyclops.
/// </summary>
public sealed partial class MeleeAttack_CanDealDamageTo_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MeleeAttack t) => t.CanDealDamageTo(default));

    /*
     * REPLACE:
     * if (!(gameObject == target) && !(gameObject == base.gameObject) && !(gameObject.GetComponent<Creature>() != null))
     * 
     * BY:
     * if (!(gameObject == target) && !(gameObject == base.gameObject) && !(gameObject.GetComponent<Creature>() != null) && !MeleeAttack_CanDealDamageTo_Patch.IsValidTarget(target, gameObject))
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Match the latest part of the if
        CodeMatcher matcher = new CodeMatcher(instructions).MatchEndForward([
            new CodeMatch(OpCodes.Ldloc_S),
            new CodeMatch(OpCodes.Callvirt, Reflect.Method((GameObject t) => t.GetComponent<Creature>())),
            new CodeMatch(OpCodes.Ldnull),
            new CodeMatch(OpCodes.Call),
            new CodeMatch(OpCodes.Brtrue),
        ]);

        // We copy the "brtrue" so we can easily insert another clause in the "if"
        CodeInstruction brTrueInstruction = matcher.Instruction;

        // Advance 1 to insert after the matched pattern
        matcher.Advance(1)
               .Insert([
                   new CodeInstruction(OpCodes.Ldarg_1), // target
                   new CodeInstruction(OpCodes.Ldloc_S, (byte)7), // gameObject
                   new CodeInstruction(OpCodes.Call, Reflect.Method(() => IsValidTarget(default, default))),
                   brTrueInstruction
                ]);
        
        return matcher.InstructionEnumeration();
    }

    /// <summary>
    /// Vanilla only accepts a Player, Creature, Vehicle or Cyclops as a bite target, and a remote player's body is none of them, so a creature never even tried to bite it
    /// (the bite check runs before any damage is dealt and before the raycast the transpiler above adjusts). The creature's simulating client now accepts the body,
    /// which makes its bite reach <see cref="LiveMixin_TakeDamage_Patch"/> where the hit is relayed to the bitten player.
    /// </summary>
    public static bool Prefix(MeleeAttack __instance, GameObject target, ref bool __result)
    {
        if (target && target.TryGetComponent(out ScoutDrone drone))
        {
            // The game's list of what a mouth can bite (a player, a creature, a vehicle, a Cyclops) has no camera drone, so a creature chased the scout drone and never bit it: it counts as a vehicle here (the
            // creatures that can bite vehicles), as long as it is out of its dock and alive. Who applies the damage is decided where it lands (LiveMixin_TakeDamage_Patch: the drone's operator).
            __result = ScoutDroneRules.CanBeBitten(__instance.canBiteVehicle, drone.IsDocked, drone.Camera && drone.Camera.liveMixin && drone.Camera.liveMixin.IsAlive());
            return false;
        }
        if (!target || !target.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            return true;
        }

        __result = CanBiteRemotePlayer(__instance, remotePlayerIdentifier.RemotePlayer);
        LogConsideredBite(__instance, remotePlayerIdentifier.RemotePlayer, __result);
        return false;
    }

    private static float lastConsideredBiteLogTime;

    /// <summary>
    /// At most once a second: shows that a creature's mouth touched a remote player and what was decided (to tell "never tried" from "tried and refused" when no bite arrives).
    /// </summary>
    private static void LogConsideredBite(MeleeAttack attack, RemotePlayer remotePlayer, bool result)
    {
        if (Time.time - lastConsideredBiteLogTime < 1f)
        {
            return;
        }
        lastConsideredBiteLogTime = Time.time;
        Log.Debug($"[CreatureAttackedPlayer] {attack.name} touched remote player {remotePlayer.PlayerName}, can bite: {result}");
    }

    private static bool CanBiteRemotePlayer(MeleeAttack attack, RemotePlayer remotePlayer)
    {
        if (!attack.canBitePlayer)
        {
            return false;
        }

        // Copies of a creature on other clients do not act (their AI is skipped), only the simulating client's creature bites
        Creature creature = attack.GetComponentInParent<Creature>();
        if (!creature || !creature.TryGetNitroxId(out NitroxId creatureId) || !Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            return false;
        }

        // Creatures whose attacks are synced (AI whitelist) are replicated on the victim's side, their copy there bites the victim itself
        if (Resolve<AI>().IsCreatureWhitelisted(creature))
        {
            return false;
        }

        // Same conditions as Player.CanBeAttacked for the local player (not inside a base or sub, not in creative), plus alive and not in a vehicle
        // (a vehicle is hit through its own live mixin)
        return remotePlayer.LiveMixin.IsAlive() && remotePlayer.CanBeAttacked() && !remotePlayer.Vehicle;
    }

    public static bool IsValidTarget(GameObject target, GameObject gameObject)
    {
        return target.GetComponent<NitroxCyclops>() && gameObject.GetComponentInParent<NitroxCyclops>(true);
    }
}
