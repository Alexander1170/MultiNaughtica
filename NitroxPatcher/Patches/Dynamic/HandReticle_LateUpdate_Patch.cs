using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Writes the lines of the Cyclops power share and of the tow hooks under the "Press X to exit" of a vehicle's pilot or of the Cyclops helm: "Charging from Cyclops", "Emergency power from docked X", "Towing X". The pilot's seats write
/// the line above it and a blank one below it every frame, and the game clears all of the reticle's lines at the end of every <c>LateUpdate</c>, so this runs before it, every frame.
/// </summary>
public sealed partial class HandReticle_LateUpdate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((HandReticle t) => t.LateUpdate());

    private static bool failureLogged;

    public static void Prefix(HandReticle __instance)
    {
        if (!string.IsNullOrEmpty(__instance.textUseSubscript))
        {
            return;
        }

        try
        {
            bool power = PowerShare.TryGetPilotLine(out string powerLine);
            bool tow = VehicleTows.TryGetLine(out string towLine);
            if (power || tow)
            {
                PatchProbe.Fired("HandReticle.LateUpdate");
                __instance.SetTextRaw(HandReticle.TextType.UseSubscript, power && tow ? string.Concat(powerLine, "\n", towLine) : power ? powerLine : towLine);
            }
        }
        catch (System.Exception exception)
        {
            // This runs every frame in front of the game's own LateUpdate: whatever is wrong with a line must not stop the reticle
            if (!failureLogged)
            {
                failureLogged = true;
                Log.Error(exception, "[VehicleUpgrades] the on-screen lines of the power share and the tow hook failed");
            }
        }
    }
}
