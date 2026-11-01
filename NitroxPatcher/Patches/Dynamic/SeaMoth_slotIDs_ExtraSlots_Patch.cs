using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Seamoth's list of module slots (<c>Vehicle.slotIDs</c> is virtual and the whole game asks for it through it) is the game's four names plus the extra module slots of the server's option
/// (<see cref="VehicleSlotLists" />). The game's own static readonly list is left alone: Mono's JIT may keep the array of such a field in code it has compiled.
/// </summary>
public sealed partial class SeaMoth_slotIDs_ExtraSlots_Patch : NitroxPatch, IDynamicPatch
{
    // the override that returns the static list of this vehicle, not Vehicle.slotIDs: a lambda like (SeaMoth t) => t.slotIDs is compiled to a call of the property as the base class declares it
    private static readonly MethodInfo TARGET_METHOD = typeof(SeaMoth).GetProperty(nameof(SeaMoth.slotIDs), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).GetGetMethod(true);

    public static void Postfix(ref string[] __result)
    {
        if (VehicleSlotLists.Extra > 0)
        {
            __result = VehicleSlotLists.SeamothSlots;
        }
    }
}
