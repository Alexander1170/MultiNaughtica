using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The game's own Seamoth Solar Charger adds a fixed amount of energy once a second, on every client that has the Seamoth loaded. The co-op build replaces the amount with the server's setting
/// (1 per second and module by default, which is the game's value) and lets only the client that simulates the Seamoth do it (<see cref="VehicleSolar" />). With the vehicle upgrades switched
/// off the game's method runs unchanged.
/// </summary>
public sealed partial class SeaMoth_UpdateSolarRecharge_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaMoth t) => t.UpdateSolarRecharge());

    public static bool Prefix(SeaMoth __instance)
    {
        PatchProbe.Fired("SeaMoth.UpdateSolarRecharge");
        return VehicleSolar.OnSeamothSolarTick(__instance);
    }
}
