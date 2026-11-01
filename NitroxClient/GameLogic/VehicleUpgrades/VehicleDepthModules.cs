using System.Collections.Generic;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// Keeps the crush depth of Seamoths and Prawn Suits right when a module is installed or removed. The game takes the best installed depth module (<see cref="DepthModuleRules" />), but its
/// Seamoth code only knows its own three modules (a table inside the method, which cannot be extended) and its Prawn Suit code recomputes only when a vanilla depth module changed, so a
/// Harmony postfix on both runs this after every module change. The Cyclops needs no code: its table (<c>SubRoot.hullReinforcement</c>) is filled by <see cref="CoopItemRegistry" /> and the game
/// recomputes after every change of its upgrade console.
/// </summary>
public static class VehicleDepthModules
{
    // The table that SeaMoth.OnUpgradeModuleChange builds in the method: extra metres on top of the base crush depth of the Seamoth prefab
    private static readonly Dictionary<TechType, float> SeamothVanillaExtras = new()
    {
        [TechType.SeamothReinforcementModule] = 800f,
        [TechType.VehicleHullModule1] = 100f,
        [TechType.VehicleHullModule2] = 300f,
        [TechType.VehicleHullModule3] = DepthModuleRules.SEAMOTH_TOP_EXTRA
    };

    public static void RefreshSeamoth(SeaMoth seaMoth)
    {
        if (!seaMoth || seaMoth.modules == null || !seaMoth.crushDamage)
        {
            return;
        }

        List<float> vanillaExtras = [];
        List<int> coopTiers = [];
        foreach (string slot in seaMoth.slotIDs)
        {
            TechType techType = seaMoth.modules.GetTechTypeInSlot(slot);
            if (SeamothVanillaExtras.TryGetValue(techType, out float extra))
            {
                vanillaExtras.Add(extra);
            }
            else if (CoopItems.TryGet(techType, out CoopItemDefinition item) && item.Kind == CoopItemKind.DepthModule && item.Vehicle == CoopVehicle.Seamoth)
            {
                coopTiers.Add(item.Tier);
            }
        }

        SetExtraCrushDepth(seaMoth.crushDamage, DepthModuleRules.Best(CoopVehicle.Seamoth, vanillaExtras, coopTiers, VehicleUpgradeSettings.Enabled, VehicleUpgradeSettings.ExtraDepthPerModule));
    }

    /// <summary>The Prawn Suit looks its modules up in <c>Exosuit.crushDepths</c>, to which <see cref="CoopItemRegistry" /> adds the new modules while the vehicle upgrades are on.</summary>
    public static void RefreshPrawn(Exosuit exosuit)
    {
        if (!exosuit || exosuit.modules == null || !exosuit.crushDamage)
        {
            return;
        }

        float best = 0f;
        foreach (string slot in exosuit.slotIDs)
        {
            if (Exosuit.crushDepths.TryGetValue(exosuit.modules.GetTechTypeInSlot(slot), out float extra) && extra > best)
            {
                best = extra;
            }
        }
        SetExtraCrushDepth(exosuit.crushDamage, best);
    }

    private static void SetExtraCrushDepth(CrushDamage crushDamage, float extra)
    {
        // The game announces every change of the crush depth ("CrushDepthNow"): not a second time with the same number
        if (!Mathf.Approximately(crushDamage.extraCrushDepth, extra))
        {
            crushDamage.SetExtraCrushDepth(extra);
        }
    }
}
