using System.Collections.Generic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The crush depth the depth modules give (docs/coop/VEHICLE_UPGRADES.md), free of Unity types so that it can be tested. The game adds an <i>extra</i> number of metres to the base crush depth of
/// the vehicle's prefab and takes the <i>best</i> installed module, not the sum (<c>CrushDamage.SetExtraCrushDepth</c>). The new modules add <see cref="VehicleUpgradeSettings.ExtraDepthPerModule" />
/// per tier to the best vanilla module of their vehicle, so the extras here are the vanilla extras of the top vanilla tier plus tier times step, and the depth shown in a tooltip is the
/// vanilla maximum depth of that tier plus tier times step: the vehicle's base depth cancels out and is not needed.
/// <para>
/// Every client has to compute the same number, because the client that holds the vehicle decides when it takes crush damage and every client's HUD and alarms show their own number: the step
/// always comes from the server's settings and never from a file of the client.
/// </para>
/// </summary>
public static class DepthModuleRules
{
    /// <summary>The extra metres of the best vanilla module: Seamoth Depth Module MK3 (VehicleHullModule3).</summary>
    public const float SEAMOTH_TOP_EXTRA = 700f;

    /// <summary>The extra metres of the best vanilla Prawn Suit module: Prawn Suit Depth Module MK2 (ExoHullModule2).</summary>
    public const float PRAWN_TOP_EXTRA = 800f;

    /// <summary>The extra metres of the best vanilla Cyclops module: Cyclops Depth Module MK3 (CyclopsHullModule3).</summary>
    public const float CYCLOPS_TOP_EXTRA = 1200f;

    /// <summary>The maximum depth in metres of the best vanilla module, as the wiki and the in-game tooltips say (base depth 200 + 700, 900 + 800, 500 + 1200).</summary>
    public const float SEAMOTH_TOP_DEPTH = 900f;

    public const float PRAWN_TOP_DEPTH = 1700f;
    public const float CYCLOPS_TOP_DEPTH = 1700f;

    public static float TopExtra(CoopVehicle vehicle)
    {
        return vehicle switch
        {
            CoopVehicle.Seamoth => SEAMOTH_TOP_EXTRA,
            CoopVehicle.Prawn => PRAWN_TOP_EXTRA,
            _ => CYCLOPS_TOP_EXTRA
        };
    }

    public static float TopDepth(CoopVehicle vehicle)
    {
        return vehicle switch
        {
            CoopVehicle.Seamoth => SEAMOTH_TOP_DEPTH,
            CoopVehicle.Prawn => PRAWN_TOP_DEPTH,
            _ => CYCLOPS_TOP_DEPTH
        };
    }

    /// <summary>The extra crush depth the module of this vehicle and tier gives, on top of the vehicle's base depth.</summary>
    public static float ExtraFor(CoopVehicle vehicle, int tier, float step)
    {
        return TopExtra(vehicle) + tier * step;
    }

    /// <summary>The maximum depth in metres the module of this vehicle and tier gives (what its tooltip says).</summary>
    public static float DepthFor(CoopVehicle vehicle, int tier, float step)
    {
        return TopDepth(vehicle) + tier * step;
    }

    /// <summary>
    /// The extra crush depth of a vehicle: the best of the extras the vanilla modules give (<paramref name="vanillaExtras" />, one per installed vanilla module) and the new modules
    /// (<paramref name="coopTiers" />, the tiers of the installed new modules of this vehicle). The new modules count only when <paramref name="enabled" /> (the server's master switch).
    /// 0 when nothing is installed.
    /// </summary>
    public static float Best(CoopVehicle vehicle, IEnumerable<float> vanillaExtras, IEnumerable<int> coopTiers, bool enabled, float step)
    {
        float best = 0f;
        foreach (float extra in vanillaExtras)
        {
            if (extra > best)
            {
                best = extra;
            }
        }
        if (enabled)
        {
            foreach (int tier in coopTiers)
            {
                float extra = ExtraFor(vehicle, tier, step);
                if (extra > best)
                {
                    best = extra;
                }
            }
        }
        return best;
    }
}
