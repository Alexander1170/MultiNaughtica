#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live batch check u4 : two players put a module into the same slot of a Seamoth (or a Prawn suit or a Cyclops upgrade console) at the same moment, the server decides
// first-wins (ModuleSlotArbiter), the loser's client takes its module back (ModuleSlotRejected, EquipmentSlots.TakeBackModule / ReleaseContestedOccupant). Debug builds only.
//
// The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]"
// (at most two arguments, all lower case) and log one "[SyncHarness] <name>: key=value ..." line per fact (no spaces inside a value). A slot is named by its number (1-based, in the ordinal order of the
// slot names: Seamoth 1..4 = SeamothModule1..4) or by its name. A module is named by its TechType (the first one in the inventory) or by the first 8 characters of its NitroxId (exactly that module).
//
//   u4target [seamoth|prawn|cyclops]   chooses what the other commands work on (default seamoth) and logs the nearest one that has a NitroxId and an Equipment: owner id, slots, distance
//   u4give <TechType> [count]           puts new modules into the inventory (CraftData.InstantiateFromPrefabAsync, NitroxEntity id, Inventory.main.container.AddItem: the path of lockeradd, so that the server
//                                       registers an InventoryItemEntity); one "gave" line per module with its id
//   u4install <slot> <module>           Equipment.AddItem(slot, item, false), the call under Inventory.AddOrSwap that a drop of the PDA makes (Equipment_AddItem_Patch fires as for a drag)
//   u4swap <slot> <module>              Inventory.AddOrSwap(item, equipment, slot) on an OCCUPIED slot (the old module goes into the inventory)
//   u4remove <slot>                     Inventory.AddOrSwap(held, Inventory.main.container): the PDA drag from a slot to the grid
//   u4move <from> <to>                  Inventory.AddOrSwap(held, equipment, toSlot): remove and add in one call (EntityDestroyed, then EntitySpawnedByClient with the same id)
//   u4bounce <slot>                     takes the module out into the inventory and, U4OpGapSeconds later, puts the same module back into the slot (the same player again inside the arbiter's window)
//   u4rapid <slot> <moduleA>:<moduleB>  installs A, then, U4OpGapSeconds later, swaps B in for it (a different module of the SAME player inside the window: the arbiter must never reject that)
//   u4forget <slot>                     destroys the module of a slot on this client only, nothing is sent (the harness-only desync of VEHICLES.md: "a path that skips the packet")
//   u4arm <slot> <module>, u4delay <ms>, u4after <ms>, u4fire
//                                       arm remembers the module and the slot; delay and after are remembered too; fire (usually /consoleall u4fire) installs the armed module now, or after the delay,
//                                       and removes it again after "after" milliseconds (u4after: line). The race of the check: arm on both clients, then one /consoleall u4fire
//   u4state [label]                     one block "begin ... end" with the slots of the target, the equipped counts, orphans and a census of every module object with a NitroxId of the techs involved
//
// Every command that acts logs ONE action line "[SyncHarness] <command>: player= tech= id= slot= owner= result=<True|False|refused|forgotten> why= holds= t=" followed by key=value extras, or
// "[SyncHarness] <command>: failed reason=<token>" when it could not even start. Nothing throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private static string u4Kind = "seamoth";
    private static int u4StateSeq;
    private static InventoryItem u4ArmedItem;
    private static Equipment u4ArmedEquipment;
    private static string u4ArmedSlot;
    private static float u4DelayMs;
    private static float u4RemoveAfterMs;
    private static readonly HashSet<TechType> u4Techs = new HashSet<TechType> { TechType.VehiclePowerUpgradeModule, TechType.VehicleArmorPlating, TechType.VehicleHullModule1 };

    private sealed class U4Target
    {
        public string Kind;
        public Equipment Equipment;
        public NitroxId OwnerId;
        public string Tech;
        public float Distance;
        public Vehicle VehicleComponent;
    }

    private sealed class U4Plan
    {
        public U4Target Target;
        public string Slot;
        public TechType Tech;
        public InventoryItem Item;
    }

    // ---- small helpers

    private static string U4Num(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string U4Num3(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string U4Whole(float value)
    {
        return ((int)value).ToString(CultureInfo.InvariantCulture);
    }

    private static string U4Short(string text)
    {
        return text.Length >= 8 ? text.Substring(0, 8) : text;
    }

    private static string U4Token(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }
        return text.Replace(' ', '_').Replace('|', '_').Replace('=', '_');
    }

    private static string U4Me()
    {
        try
        {
            string name = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>().PlayerName;
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string U4IdOf(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private static string U4IdOfObject(GameObject gameObject)
    {
        return gameObject && gameObject.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private static string U4ItemId(InventoryItem item)
    {
        if (item == null)
        {
            return "none";
        }
        return item.item ? U4IdOf(item.item) : "noid";
    }

    private static string U4PlayerId()
    {
        return Player.main ? U4IdOf(Player.main) : "noid";
    }

    private static void U4Failed(string command, string reason)
    {
        Log.Warn($"[SyncHarness] {command}: failed reason={reason} player={U4Me()}");
    }

    private static void U4Exception(string command, Exception exception)
    {
        Log.Warn($"[SyncHarness] {command}: failed reason=exception-{exception.GetType().Name} {exception.Message}");
    }

    private static bool U4IsHex(string text)
    {
        foreach (char c in text)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex)
            {
                return false;
            }
        }
        return text.Length > 0;
    }

    // ---- target

    private static U4Target U4FindTarget(string kind)
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        U4Target best = null;
        if (kind == "cyclops")
        {
            foreach (UpgradeConsole console in FindObjectsOfType<UpgradeConsole>())
            {
                if (console && console.gameObject.activeInHierarchy && console.modules != null)
                {
                    best = U4Nearer(best, U4MakeTarget("cyclops", console.modules, console.transform.position, here, "UpgradeConsole", null));
                }
            }
            return best;
        }

        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            bool wanted = kind == "prawn" ? (vehicle is Exosuit) : (vehicle is SeaMoth);
            if (wanted && vehicle.gameObject.activeInHierarchy && vehicle.modules != null)
            {
                best = U4Nearer(best, U4MakeTarget(kind, vehicle.modules, vehicle.transform.position, here, kind == "prawn" ? "Exosuit" : "Seamoth", vehicle));
            }
        }
        return best;
    }

    private static U4Target U4Nearer(U4Target current, U4Target candidate)
    {
        if (candidate == null)
        {
            return current;
        }
        if (current == null || candidate.Distance < current.Distance)
        {
            return candidate;
        }
        return current;
    }

    private static U4Target U4MakeTarget(string kind, Equipment equipment, Vector3 position, Vector3 here, string tech, Vehicle vehicle)
    {
        // the id of the owner is the one EquipmentSlots.BroadcastEquip sends as the parent of the module
        NitroxId ownerId = null;
        if (!equipment.owner || !equipment.owner.TryGetNitroxId(out ownerId))
        {
            return null;
        }
        U4Target target = new U4Target();
        target.Kind = kind;
        target.Equipment = equipment;
        target.OwnerId = ownerId;
        target.Tech = tech;
        target.Distance = Vector3.Distance(here, position);
        target.VehicleComponent = vehicle;
        return target;
    }

    private static List<string> U4SlotNames(Equipment equipment)
    {
        List<string> names = new List<string>(equipment.equipment.Keys);
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    private static string U4ResolveSlot(Equipment equipment, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        List<string> names = U4SlotNames(equipment);
        foreach (string name in names)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        int index;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 1 && index <= names.Count)
        {
            return names[index - 1];
        }
        return null;
    }

    private static string U4HoldsId(Equipment equipment, string slot)
    {
        if (equipment == null || slot == null)
        {
            return "none";
        }
        InventoryItem occupant = equipment.GetItemInSlot(slot);
        return occupant != null && occupant.item ? U4IdOf(occupant.item) : "none";
    }

    /// <summary>
    ///     The first item of the inventory that matches: an 8 character hex text is the start of the NitroxId of exactly that module, anything else is a TechType name.
    /// </summary>
    private static InventoryItem U4FindInInventory(string spec, InventoryItem exclude, out TechType tech)
    {
        tech = TechType.None;
        if (string.IsNullOrEmpty(spec) || !Inventory.main || Inventory.main.container == null)
        {
            return null;
        }
        bool byId = spec.Length == 8 && U4IsHex(spec);
        TechType wanted = TechType.None;
        if (!byId && (!Enum.TryParse(spec, true, out wanted) || wanted == TechType.None))
        {
            return null;
        }
        foreach (InventoryItem candidate in Inventory.main.container)
        {
            if (candidate == null || candidate == exclude || !candidate.item)
            {
                continue;
            }
            bool match = byId ? U4IdOf(candidate.item).StartsWith(spec, StringComparison.OrdinalIgnoreCase) : candidate.item.GetTechType() == wanted;
            if (match)
            {
                tech = candidate.item.GetTechType();
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    ///     The target, the slot named by argument 0 and the module named by argument 1 of a command, or null after logging why not.
    /// </summary>
    private static U4Plan U4PrepareItem(NotificationCenter.Notification n, string command)
    {
        U4Target target = U4FindTarget(u4Kind);
        if (target == null)
        {
            U4Failed(command, $"no-{u4Kind}-with-id-and-equipment");
            return null;
        }
        string slot = U4ResolveSlot(target.Equipment, LiveArgString(n, 0, ""));
        if (slot == null)
        {
            U4Failed(command, "unknown-slot");
            return null;
        }
        TechType tech;
        InventoryItem item = U4FindInInventory(LiveArgString(n, 1, ""), null, out tech);
        if (item == null)
        {
            U4Failed(command, "module-not-in-inventory");
            return null;
        }
        U4Plan plan = new U4Plan();
        plan.Target = target;
        plan.Slot = slot;
        plan.Tech = tech;
        plan.Item = item;
        return plan;
    }

    // ---- the game's own paths

    /// <summary>
    ///     The only place that installs: Equipment.AddItem(slot, item, false). A slot that is occupied here is refused by us ("refused", why=slot-occupied), so that a serial install shows as
    ///     such and not as a swap. Returns "True" or "False" (what the game answered) or "refused".
    /// </summary>
    private static string U4DoInstall(Equipment equipment, string slot, InventoryItem item, out string why)
    {
        why = "ok";
        if (equipment == null || slot == null)
        {
            why = "no-target";
            return "refused";
        }
        if (item == null || !item.item)
        {
            why = "no-item";
            return "refused";
        }
        InventoryItem holder = equipment.GetItemInSlot(slot);
        if (holder != null && holder.item)
        {
            why = "slot-occupied";
            return "refused";
        }
        bool added = equipment.AddItem(slot, item, false);
        why = added ? "ok" : "game-refused";
        return added ? "True" : "False";
    }

    private static string U4ActionLine(string command, string tech, string id, string slot, Equipment equipment, string result, string why, string extra)
    {
        string owner = equipment != null && equipment.owner ? U4Short(U4IdOfObject(equipment.owner)) : "noid";
        return $"[SyncHarness] {command}: player={U4Me()} tech={tech} id={id} slot={(slot ?? "none")} owner={owner} result={result} why={why} holds={U4HoldsId(equipment, slot)} t={U4Num3(Time.realtimeSinceStartup)}{extra}";
    }

    private static bool U4InInventory(InventoryItem item)
    {
        return item != null && Inventory.main && object.ReferenceEquals(item.container, Inventory.main.container);
    }

    // ---- commands

    private void LiveCommand_u4target(NotificationCenter.Notification n)
    {
        try
        {
            string kind = LiveArgString(n, 0, "seamoth");
            if (kind != "seamoth" && kind != "prawn" && kind != "cyclops")
            {
                kind = "seamoth";
            }
            u4Kind = kind;
            U4Target target = U4FindTarget(kind);
            if (target == null)
            {
                U4Failed("u4target", $"no-{kind}-with-id-and-equipment");
                return;
            }
            string slots = string.Join(",", U4SlotNames(target.Equipment));
            Log.Info($"[SyncHarness] u4target: player={U4Me()} kind={kind} owner={target.OwnerId} tech={target.Tech} dist={U4Num(target.Distance)} slots={slots} playerId={U4PlayerId()}");
        }
        catch (Exception e)
        {
            U4Exception("u4target", e);
        }
    }

    private void LiveCommand_u4give(NotificationCenter.Notification n)
    {
        try
        {
            TechType tech;
            if (!Enum.TryParse(LiveArgString(n, 0, ""), true, out tech) || tech == TechType.None)
            {
                U4Failed("u4give", "needs-a-TechType");
                return;
            }
            int count = Mathf.Clamp((int)LiveArgFloat(n, 1, 1f), 1, 4);
            u4Techs.Add(tech);
            StartCoroutine(U4GiveRoutine(tech, count));
        }
        catch (Exception e)
        {
            U4Exception("u4give", e);
        }
    }

    private IEnumerator U4GiveRoutine(TechType tech, int count)
    {
        // Like lockeradd: a new object with an id goes into the player's inventory, where Nitrox tells the server about it (InventoryItemEntity). No yield inside a try block
        int given = 0;
        string failure = null;
        for (int i = 0; i < count && failure == null; i++)
        {
            TaskResult<GameObject> prefabResult = new();
            yield return CraftData.InstantiateFromPrefabAsync(tech, prefabResult, false);
            failure = U4GiveOne(tech, prefabResult.Get());
            if (failure == null)
            {
                given++;
            }
        }
        if (failure != null)
        {
            U4Failed("u4give", failure);
        }
        Log.Info($"[SyncHarness] u4give: done player={U4Me()} tech={tech} requested={count} given={given}");
    }

    private static string U4GiveOne(TechType tech, GameObject item)
    {
        try
        {
            if (!item)
            {
                return "no-prefab";
            }
            Pickupable pickupable = item.GetComponent<Pickupable>();
            if (!pickupable)
            {
                UnityEngine.Object.Destroy(item);
                return "no-pickupable";
            }
            pickupable.Initialize();
            NitroxId id = NitroxEntity.GetIdOrGenerateNew(item);
            if (Inventory.main.container.AddItem(pickupable) == null)
            {
                UnityEngine.Object.Destroy(item);
                return "inventory-full";
            }
            Log.Info($"[SyncHarness] u4give: gave player={U4Me()} tech={tech} id={id} inventory={Inventory.main.container.GetCount(tech)}");
            return null;
        }
        catch (Exception e)
        {
            return $"exception-{e.GetType().Name}";
        }
    }

    private void LiveCommand_u4install(NotificationCenter.Notification n)
    {
        try
        {
            U4Plan plan = U4PrepareItem(n, "u4install");
            if (plan == null)
            {
                return;
            }
            string why;
            string result = U4DoInstall(plan.Target.Equipment, plan.Slot, plan.Item, out why);
            Log.Info(U4ActionLine("u4install", plan.Tech.ToString(), U4ItemId(plan.Item), plan.Slot, plan.Target.Equipment, result, why, string.Empty));
        }
        catch (Exception e)
        {
            U4Exception("u4install", e);
        }
    }

    private void LiveCommand_u4swap(NotificationCenter.Notification n)
    {
        try
        {
            U4Plan plan = U4PrepareItem(n, "u4swap");
            if (plan == null)
            {
                return;
            }
            Equipment equipment = plan.Target.Equipment;
            InventoryItem old = equipment.GetItemInSlot(plan.Slot);
            if (old == null || !old.item)
            {
                Log.Info(U4ActionLine("u4swap", plan.Tech.ToString(), U4ItemId(plan.Item), plan.Slot, equipment, "refused", "slot-empty", " old=none oldInInventory=False"));
                return;
            }
            string oldId = U4ItemId(old);
            bool swapped = Inventory.AddOrSwap(plan.Item, equipment, plan.Slot);
            Log.Info(U4ActionLine("u4swap", plan.Tech.ToString(), U4ItemId(plan.Item), plan.Slot, equipment, swapped.ToString(), swapped ? "ok" : "game-refused", $" old={oldId} oldInInventory={U4InInventory(old)}"));
        }
        catch (Exception e)
        {
            U4Exception("u4swap", e);
        }
    }

    private void LiveCommand_u4remove(NotificationCenter.Notification n)
    {
        try
        {
            U4Target target = U4FindTarget(u4Kind);
            string slot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 0, "")) : null;
            if (target == null || slot == null)
            {
                U4Failed("u4remove", target == null ? $"no-{u4Kind}-with-id-and-equipment" : "unknown-slot");
                return;
            }
            InventoryItem held = target.Equipment.GetItemInSlot(slot);
            if (held == null || !held.item)
            {
                Log.Info(U4ActionLine("u4remove", "none", "none", slot, target.Equipment, "refused", "slot-empty", " inInventory=False"));
                return;
            }
            string tech = held.item.GetTechType().ToString();
            string id = U4ItemId(held);
            bool moved = Inventory.AddOrSwap(held, Inventory.main.container);
            Log.Info(U4ActionLine("u4remove", tech, id, slot, target.Equipment, moved.ToString(), moved ? "ok" : "game-refused", $" inInventory={U4InInventory(held)}"));
        }
        catch (Exception e)
        {
            U4Exception("u4remove", e);
        }
    }

    private void LiveCommand_u4move(NotificationCenter.Notification n)
    {
        try
        {
            U4Target target = U4FindTarget(u4Kind);
            string fromSlot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 0, "")) : null;
            string toSlot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 1, "")) : null;
            if (target == null || fromSlot == null || toSlot == null)
            {
                U4Failed("u4move", target == null ? $"no-{u4Kind}-with-id-and-equipment" : "unknown-slot");
                return;
            }
            Equipment equipment = target.Equipment;
            InventoryItem held = equipment.GetItemInSlot(fromSlot);
            if (held == null || !held.item)
            {
                Log.Info(U4ActionLine("u4move", "none", "none", toSlot, equipment, "refused", "slot-empty", $" from={fromSlot} fromHolds=none"));
                return;
            }
            string tech = held.item.GetTechType().ToString();
            string id = U4ItemId(held);
            InventoryItem occupant = equipment.GetItemInSlot(toSlot);
            if (occupant != null && occupant.item)
            {
                Log.Info(U4ActionLine("u4move", tech, id, toSlot, equipment, "refused", "target-occupied", $" from={fromSlot} fromHolds={U4HoldsId(equipment, fromSlot)}"));
                return;
            }
            bool moved = Inventory.AddOrSwap(held, equipment, toSlot);
            Log.Info(U4ActionLine("u4move", tech, id, toSlot, equipment, moved.ToString(), moved ? "ok" : "game-refused", $" from={fromSlot} fromHolds={U4HoldsId(equipment, fromSlot)}"));
        }
        catch (Exception e)
        {
            U4Exception("u4move", e);
        }
    }

    private void LiveCommand_u4bounce(NotificationCenter.Notification n)
    {
        try
        {
            U4Target target = U4FindTarget(u4Kind);
            string slot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 0, "")) : null;
            if (target == null || slot == null)
            {
                U4Failed("u4bounce", target == null ? $"no-{u4Kind}-with-id-and-equipment" : "unknown-slot");
                return;
            }
            Equipment equipment = target.Equipment;
            InventoryItem held = equipment.GetItemInSlot(slot);
            if (held == null || !held.item)
            {
                Log.Info(U4ActionLine("u4bounce", "none", "none", slot, equipment, "refused", "slot-empty", " out=False back=skipped"));
                return;
            }
            // out now and back in U4OpGapSeconds later: the same player puts the same module into the slot again inside the arbiter's window (3 s). Not in one frame: the other client would get the
            // destroy and both spawns of the same id in one Update, an ordering that a human on the PDA cannot produce
            StartCoroutine(U4BounceRoutine(equipment, slot, held, held.item.GetTechType().ToString(), U4ItemId(held)));
        }
        catch (Exception e)
        {
            U4Exception("u4bounce", e);
        }
    }

    // The pause between the two operations of u4bounce and u4rapid: far inside the arbiter's window, long enough for the other client to finish spawning what the first operation sent
    private const float U4OpGapSeconds = 0.5f;

    private IEnumerator U4BounceRoutine(Equipment equipment, string slot, InventoryItem held, string tech, string id)
    {
        // No yield inside a try block: the two halves are in helpers
        bool outOk = U4BounceOut(held);
        if (outOk)
        {
            yield return new WaitForSecondsRealtime(U4OpGapSeconds);
        }
        U4BounceBack(equipment, slot, held, tech, id, outOk);
    }

    private static bool U4BounceOut(InventoryItem held)
    {
        try
        {
            return Inventory.AddOrSwap(held, Inventory.main.container);
        }
        catch (Exception e)
        {
            U4Exception("u4bounce", e);
            return false;
        }
    }

    private static void U4BounceBack(Equipment equipment, string slot, InventoryItem held, string tech, string id, bool outOk)
    {
        try
        {
            string why = "ok";
            string back = outOk ? U4DoInstall(equipment, slot, held, out why) : "skipped";
            bool both = outOk && back == "True";
            Log.Info(U4ActionLine("u4bounce", tech, id, slot, equipment, both.ToString(), both ? "ok" : (outOk ? why : "game-refused"), $" out={outOk} back={back}"));
        }
        catch (Exception e)
        {
            U4Exception("u4bounce", e);
        }
    }

    private void LiveCommand_u4rapid(NotificationCenter.Notification n)
    {
        try
        {
            U4Target target = U4FindTarget(u4Kind);
            string slot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 0, "")) : null;
            if (target == null || slot == null)
            {
                U4Failed("u4rapid", target == null ? $"no-{u4Kind}-with-id-and-equipment" : "unknown-slot");
                return;
            }
            string[] specs = LiveArgString(n, 1, "").Split(':');
            InventoryItem first = specs.Length == 2 ? U4FindInInventory(specs[0], null, out TechType _) : null;
            InventoryItem second = specs.Length == 2 ? U4FindInInventory(specs[1], first, out TechType _) : null;
            if (first == null || second == null)
            {
                U4Failed("u4rapid", "needs-two-modules-in-the-inventory");
                return;
            }
            // A in now, B swapped in for it U4OpGapSeconds later (inside the arbiter's window). Not in one frame: the other client would get the spawn of A, the destroy of A (the swap's RemoveItem) and
            // the two spawns of the swap in one Update, and A's queued spawn would then be dropped or overwritten (an orphan module, a stuck rating) by an ordering that a human on the PDA cannot produce
            StartCoroutine(U4RapidRoutine(target.Equipment, slot, first, second));
        }
        catch (Exception e)
        {
            U4Exception("u4rapid", e);
        }
    }

    private IEnumerator U4RapidRoutine(Equipment equipment, string slot, InventoryItem first, InventoryItem second)
    {
        // No yield inside a try block: the two halves are in helpers. ONE action line at the end (the script judges the first one it finds), or one when the first install is refused
        string tech = second.item ? second.item.GetTechType().ToString() : "none";
        string firstId = U4ItemId(first);
        string secondId = U4ItemId(second);
        if (!U4RapidFirst(equipment, slot, first, tech, firstId, secondId))
        {
            yield break;
        }
        yield return new WaitForSecondsRealtime(U4OpGapSeconds);
        U4RapidSecond(equipment, slot, first, second, tech, firstId, secondId);
    }

    private static bool U4RapidFirst(Equipment equipment, string slot, InventoryItem first, string tech, string firstId, string secondId)
    {
        try
        {
            string whyFirst;
            string firstResult = U4DoInstall(equipment, slot, first, out whyFirst);
            if (firstResult == "True")
            {
                return true;
            }
            Log.Info(U4ActionLine("u4rapid", tech, secondId, slot, equipment, firstResult, $"first-{whyFirst}", $" first={firstId} firstResult={firstResult} second=skipped oldInInventory=False"));
            return false;
        }
        catch (Exception e)
        {
            U4Exception("u4rapid", e);
            return false;
        }
    }

    private static void U4RapidSecond(Equipment equipment, string slot, InventoryItem first, InventoryItem second, string tech, string firstId, string secondId)
    {
        try
        {
            InventoryItem occupant = equipment.GetItemInSlot(slot);
            if (occupant == null || !occupant.item)
            {
                // the module that was just installed is not in the slot any more: a swap would be a plain install, which is not what the step is about
                Log.Info(U4ActionLine("u4rapid", tech, secondId, slot, equipment, "refused", "first-gone", $" first={firstId} firstResult=True second=skipped oldInInventory={U4InInventory(first)}"));
                return;
            }
            bool swapped = Inventory.AddOrSwap(second, equipment, slot);
            Log.Info(U4ActionLine("u4rapid", tech, secondId, slot, equipment, swapped.ToString(), swapped ? "ok" : "game-refused", $" first={firstId} firstResult=True second={swapped} oldInInventory={U4InInventory(first)}"));
        }
        catch (Exception e)
        {
            U4Exception("u4rapid", e);
        }
    }

    private void LiveCommand_u4forget(NotificationCenter.Notification n)
    {
        try
        {
            U4Target target = U4FindTarget(u4Kind);
            string slot = target != null ? U4ResolveSlot(target.Equipment, LiveArgString(n, 0, "")) : null;
            if (target == null || slot == null)
            {
                U4Failed("u4forget", target == null ? $"no-{u4Kind}-with-id-and-equipment" : "unknown-slot");
                return;
            }
            Equipment equipment = target.Equipment;
            InventoryItem held = equipment.GetItemInSlot(slot);
            if (held == null || !held.item)
            {
                Log.Info(U4ActionLine("u4forget", "none", "none", slot, equipment, "refused", "slot-empty", string.Empty));
                return;
            }
            string tech = held.item.GetTechType().ToString();
            string id = U4ItemId(held);
            bool forgotten = false;
            // The sequence of EntityDestroyedProcessor.DestroyPickupable, with nothing sent: the server and the other player keep the module, this client loses it
            using (PacketSuppressor<EntityDestroyed, EntityMetadataUpdate, EntityReparented, EntitySpawnedByClient, PlayerQuickSlotsBindingChanged>.Suppress())
            {
                InventoryItem removed = equipment.RemoveItem(slot, true, false);
                if (removed != null && removed.item)
                {
                    Pickupable pickupable = removed.item;
                    pickupable.OnDestroy();
                    UnityEngine.Object.Destroy(pickupable.gameObject);
                    forgotten = true;
                }
            }
            Log.Info(U4ActionLine("u4forget", tech, id, slot, equipment, forgotten ? "forgotten" : "False", forgotten ? "ok" : "game-refused", string.Empty));
        }
        catch (Exception e)
        {
            U4Exception("u4forget", e);
        }
    }

    private void LiveCommand_u4arm(NotificationCenter.Notification n)
    {
        try
        {
            U4Plan plan = U4PrepareItem(n, "u4arm");
            if (plan == null)
            {
                return;
            }
            u4ArmedItem = plan.Item;
            u4ArmedEquipment = plan.Target.Equipment;
            u4ArmedSlot = plan.Slot;
            bool slotEmpty = plan.Target.Equipment.GetItemInSlot(plan.Slot) == null;
            string owner = U4Short(U4IdOfObject(plan.Target.Equipment.owner));
            Log.Info($"[SyncHarness] u4arm: armed player={U4Me()} tech={plan.Tech} id={U4ItemId(plan.Item)} slot={plan.Slot} owner={owner} slotEmpty={slotEmpty}");
        }
        catch (Exception e)
        {
            U4Exception("u4arm", e);
        }
    }

    private void LiveCommand_u4delay(NotificationCenter.Notification n)
    {
        u4DelayMs = Mathf.Clamp(LiveArgFloat(n, 0, 0f), 0f, 5000f);
        Log.Info($"[SyncHarness] u4delay: delayMs={U4Whole(u4DelayMs)} player={U4Me()}");
    }

    private void LiveCommand_u4after(NotificationCenter.Notification n)
    {
        u4RemoveAfterMs = Mathf.Clamp(LiveArgFloat(n, 0, 0f), 0f, 5000f);
        Log.Info($"[SyncHarness] u4after: removeAfterMs={U4Whole(u4RemoveAfterMs)} player={U4Me()}");
    }

    private void LiveCommand_u4fire(NotificationCenter.Notification n)
    {
        try
        {
            if (u4ArmedItem == null || u4ArmedEquipment == null || u4ArmedSlot == null)
            {
                U4Failed("u4fire", "nothing-armed");
                return;
            }
            InventoryItem item = u4ArmedItem;
            Equipment equipment = u4ArmedEquipment;
            string slot = u4ArmedSlot;
            float delay = u4DelayMs;
            float removeAfter = u4RemoveAfterMs;
            u4ArmedItem = null;
            u4ArmedEquipment = null;
            u4ArmedSlot = null;
            u4DelayMs = 0f;
            u4RemoveAfterMs = 0f;
            if (delay <= 0f)
            {
                // in the frame of the packet: the tightest race the harness can make
                U4FireNow(equipment, slot, item, delay, removeAfter);
            }
            else
            {
                StartCoroutine(U4FireLater(equipment, slot, item, delay, removeAfter));
            }
        }
        catch (Exception e)
        {
            U4Exception("u4fire", e);
        }
    }

    private IEnumerator U4FireLater(Equipment equipment, string slot, InventoryItem item, float delay, float removeAfter)
    {
        yield return new WaitForSecondsRealtime(delay / 1000f);
        try
        {
            U4FireNow(equipment, slot, item, delay, removeAfter);
        }
        catch (Exception e)
        {
            U4Exception("u4fire", e);
        }
    }

    private void U4FireNow(Equipment equipment, string slot, InventoryItem item, float delay, float removeAfter)
    {
        string tech = item != null && item.item ? item.item.GetTechType().ToString() : "none";
        string why;
        string result = U4DoInstall(equipment, slot, item, out why);
        Log.Info(U4ActionLine("u4fire", tech, U4ItemId(item), slot, equipment, result, why, $" delayMs={U4Whole(delay)} removeAfterMs={U4Whole(removeAfter)}"));
        if (result == "True" && removeAfter > 0f)
        {
            StartCoroutine(U4RemoveLater(equipment, slot, item, removeAfter));
        }
    }

    private IEnumerator U4RemoveLater(Equipment equipment, string slot, InventoryItem item, float removeAfter)
    {
        yield return new WaitForSecondsRealtime(removeAfter / 1000f);
        try
        {
            string tech = item != null && item.item ? item.item.GetTechType().ToString() : "none";
            string id = U4ItemId(item);
            InventoryItem held = equipment.GetItemInSlot(slot);
            if (held == null || !held.item)
            {
                Log.Info(U4ActionLine("u4after", tech, id, slot, equipment, "refused", "slot-empty", " inInventory=False"));
            }
            else
            {
                bool moved = Inventory.AddOrSwap(held, Inventory.main.container);
                Log.Info(U4ActionLine("u4after", tech, id, slot, equipment, moved.ToString(), moved ? "ok" : "game-refused", $" inInventory={U4InInventory(held)}"));
            }
        }
        catch (Exception e)
        {
            U4Exception("u4after", e);
        }
    }

    // ---- state

    private void LiveCommand_u4state(NotificationCenter.Notification n)
    {
        u4StateSeq++;
        int seq = u4StateSeq;
        try
        {
            string label = LiveArgString(n, 0, "-");
            Log.Info($"[SyncHarness] u4state: begin seq={seq} label={label} player={U4Me()} playerId={U4PlayerId()} t={U4Num3(Time.realtimeSinceStartup)}");
            HashSet<TechType> techs = new HashSet<TechType>(u4Techs);
            U4Target target = U4FindTarget(u4Kind);
            if (target == null)
            {
                Log.Info($"[SyncHarness] u4state: target none kind={u4Kind}");
            }
            else
            {
                U4DumpTarget(target, techs);
            }
            U4DumpCensus(techs);
        }
        catch (Exception e)
        {
            U4Exception("u4state", e);
        }
        // always the last line, so that a reader knows the block is complete
        Log.Info($"[SyncHarness] u4state: end seq={seq}");
    }

    private static IDictionary U4ReadCounts(Equipment equipment)
    {
        FieldInfo field = typeof(Equipment).GetField("equippedCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(equipment) as IDictionary : null;
    }

    // Vehicle.OnUpgradeModuleChange (IL): enginePowerRating = 1 + count(VehiclePowerUpgradeModule), mirroredSelfDamageFraction = 0.5 * 0.5 ^ count(VehicleArmorPlating)
    private static string U4Rating(Vehicle vehicle)
    {
        if (!vehicle)
        {
            return "n/a";
        }
        FieldInfo field = typeof(Vehicle).GetField("enginePowerRating", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object value = field != null ? field.GetValue(vehicle) : null;
        return (value is float rating) ? U4Num(rating) : "n/a";
    }

    private static string U4Armor(Vehicle vehicle)
    {
        if (!vehicle)
        {
            return "n/a";
        }
        DealDamageOnImpact impact = vehicle.GetComponent<DealDamageOnImpact>();
        return impact ? U4Num(impact.mirroredSelfDamageFraction) : "n/a";
    }

    private static void U4DumpTarget(U4Target target, HashSet<TechType> techs)
    {
        Equipment equipment = target.Equipment;
        List<string> names = U4SlotNames(equipment);
        Dictionary<TechType, int> inSlots = new Dictionary<TechType, int>();
        HashSet<Pickupable> held = new HashSet<Pickupable>();
        List<string> slotLines = new List<string>();
        int occupied = 0;
        for (int i = 0; i < names.Count; i++)
        {
            InventoryItem inSlot = equipment.GetItemInSlot(names[i]);
            Pickupable module = inSlot != null ? inSlot.item : null;
            if (module)
            {
                occupied++;
                TechType slotTech = module.GetTechType();
                techs.Add(slotTech);
                int before;
                if (!inSlots.TryGetValue(slotTech, out before))
                {
                    before = 0;
                }
                inSlots[slotTech] = before + 1;
                held.Add(module);
                string containerState = object.ReferenceEquals(inSlot.container, equipment) ? "ok" : "mismatch";
                slotLines.Add($"[SyncHarness] u4state: slot name={names[i]} index={i + 1} tech={slotTech} id={U4IdOf(module)} container={containerState}");
            }
            else
            {
                slotLines.Add($"[SyncHarness] u4state: slot name={names[i]} index={i + 1} tech=empty id=none container=-");
            }
        }

        // objects in the root of the equipment that no slot holds: what a blind overwrite of a slot leaves behind
        List<string> orphanLines = new List<string>();
        if (equipment.tr)
        {
            foreach (Transform child in equipment.tr)
            {
                Pickupable loose = child.GetComponent<Pickupable>();
                if (loose && !held.Contains(loose))
                {
                    techs.Add(loose.GetTechType());
                    orphanLines.Add($"[SyncHarness] u4state: orphan id={U4IdOf(loose)} tech={loose.GetTechType()} reason=in-equipment-root-not-in-a-slot");
                }
            }
        }

        // equippedCount against the slots (read by reflection, it is a private field): they have to agree for every tech type
        string countsState = "n/a";
        int countedTotal = 0;
        List<string> countedLines = new List<string>();
        IDictionary counted = U4ReadCounts(equipment);
        if (counted != null)
        {
            bool bad = false;
            HashSet<TechType> listed = new HashSet<TechType>();
            foreach (DictionaryEntry entry in counted)
            {
                TechType countedTech = entry.Key is TechType key ? key : TechType.None;
                int countedValue = Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture);
                int slotsValue;
                if (!inSlots.TryGetValue(countedTech, out slotsValue))
                {
                    slotsValue = 0;
                }
                listed.Add(countedTech);
                countedTotal += countedValue;
                bad = bad || countedValue != slotsValue;
                countedLines.Add($"[SyncHarness] u4state: counted tech={countedTech} counted={countedValue} inSlots={slotsValue}");
            }
            foreach (KeyValuePair<TechType, int> pair in inSlots)
            {
                if (!listed.Contains(pair.Key))
                {
                    bad = true;
                    countedLines.Add($"[SyncHarness] u4state: counted tech={pair.Key} counted=0 inSlots={pair.Value}");
                }
            }
            countsState = bad ? "bad" : "ok";
        }

        Log.Info($"[SyncHarness] u4state: target kind={target.Kind} owner={target.OwnerId} tech={target.Tech} dist={U4Num(target.Distance)} slots={names.Count} occupied={occupied} orphans={orphanLines.Count} rating={U4Rating(target.VehicleComponent)} armor={U4Armor(target.VehicleComponent)} counts={countsState} countedTotal={countedTotal}");
        foreach (string line in slotLines)
        {
            Log.Info(line);
        }
        foreach (string line in countedLines)
        {
            Log.Info(line);
        }
        foreach (string line in orphanLines)
        {
            Log.Info(line);
        }
    }

    /// <summary>
    ///     Where a module object is: in a slot of an equipment ("slot:&lt;owner8&gt;/&lt;slot&gt;"), in the equipment but in no slot ("orphan:&lt;owner8&gt;"), in the player's inventory ("inventory"),
    ///     in another container ("other:&lt;name&gt;", which is also where the copy of a remote player's item sits), loose in the world ("world") or nowhere ("detached:&lt;parent&gt;").
    /// </summary>
    private static string U4Where(Pickupable pickupable)
    {
        InventoryItem held = pickupable.inventoryItem;
        IItemsContainer container = held != null ? held.container : null;
        Equipment equipment = container as Equipment;
        if (equipment != null)
        {
            string slot = string.Empty;
            string owner = equipment.owner ? U4Short(U4IdOfObject(equipment.owner)) : "noid";
            return equipment.GetItemSlot(pickupable, ref slot) ? $"slot:{owner}/{U4Token(slot)}" : $"orphan:{owner}";
        }
        ItemsContainer items = container as ItemsContainer;
        if (items != null)
        {
            if (Inventory.main && object.ReferenceEquals(items, Inventory.main.container))
            {
                return "inventory";
            }
            return $"other:{U4Token(items.tr ? items.tr.name : "notr")}";
        }
        if (pickupable.gameObject.activeInHierarchy)
        {
            return "world";
        }
        Transform parent = pickupable.transform.parent;
        return $"detached:{U4Token(parent ? parent.name : "noparent")}";
    }

    /// <summary>
    ///     Every Pickupable object with a NitroxId of the techs involved, active or not (items in containers are inactive): the same id twice means a duplicate or a ghost copy.
    /// </summary>
    private static void U4DumpCensus(HashSet<TechType> techs)
    {
        int rows = 0;
        foreach (Pickupable candidate in Resources.FindObjectsOfTypeAll<Pickupable>())
        {
            if (!candidate || !candidate.gameObject.scene.IsValid())
            {
                continue;
            }
            NitroxId id;
            if (!candidate.TryGetNitroxId(out id))
            {
                continue;
            }
            TechType tech = candidate.GetTechType();
            if (!techs.Contains(tech))
            {
                continue;
            }
            rows++;
            Log.Info($"[SyncHarness] u4state: module id={id} tech={tech} where={U4Where(candidate)} active={candidate.gameObject.activeInHierarchy}");
        }
        Log.Info($"[SyncHarness] u4state: census rows={rows}");
    }
}
#endif
