using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Bases;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A bioreactor burns its fuel only for the player who simulates the base. In vanilla every client burned the fuel of its own copy of the container: the fuel was used up
/// once per player, the other players kept (or lost) different items, and the server never learned that an item was gone, so the burned fuel came back after a relog.
/// Now only the simulating player consumes (and tells the server which item is gone, the other clients remove it when that arrives); the other clients still show the reactor
/// producing while it has fuel, and take the charge and the burn progress from the metadata the simulating player sends (<see cref="NitroxClient.GameLogic.Spawning.Metadata.Extractor.PowerSourceMetadataExtractor"/>).
/// </summary>
public sealed partial class BaseBioReactor_ProducePower_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseBioReactor t) => t.ProducePower(default));

    public static bool Prefix(BaseBioReactor __instance, float requested, ref float __result, out int __state)
    {
        if (BaseSimulation.DisabledByHarness || BaseSimulation.RunsHere(__instance, Resolve<SimulationOwnership>()))
        {
            __state = __instance.container.count;
            return true;
        }

        // Not our base to burn: same answer as the vanilla method gives while there is fuel, nothing consumed
        __state = -1;
        __result = requested > 0f && __instance.container.count > 0 ? requested : 0f;
        return false;
    }

    public static void Postfix(BaseBioReactor __instance, int __state)
    {
        // An item was burned in this call: tell the others how far the next one is burned now, without waiting for the throttle
        if (!BaseSimulation.DisabledByHarness && __state >= 0 && __instance.container.count < __state && __instance.TryGetComponent(out PowerSource powerSource))
        {
            PowerSource_ModifyPower_Patch.BroadcastNow(powerSource);
        }
    }

    /*
     * this.container.RemoveItem(pickupable, true);
     * BaseBioReactor_ProducePower_Patch.Callback(pickupable); <--- [INSERTED LINE]
     * Object.Destroy(pickupable.gameObject);
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        CodeMatcher matcher = new(instructions);
        matcher.MatchStartForward(new CodeMatch(OpCodes.Call, Reflect.Method(() => Object.Destroy(default(Object)))));
        if (matcher.IsInvalid)
        {
            throw new System.InvalidOperationException($"{nameof(BaseBioReactor_ProducePower_Patch)}: the call of Object.Destroy was not found in BaseBioReactor.ProducePower");
        }

        // ldloc.s <pickupable>, callvirt Component.get_gameObject, call Object.Destroy: check that this is what comes before the call, a game build with other IL must not get the wrong value
        if (!matcher.InstructionAt(-1).Calls(typeof(Component).GetProperty(nameof(Component.gameObject)).GetGetMethod()) || !matcher.InstructionAt(-2).IsLdloc())
        {
            throw new System.InvalidOperationException($"{nameof(BaseBioReactor_ProducePower_Patch)}: Object.Destroy in BaseBioReactor.ProducePower is not called with the game object of a local");
        }

        matcher.Advance(-2);
        CodeInstruction loadPickupable = matcher.Instruction.Clone();
        return matcher.InsertAndAdvance([
                          loadPickupable,
                          new CodeInstruction(OpCodes.Call, Reflect.Method(() => Callback(default)))
                      ]).InstructionEnumeration();
    }

    public static void Callback(Pickupable pickupable)
    {
        if (BaseSimulation.DisabledByHarness || !pickupable || !pickupable.TryGetIdOrWarn(out NitroxId id))
        {
            return;
        }

        // The item left the container of this client and is destroyed right now: the server and the other clients have to lose it too
        Resolve<Entities>().RemoveEntity(id);
        Resolve<IPacketSender>().Send(new EntityDestroyed(id));
    }
}
