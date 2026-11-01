using System;
using System.Collections.Generic;
using System.Linq;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>Which vehicle an extra module slot belongs to.</summary>
public enum ExtraSlotKind
{
    Seamoth,
    Exosuit,
    Cyclops
}

/// <summary>
/// The extra module slots of the vehicles (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots"): the Seamoth and the Prawn Suit get up to two more module slots, the Cyclops up to two more upgrade slots.
/// The game keeps the slots of a vehicle as a static list of names (<c>SeaMoth._slotIDs</c>, <c>Exosuit._slotIDs</c>, <c>SubRoot.slotNames</c>) and registers every name with its slot type
/// (<c>Equipment.slotMapping</c>); this class holds the names and the order, free of game types, so that they are tested. The extra names are appended at the END of each list: the Prawn Suit's list
/// starts with its two arms and the game addresses the arms and the first modules by position, so nothing that exists moves.
/// </summary>
public static class ExtraModuleSlotRules
{
    public const int MIN_EXTRA = 0;
    public const int MAX_EXTRA = 2;
    public const int DEFAULT_EXTRA = 2;

    /// <summary>The slots of the game as it is (<c>SeaMoth._slotIDs</c>).</summary>
    public static IReadOnlyList<string> VanillaSeamoth { get; } = ["SeamothModule1", "SeamothModule2", "SeamothModule3", "SeamothModule4"];

    /// <summary>The slots of the game as it is (<c>Exosuit._slotIDs</c>): the arms first, then the four modules.</summary>
    public static IReadOnlyList<string> VanillaExosuit { get; } = ["ExosuitArmLeft", "ExosuitArmRight", "ExosuitModule1", "ExosuitModule2", "ExosuitModule3", "ExosuitModule4"];

    /// <summary>The slots of the game as it is (<c>SubRoot.slotNames</c>).</summary>
    public static IReadOnlyList<string> VanillaCyclops { get; } = ["Module1", "Module2", "Module3", "Module4", "Module5", "Module6"];

    private static readonly string[] SeamothExtra = ["SeamothModule5", "SeamothModule6"];
    private static readonly string[] ExosuitExtra = ["ExosuitModule5", "ExosuitModule6"];
    private static readonly string[] CyclopsExtra = ["Module7", "Module8"];

    /// <summary>Every name of an extra slot, with the vehicle it belongs to: what the game's slot registry has to learn.</summary>
    public static IReadOnlyList<(string Slot, ExtraSlotKind Kind)> AllExtraSlots { get; } =
    [
        .. SeamothExtra.Select(slot => (slot, ExtraSlotKind.Seamoth)),
        .. ExosuitExtra.Select(slot => (slot, ExtraSlotKind.Exosuit)),
        .. CyclopsExtra.Select(slot => (slot, ExtraSlotKind.Cyclops))
    ];

    public static int Clamp(int extra) => Math.Max(MIN_EXTRA, Math.Min(MAX_EXTRA, extra));

    /// <summary>The list of the Seamoth's slots with <paramref name="extra" /> more slots (the option, clamped).</summary>
    public static string[] Seamoth(int extra) => Build(VanillaSeamoth, SeamothExtra, extra);

    /// <summary>The list of the Prawn Suit's slots: the vanilla six (arms first), then the extra modules.</summary>
    public static string[] Exosuit(int extra) => Build(VanillaExosuit, ExosuitExtra, extra);

    /// <summary>The list of the Cyclops' upgrade slots.</summary>
    public static string[] Cyclops(int extra) => Build(VanillaCyclops, CyclopsExtra, extra);

    /// <summary>The slots that exist for a vehicle with <paramref name="extra" /> more slots but not in the game as it is: what the vehicles that were created before the option arrived have to be given.</summary>
    public static string[] ExtraOf(ExtraSlotKind kind, int extra) => Pick(kind).Take(Clamp(extra)).ToArray();

    /// <summary>Whether this is the name of one of the extra slots.</summary>
    public static bool IsExtraSlot(string slot) => AllExtraSlots.Any(pair => pair.Slot == slot);

    /// <summary>
    /// Whether the slot exists with that option: every vanilla slot always, an extra slot only when the option covers it (<c>SeamothModule6</c> needs 2, <c>SeamothModule5</c> needs 1). A module that sits in a slot
    /// that does not exist (the option was lowered, or the world is played with an older build) is kept in the save and is not installed (it returns with the slot).
    /// </summary>
    public static bool SlotExists(string slot, int extra)
    {
        foreach (ExtraSlotKind kind in Enum.GetValues(typeof(ExtraSlotKind)))
        {
            string[] names = Pick(kind);
            int index = Array.IndexOf(names, slot);
            if (index >= 0)
            {
                return index < Clamp(extra);
            }
        }
        return true;
    }

    /// <summary>
    /// A module that is installed in an extra slot which the equipment does not have (<paramref name="equipmentHasSlot" /> false) is dormant: the client does not install it, does not complain, and the module stays in the
    /// save until the slot exists again. A slot of the game itself that is missing is not dormancy, it is a fault.
    /// </summary>
    public static bool IsDormant(string slot, bool equipmentHasSlot) => !equipmentHasSlot && IsExtraSlot(slot);

    // ---- the place of the slots on the PDA's equipment screen (measured in the running game, docs/coop/VEHICLE_UPGRADES.md): units of the screen, the origin is the middle of the equipment panel

    /// <summary>The width and the height of a module slot of the PDA screen.</summary>
    public const float SLOT_SIZE = 124f;

    /// <summary>The width and the height of an arm slot of the Prawn Suit.</summary>
    public const float ARM_SIZE = 99f;

    /// <summary>The slots of the game as they are placed (x, y, size), by the id of the slot: what the extra slots must not cover.</summary>
    public static IReadOnlyDictionary<string, (float X, float Y, float Size)> VanillaLayout { get; } = new Dictionary<string, (float X, float Y, float Size)>
    {
        ["SeamothModule1"] = (143f, -149f, SLOT_SIZE),
        ["SeamothModule2"] = (-143f, -149f, SLOT_SIZE),
        ["SeamothModule3"] = (143f, 136f, SLOT_SIZE),
        ["SeamothModule4"] = (-143f, 136f, SLOT_SIZE),
        ["ExosuitArmLeft"] = (215f, -5.5f, ARM_SIZE),
        ["ExosuitArmRight"] = (-215f, -5.5f, ARM_SIZE),
        ["ExosuitModule1"] = (-143f, 136f, SLOT_SIZE),
        ["ExosuitModule2"] = (143f, 136f, SLOT_SIZE),
        ["ExosuitModule3"] = (-143f, -149f, SLOT_SIZE),
        ["ExosuitModule4"] = (143f, -149f, SLOT_SIZE),
        ["Module1"] = (-249f, 136f, SLOT_SIZE),
        ["Module2"] = (-249f, 0f, SLOT_SIZE),
        ["Module3"] = (-249f, -149f, SLOT_SIZE),
        ["Module4"] = (167f, 136f, SLOT_SIZE),
        ["Module5"] = (167f, 0f, SLOT_SIZE),
        ["Module6"] = (167f, -149f, SLOT_SIZE)
    };

    /// <summary>
    /// Where an extra slot goes. The Seamoth gets a middle row (a 2 x 3 grid with the pitch of the rows it has); the Prawn Suit and the Cyclops have no room at the sides (the arm slots; the edge of the screen and the
    /// inventory grid), so their new slots take the middle of the top and the bottom row.
    /// </summary>
    public static (float X, float Y) LayoutOf(string slot) => slot switch
    {
        "SeamothModule5" => (143f, -6.5f),
        "SeamothModule6" => (-143f, -6.5f),
        "ExosuitModule5" => (0f, 136f),
        "ExosuitModule6" => (0f, -149f),
        "Module7" => (-41f, 136f),
        "Module8" => (-41f, -149f),
        _ => throw new ArgumentException($"{slot} is not an extra slot", nameof(slot))
    };

    /// <summary>The id of a plain slot of the same vehicle (no picture of the vehicle on it) whose screen object is copied for an extra slot.</summary>
    public static string TemplateOf(string slot) => slot switch
    {
        "SeamothModule5" or "SeamothModule6" => "SeamothModule2",
        "ExosuitModule5" or "ExosuitModule6" => "ExosuitModule2",
        "Module7" or "Module8" => "Module2",
        _ => throw new ArgumentException($"{slot} is not an extra slot", nameof(slot))
    };

    private static string[] Pick(ExtraSlotKind kind) => kind switch
    {
        ExtraSlotKind.Seamoth => SeamothExtra,
        ExtraSlotKind.Exosuit => ExosuitExtra,
        _ => CyclopsExtra
    };

    private static string[] Build(IReadOnlyList<string> vanilla, string[] extras, int extra) => [.. vanilla, .. extras.Take(Clamp(extra))];
}
