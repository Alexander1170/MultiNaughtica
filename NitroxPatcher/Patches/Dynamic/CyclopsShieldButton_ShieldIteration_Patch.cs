using System;
using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Engineer;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class CyclopsShieldButton_ShieldIteration_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsShieldButton t) => t.ShieldIteration());

    public static bool Prefix(CyclopsShieldButton __instance, out float __state)
    {
        __state = -1f;
        // Only the owner should run this method so the power drains at the correct speed
        if (!__instance.subRoot.TryGetNitroxId(out NitroxId id) || !Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return false;
        }
        // The engineer's route "shields": the game takes the cost from this field once per call, so it is changed for the call and put back (the engineer console, station 3)
        float factor = EngineerState.ShieldCostFactor(__instance.subRoot);
        if (factor != 1f)
        {
            __state = __instance.subRoot.shieldPowerCost;
            __instance.subRoot.shieldPowerCost = __state * factor;
        }
        return true;
    }

    public static Exception Finalizer(CyclopsShieldButton __instance, float __state, Exception __exception)
    {
        if (__state >= 0f && __instance.subRoot)
        {
            __instance.subRoot.shieldPowerCost = __state;
        }
        return __exception;
    }
}
