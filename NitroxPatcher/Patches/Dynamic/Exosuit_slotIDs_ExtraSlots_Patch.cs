using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Prawn Suit's list of slots (its two arms, then its four modules) gets the extra module slots of the server's option appended (<see cref="VehicleSlotLists" />), after the arms and the four modules, so that
/// everything the game addresses by position keeps its place. See <see cref="SeaMoth_slotIDs_ExtraSlots_Patch" /> for why the game's list is not replaced.
/// </summary>
public sealed partial class Exosuit_slotIDs_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    // the override that returns the static list of this vehicle, not Vehicle.slotIDs: a lambda like (Exosuit t) => t.slotIDs is compiled to a call of the property as the base class declares it
    private static readonly MethodInfo TARGET_METHOD = typeof(Exosuit).GetProperty(nameof(Exosuit.slotIDs), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).GetGetMethod(true);

    public static void Postfix(ref string[] __result)
    {
        if (VehicleSlotLists.Extra > 0)
        {
            __result = VehicleSlotLists.ExosuitSlots;
        }
    }
}
