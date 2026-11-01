using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic;

/// <summary>
/// The quick bar of the player has eight slots, not the game's five (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots for the player"): the keys 1 to 5 are the game's own, 6, 7 and 8 are Nitrox key bindings that
/// <see cref="PlayerQuickSlotKeys" /> reads. The game builds the bar in <c>Inventory.Awake</c> with the number 5 written into it as a literal, and indexes its static list of slot names with the number of the slot (six
/// names, QuickSlot0 to QuickSlot5, for a bar of five); the number and the names are here, and the patches put them in the game's place. The server and the packets keep a list of bindings of whatever length the
/// client has, so nothing about the save changes: a save with five bindings is a bar whose last three slots are empty. A vehicle has a bar of its own (its modules), which this does not touch.
/// </summary>
public static class PlayerQuickSlots
{
    /// <summary>The slots of the player's quick bar: the game's five and three more (the keys 6, 7 and 8).</summary>
    public const int COUNT = 8;

    /// <summary>The slots the game has: <c>Inventory.Awake</c> makes the bar with this number.</summary>
    public const int GAME_COUNT = 5;

    /// <summary>The number that the first extra key hands to <c>IQuickSlots.SlotKeyDown</c>: the keys 1 to 5 are 0 to 4, so key 6 is 5.</summary>
    public const int FIRST_EXTRA_KEY_NUMBER = GAME_COUNT;

    /// <summary>The keys the game does not have: 6, 7 and 8.</summary>
    public const int EXTRA_KEYS = COUNT - GAME_COUNT;

    private static readonly string[] slotNames = MakeNames(COUNT);

    /// <summary>
    /// The names of the slots in the game's equipment events, indexed by the number of the slot, one for each of the eight (the game's list has six and its code reads it with the number of the slot, so the sixth and
    /// seventh slot would read past its end). The first six are the game's own names. Do not change the array: the game reads it.
    /// </summary>
    public static string[] SlotNames => slotNames;

    /// <summary>The names QuickSlot0, QuickSlot1 and so on, <paramref name="count" /> of them (the game's own naming).</summary>
    public static string[] MakeNames(int count)
    {
        string[] names = new string[Math.Max(0, count)];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = $"QuickSlot{i}";
        }
        return names;
    }

    /// <summary>
    /// How many of the bindings a saved list holds can be put on a bar of <paramref name="slotCount" /> slots: all of them, unless the bar is shorter than the list (a build whose patch did not take hold keeps the
    /// game's five, and must not write past them with the eight of a save).
    /// </summary>
    public static int SlotsToRestore(int savedLength, int slotCount)
    {
        return Math.Max(0, Math.Min(savedLength, slotCount));
    }

    /// <summary>
    /// The slots a saved list of <paramref name="savedLength" /> bindings says nothing about on a bar of <paramref name="slotCount" /> slots: a save from before the bar had eight slots lists five, so the other
    /// three are to be emptied at the join (the game binds a tool to a free slot as the items come into the inventory, and that must not outlast the join: the saved bar is the truth).
    /// </summary>
    public static IReadOnlyList<int> SlotsWithoutASavedBinding(int savedLength, int slotCount)
    {
        List<int> slots = [];
        for (int slot = Math.Max(0, savedLength); slot < slotCount; slot++)
        {
            slots.Add(slot);
        }
        return slots;
    }
}
