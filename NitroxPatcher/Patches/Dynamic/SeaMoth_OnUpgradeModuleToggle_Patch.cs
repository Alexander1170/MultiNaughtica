using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A quick slot module of the Seamoth that can be switched on and off was switched: the Seamoth Tow Hook hooks a vehicle when its slot is switched on and lets go when it is switched off (also when the
/// game switches it off, for example when the pilot gets out). The Seamoth's own method only knows its three loot sensors and returns for the rest.
/// </summary>
public sealed partial class SeaMoth_OnUpgradeModuleToggle_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaMoth t) => t.OnUpgradeModuleToggle(default, default));

    public static void Postfix(SeaMoth __instance, int slotID, bool active)
    {
        if (__instance.modules != null && slotID >= 0 && slotID < __instance.slotIDs.Length && __instance.modules.GetTechTypeInSlot(__instance.slotIDs[slotID]) == (TechType)CoopItems.SEAMOTH_TOW_HOOK)
        {
            VehicleTows.OnSeamothToggle(__instance, slotID, active);
        }
    }
}
