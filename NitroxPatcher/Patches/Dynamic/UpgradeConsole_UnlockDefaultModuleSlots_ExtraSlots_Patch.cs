using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Cyclops' upgrade console adds its slots with the six names written into the method (not from <c>SubRoot.slotNames</c>): the extra upgrade slots of the server's option are added after them
/// (<see cref="VehicleSlotLists.ExtraCyclopsSlots" />; adding a slot that exists is ignored).
/// </summary>
public sealed partial class UpgradeConsole_UnlockDefaultModuleSlots_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((UpgradeConsole t) => t.UnlockDefaultModuleSlots());

    public static void Postfix(UpgradeConsole __instance)
    {
        if (VehicleSlotLists.Extra > 0 && __instance.modules != null)
        {
            __instance.modules.AddSlots(VehicleSlotLists.ExtraCyclopsSlots);
        }
    }
}
