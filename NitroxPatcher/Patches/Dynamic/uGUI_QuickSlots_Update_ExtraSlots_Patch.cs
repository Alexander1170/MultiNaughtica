using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The quick bar of a vehicle with extra module slots (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots") and of the player (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots for the player"). It reads the keys 1 to 5
/// and draws the wheel of every slot once a frame, so this is the one place that sees everything that the keys and the bar ask of the vehicle's arrays: before the frame the arrays are made as long as the list of slots
/// (<see cref="VehicleSlotLists.EnsureQuickSlotArrays" />), after it the keys that the game does not have are read: the sixth of a vehicle (<see cref="VehicleQuickSlotKeys.Tick" />) and the sixth, seventh and
/// eighth of the player's own bar (<see cref="PlayerQuickSlotKeys.Tick" />).
/// </summary>
public sealed partial class uGUI_QuickSlots_Update_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_QuickSlots t) => t.Update());

    public static void Prefix(IQuickSlots ___target)
    {
        if (___target is Vehicle vehicle)
        {
            VehicleSlotLists.EnsureQuickSlotArrays(vehicle);
        }
    }

    public static void Postfix(IQuickSlots ___target)
    {
        PatchProbe.Fired("uGUI_QuickSlots.Update");
        VehicleQuickSlotKeys.Tick(___target);
        PlayerQuickSlotKeys.Tick(___target);
    }
}
