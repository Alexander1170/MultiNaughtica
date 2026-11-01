using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Prawn Suit recomputes its crush depth only when one of its vanilla depth modules changed. The depth modules of the co-op build live in the same table (<c>Exosuit.crushDepths</c>) but
/// would never trigger that code, so the depth is recomputed after every module change (<see cref="VehicleDepthModules" />).
/// </summary>
public sealed partial class Exosuit_OnUpgradeModuleChange_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Exosuit t) => t.OnUpgradeModuleChange(default, default(TechType), default));

    public static void Postfix(Exosuit __instance)
    {
        VehicleDepthModules.RefreshPrawn(__instance);
    }
}
