using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The PDA's equipment screen has an object for every slot the game knows and none for the extra module slots: they are copied from a plain slot of the same vehicle when the screen starts
/// (<see cref="ExtraModuleSlotsScreen" />).
/// </summary>
public sealed partial class uGUI_Equipment_Awake_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_Equipment t) => t.Awake());

    public static void Postfix(uGUI_Equipment __instance)
    {
        ExtraModuleSlotsScreen.AddSlotObjects(__instance);
    }
}
