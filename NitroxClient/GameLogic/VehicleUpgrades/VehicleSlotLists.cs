using System;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The extra module slots in the game (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots"; the names, their order and the rules are <see cref="ExtraModuleSlotRules" />). The game keeps the slots of a vehicle as
/// static lists of names (<c>SeaMoth._slotIDs</c>, <c>Exosuit._slotIDs</c>, <c>SubRoot.slotNames</c>, all <c>static readonly</c>) and the type of every name in <c>Equipment.slotMapping</c>. The lists are NOT replaced:
/// Mono's JIT may embed the array of a static readonly field into the code it compiles after the class is initialised, so a replaced array would stay in force in every method compiled before the next change
/// (a second session in the same process, a single player game after a co-op one). The lists are read through the patches instead: the getters <c>SeaMoth.slotIDs</c> and <c>Exosuit.slotIDs</c> (virtual, called
/// through <c>Vehicle.slotIDs</c> by the whole game) return the lists of this class, and the one place that reads <c>SubRoot.slotNames</c> (<c>SubRoot.SetCyclopsUpgrades</c>, patched by a transpiler) calls
/// <see cref="CyclopsSlotNames" />. <c>UpgradeConsole.UnlockDefaultModuleSlots</c> has the six names of the Cyclops written into it and gets the extra ones from a patch. <see cref="Apply" /> sets the option, which is
/// done before any vehicle exists (the settings come with the initial sync, and the vehicles of the world are spawned by a processor that depends on it); everything in the game that walks a vehicle's slots does it
/// through those lists, and the arrays of the quick slots are sized from <c>slotIDs.Length</c> when the vehicle starts.
/// </summary>
public static class VehicleSlotLists
{
    private static readonly HashSet<string> dormantReported = [];

    private static string[] seamoth = ExtraModuleSlotRules.Seamoth(0);
    private static string[] exosuit = ExtraModuleSlotRules.Exosuit(0);
    private static string[] cyclops = ExtraModuleSlotRules.Cyclops(0);
    private static string[] cyclopsExtra = ExtraModuleSlotRules.ExtraOf(ExtraSlotKind.Cyclops, 0);

    /// <summary>How many extra slots are in force (0 to 2).</summary>
    public static int Extra { get; private set; }

    /// <summary>The Seamoth's slots now (what <c>SeaMoth.slotIDs</c> answers).</summary>
    public static string[] SeamothSlots => seamoth;

    /// <summary>The Prawn Suit's slots now: its two arms, then its modules (what <c>Exosuit.slotIDs</c> answers).</summary>
    public static string[] ExosuitSlots => exosuit;

    /// <summary>The Cyclops' upgrade slots now (what the game's <c>SubRoot.slotNames</c> would hold).</summary>
    public static string[] CyclopsSlotNames => cyclops;

    /// <summary>How many upgrade slots the Cyclops has now: the number the game's <c>SubRoot.SetCyclopsUpgrades</c> has written into it as 6.</summary>
    public static int CyclopsSlotCount => cyclops.Length;

    /// <summary>The Cyclops' extra upgrade slots now (none, <c>Module7</c> or <c>Module7</c> and <c>Module8</c>): what the upgrade console is given on top of its six.</summary>
    public static string[] ExtraCyclopsSlots => cyclopsExtra;

    /// <summary>
    /// Sets the number of extra slots (clamped) and the lists that go with it; nothing of the game is touched. True when the number changed. The modules that wait in a slot that does not exist are reported once per slot
    /// and per number, so the report starts again when the number changes.
    /// </summary>
    public static bool UseOption(int extra)
    {
        int wanted = ExtraModuleSlotRules.Clamp(extra);
        bool changed = wanted != Extra;
        Extra = wanted;
        seamoth = ExtraModuleSlotRules.Seamoth(wanted);
        exosuit = ExtraModuleSlotRules.Exosuit(wanted);
        cyclops = ExtraModuleSlotRules.Cyclops(wanted);
        cyclopsExtra = ExtraModuleSlotRules.ExtraOf(ExtraSlotKind.Cyclops, wanted);
        if (changed)
        {
            dormantReported.Clear();
        }
        return changed;
    }

    /// <summary>Teaches the game's slot registry the names of the extra slots and their type; harmless to do more than once. The registry is a dictionary whose content is changed, not a field that is replaced.</summary>
    public static void RegisterSlotTypes()
    {
        foreach ((string slot, ExtraSlotKind kind) in ExtraModuleSlotRules.AllExtraSlots)
        {
            Equipment.slotMapping[slot] = TypeOf(kind);
        }
    }

    /// <summary>Puts the lists in line with the option (<paramref name="extra" /> is clamped), teaches the registry the new names and gives the vehicles and consoles that already exist their new slots.</summary>
    public static void Apply(int extra)
    {
        bool changed = UseOption(extra);
        RegisterSlotTypes();
        if (changed)
        {
            Log.Info($"[VehicleUpgrades] extra module slots: {Extra} (Seamoth {seamoth.Length} slots, Prawn Suit {exosuit.Length} with its two arms, Cyclops {cyclops.Length})");
        }
        GiveExistingTheirSlots();
    }

    private static readonly HashSet<int> arraysReported = [];

    /// <summary>
    /// Makes the quick slot arrays of a vehicle as long as its list of slots now, and its table of slot indexes hold all the names. The game sizes them from the list when the vehicle starts
    /// (<c>Vehicle.LazyInitialize</c>); a vehicle that started before the extra slots were in force has four of each, and every key press of the fifth or sixth slot (<c>GetQuickSlotCooldown</c>, <c>SlotKeyDown</c>,
    /// <c>OnUpgradeModuleUse</c>, the quick bar's wheels) then reads past their end. Cheap when nothing is wrong (a few length checks), so it runs every frame for the vehicle that the quick bar shows.
    /// True when an array was grown; the first time for a vehicle it says so in the log.
    /// </summary>
    public static bool EnsureQuickSlotArrays(Vehicle vehicle)
    {
        if (!vehicle || vehicle.quickSlotTimeUsed == null)
        {
            return false;
        }
        string[] slots = vehicle.slotIDs;
        int count = slots.Length;
        int before = vehicle.quickSlotTimeUsed.Length;
        bool grown = false;
        if (vehicle.quickSlotTimeUsed.Length < count)
        {
            Array.Resize(ref vehicle.quickSlotTimeUsed, count);
            grown = true;
        }
        if (vehicle.quickSlotCooldown != null && vehicle.quickSlotCooldown.Length < count)
        {
            Array.Resize(ref vehicle.quickSlotCooldown, count);
            grown = true;
        }
        if (vehicle.quickSlotToggled != null && vehicle.quickSlotToggled.Length < count)
        {
            Array.Resize(ref vehicle.quickSlotToggled, count);
            grown = true;
        }
        if (vehicle.quickSlotCharge != null && vehicle.quickSlotCharge.Length < count)
        {
            Array.Resize(ref vehicle.quickSlotCharge, count);
            grown = true;
        }
        if (vehicle.slotIndexes != null)
        {
            // built from the list when the vehicle started (Vehicle.LazyInitialize): without the new names GetSlotIndex answers -1 for them
            for (int i = 0; i < count; i++)
            {
                if (!vehicle.slotIndexes.TryGetValue(slots[i], out int known) || known != i)
                {
                    vehicle.slotIndexes[slots[i]] = i;
                    grown = true;
                }
            }
        }
        if (grown && arraysReported.Add(vehicle.GetInstanceID()))
        {
            Log.Warn($"[VehicleUpgrades] the quick slot arrays of {vehicle.name} were {before} long for {count} slots (the vehicle started before the extra slots were in force); grown, so that the keys of the fifth and sixth slot can work");
        }
        return grown;
    }

    /// <summary>Back to the game as it is: the end of a session.</summary>
    public static void Reset() => Apply(0);

    /// <summary>
    /// Vehicles and consoles that were started before the option arrived have the old lists: the equipment gets the new slots (adding a slot that exists is ignored) and the arrays of the quick slots, which were
    /// sized from the old list, grow, and so does the table of slot indexes. In a normal session there are none (the settings arrive first), this is for a safe fallback.
    /// </summary>
    private static void GiveExistingTheirSlots()
    {
        arraysReported.Clear();
        foreach (Vehicle vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
        {
            if (vehicle.modules == null)
            {
                continue;
            }
            EnsureQuickSlotArrays(vehicle);
            vehicle.modules.AddSlots(vehicle.slotIDs);
        }
        foreach (UpgradeConsole console in UnityEngine.Object.FindObjectsOfType<UpgradeConsole>())
        {
            if (console.modules != null)
            {
                console.modules.AddSlots(cyclopsExtra);
            }
        }
    }

    /// <summary>
    /// Whether a module that wants to be installed in <paramref name="slot" /> of <paramref name="equipment" /> has to wait: the slot is one of the extra ones and the equipment does not have it (the option was lowered,
    /// or the world is played with an older build). The module is not installed, not touched and not reported as an error; it stays in the save and comes back when the slot exists again. The first such module of a
    /// slot is logged once.
    /// </summary>
    public static bool IsDormant(Equipment equipment, string slot, Nitrox.Model.DataStructures.NitroxId moduleId)
    {
        if (!ExtraModuleSlotRules.IsDormant(slot, equipment.equipment.ContainsKey(slot)))
        {
            return false;
        }
        if (dormantReported.Add(slot))
        {
            Log.Info($"[VehicleUpgrades] extra module slots: the module {moduleId} waits in slot {slot}, which this game does not have (the server allows {Extra} extra slots); it stays in the save and returns with the slot");
        }
        return true;
    }

    private static EquipmentType TypeOf(ExtraSlotKind kind) => kind switch
    {
        ExtraSlotKind.Seamoth => EquipmentType.SeamothModule,
        ExtraSlotKind.Exosuit => EquipmentType.ExosuitModule,
        _ => EquipmentType.CyclopsModule
    };
}
