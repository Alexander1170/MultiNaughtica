using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The one question the game asks about a quick slot before it spends energy for the key press, and the number the slot's wheel draws: how far its cooldown is. The Seamoth afterburner's
/// cooldown lives in <see cref="AfterburnerState" /> (the game clears its own arrays when the pilot gets out), so the answer is replaced while the afterburner burns or recovers.
/// </summary>
public sealed partial class Vehicle_GetQuickSlotCooldown_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.GetQuickSlotCooldown(default));

    public static void Postfix(Vehicle __instance, int slotID, ref float __result)
    {
        PatchProbe.Fired("Vehicle.GetQuickSlotCooldown");
        VehicleAfterburners.AdjustCooldown(__instance, slotID, ref __result);
    }
}
