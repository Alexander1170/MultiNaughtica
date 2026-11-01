using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The game computes the crush depth of a Seamoth from a table inside this method that only knows its three vanilla depth modules; the depth modules of the co-op build are added after it
/// (<see cref="VehicleDepthModules" />). Always recomputes, whatever module changed: a vanilla module installed next to a new one makes the game set the vanilla depth first.
/// </summary>
public sealed partial class SeaMoth_OnUpgradeModuleChange_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaMoth t) => t.OnUpgradeModuleChange(default, default(TechType), default));

    /// <summary>
    /// For the four modules the game's method knows (the reinforcement module and the three depth modules) its depth branch (IL_007d..IL_0122) only builds its table, sets the crush depth from it and
    /// returns: the table ignores the new modules, so the player would get the message "crush depth now" twice, the first one with a depth that is wrong while a new module is installed. The same
    /// table, extended, is applied here instead. But the method does something else first, for every module type (IL_0000..IL_0050): it enables the slot's storage input only for a storage module
    /// and activates the slot's torpedo silo only for a torpedo module, so a depth module in a slot turns both off. That is repeated here before the method is skipped.
    /// </summary>
    public static bool Prefix(SeaMoth __instance, int slotID, TechType techType, bool added)
    {
        switch (techType)
        {
            case TechType.SeamothReinforcementModule:
            case TechType.VehicleHullModule1:
            case TechType.VehicleHullModule2:
            case TechType.VehicleHullModule3:
                if (slotID >= 0 && slotID < __instance.storageInputs.Length)
                {
                    SeamothStorageInput storageInput = __instance.storageInputs[slotID];
                    if (storageInput)
                    {
                        storageInput.SetEnabled(added && techType == TechType.VehicleStorageModule);
                    }
                    GameObject torpedoSilo = __instance.torpedoSilos[slotID];
                    if (torpedoSilo)
                    {
                        torpedoSilo.SetActive(added && techType == TechType.SeamothTorpedoModule);
                    }
                }
                RiderBars.OnModulesChanged(__instance);
                VehicleDepthModules.RefreshSeamoth(__instance);
                VehicleAfterburners.OnModulesChanged(__instance);
                VehicleTows.OnModulesChanged(__instance);
                return false;
            default:
                return true;
        }
    }

    public static void Postfix(SeaMoth __instance)
    {
        // The rider bar is built or taken down, and a rider of a bar that is gone lets go (first: the rider is the one whose loss would hurt)
        RiderBars.OnModulesChanged(__instance);
        VehicleDepthModules.RefreshSeamoth(__instance);
        // A burn without its module (taken out while burning) is over
        VehicleAfterburners.OnModulesChanged(__instance);
        // A tow without its hook (taken out of the slot) is over
        VehicleTows.OnModulesChanged(__instance);
    }
}
