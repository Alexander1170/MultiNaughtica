using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class SeaMoth_OnUpgradeModuleUse_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaMoth t) => t.OnUpgradeModuleUse(default(TechType), default(int)));

    public static bool Prefix(SeaMoth __instance, TechType techType, int slotID)
    {
        switch (techType)
        {
            case TechType.SeamothElectricalDefense:
                Resolve<SeamothModulesEvent>().BroadcastElectricalDefense(techType, slotID, __instance);
                break;
        }

        return true;
    }

    /// <summary>
    /// The game's method ends by writing the cooldown of the module that was used, 0 for a module it does not know: the Seamoth Afterburner (a new instant use module) is handled after that, so that its
    /// cooldown is the last word.
    /// </summary>
    public static void Postfix(SeaMoth __instance, TechType techType, int slotID)
    {
        PatchProbe.Fired("SeaMoth.OnUpgradeModuleUse");
        VehicleAfterburners.OnUse(__instance, techType, slotID);
    }
}
