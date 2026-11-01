#if DEBUG
using System.Reflection;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sync test harness only (Debug builds): after the console command <c>holdsettled seconds</c> the game's own check for a settled world says "no" for that long, so that the
/// teleport freeze (PL4) can be watched running into its timeout without a real streaming hang. Without the command this patch does nothing.
/// </summary>
public sealed partial class Harness_LargeWorldStreamer_IsWorldSettled_Hold_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((LargeWorldStreamer t) => t.IsWorldSettled());

    public static bool Prefix(ref bool __result)
    {
        if (Time.unscaledTime < HarnessConsoleCommands.HoldWorldSettledUntil)
        {
            __result = false;
            return false;
        }

        return true;
    }
}
#endif
