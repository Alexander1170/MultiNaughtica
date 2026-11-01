using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>A Seamoth whose power runs out ends its afterburner burn.</summary>
public sealed partial class Vehicle_OnPoweredChanged_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnPoweredChanged(default));

    public static void Postfix(Vehicle __instance, bool powered)
    {
        if (!powered)
        {
            VehicleAfterburners.End(__instance, AfterburnerState.EndReason.PowerLost);
        }
    }
}
