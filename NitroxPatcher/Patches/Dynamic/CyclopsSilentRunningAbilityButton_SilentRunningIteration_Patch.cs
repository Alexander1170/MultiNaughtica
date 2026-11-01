using System;
using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Engineer;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Silent running costs power every few seconds. The metadata that switches it on is applied on every client (<c>CyclopsMetadataProcessor</c> starts the repeating call), so each
/// client drained its copy of the same battery and the charge froze. Only the client that holds the lock of the Cyclops pays (V0, battery charge; the same rule as the shield).
/// </summary>
public sealed partial class CyclopsSilentRunningAbilityButton_SilentRunningIteration_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsSilentRunningAbilityButton t) => t.SilentRunningIteration());

    public static bool Prefix(CyclopsSilentRunningAbilityButton __instance, out float __state)
    {
        __state = -1f;
        if (__instance.subRoot && __instance.subRoot.TryGetNitroxId(out NitroxId id) && !Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return false;
        }
        // The engineer's route "systems" pays for the deeper silence: the game takes the cost from this field once per call, so it is changed for the call and put back (the engineer console, station 3)
        float factor = EngineerState.SilentRunningCostFactor(__instance.subRoot);
        if (factor != 1f)
        {
            __state = __instance.subRoot.silentRunningPowerCost;
            __instance.subRoot.silentRunningPowerCost = __state * factor;
        }
        return true;
    }

    public static Exception Finalizer(CyclopsSilentRunningAbilityButton __instance, float __state, Exception __exception)
    {
        if (__state >= 0f && __instance.subRoot)
        {
            __instance.subRoot.silentRunningPowerCost = __state;
        }
        return __exception;
    }
}
