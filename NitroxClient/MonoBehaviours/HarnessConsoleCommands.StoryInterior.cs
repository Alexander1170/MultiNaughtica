#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Nitrox.Model.DataStructures;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NitroxClient.MonoBehaviours;

// Live check "story_interior" (a harness script that is not part of this repository, script story_interior.ps1): the Aurora interior for two players (STORY.md 6.1 row 11, ST11a, ST11c, ST11d, ST1k): keypad doors,
// laser-cut doors, supply crates and their items, opened by one player and seen by the other, by a late joiner (a returning player and a new one) and after a server restart. Debug builds only.
//
// The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> ..."
// (at most two arguments, lists are comma separated, no value contains a blank) and log "[SyncHarness] <name>: key=value ..." lines that the script reads. Every number is written with the invariant culture,
// nothing is patched, nothing is sent by these commands themselves: they call the same game methods as the hand of a player (OnHandClick, NumberButtonPress, Sealed.Weld, Inventory.Pickup), so that the
// Nitrox patches fire exactly as for a player, and they never throw out of a Unity callback.
//
// An object is named by its KEY: 8 hex digits, the FNV-1a hash of "<kind>|<hierarchy path>|<position of the closed object, 1 decimal>". It is the same on every client that has the object (the interior
// of the Aurora is made of world entities that the server spawns from the batch data, and the same prefab at the same place has the same path and position on every client), so the script can name an
// object on one client and find it on the other without a NitroxId (which an object may lack: that is part of what is measured).
//
//   siprep                     switches the nodamage cheat on (Creative has no no-damage bit, the Aurora fire would hurt) and the NoSurvival cheat (both client-local, cleared by every join, so the script runs it again
//                              after every client start), and logs the game mode, the Aurora position, whether it exploded, the seconds until its countdown starts, the position and the loaded scenes
//   siprobe [all|key,key,..]   one block "begin ... end": a header with the counts of the loaded, the Aurora scene's and the inactive objects of the six kinds (keypad, button, door, laser, crate, panel), then
//                              one row per object (all: the objects in the scene "Aurora" or within 600 m of the exploder, at most 300; keys: exactly those, "missing key=" for one not loaded) with its key,
//                              scene, position, NitroxId on the object itself (own) and on the nearest parent (par), state fields and hierarchy path
//   siact <key> <action>       what the hand of a player does: keypad enter (the four digits of its access code through NumberButtonPress), button button (the unlock button inside), door open|close
//                              (OnHandClick), laser cut (Sealed.Weld in four steps, like the laser cutter), crate open|take (OnHandClick: the first click opens, the second takes the item), panel weldNN
//                              (LiveMixin.AddHealth, NN percent of the maximum). Logs "begin", "effect" (the moment the game method ran), "done" (result=True|False|refused why=) and for a door "slide"
//   siwatch <key,key> <sec>    a per-frame watcher: one "init" line per key with the whole state and one "change" line for every field that changes, with the realtime, so that the script measures how
//                              long a change takes to arrive on the watching client (one watch at a time, a new one stops the old)
//   siitems <TechType>         the items of that tech: in this player's inventory (ids), in supply crates, loose in the loaded scenes, and whether the inventory has room for one more
//   sistand <key,key,..>       a place to stand near each object (raycasts: the direction with the most free room, 0.9 m along it, the floor below), as x,y,z and as rounded integers for /teleport
public partial class HarnessConsoleCommands
{
    private const float SI_NEAR_RADIUS = 600f;
    private const int SI_ROW_CAP = 300;

    private static readonly string[] siKinds = { "keypad", "button", "door", "laser", "crate", "panel" };
    private static int siProbeSeq;
    private Coroutine siWatchRoutine;

    private sealed class SiObject
    {
        public string Kind;
        public string Key;
        public Component Main;
        public string Path;
        public string Scene;
        public Vector3 Pos;
    }

    private sealed class SiField
    {
        public string Name;
        public string Value;
    }

    // ---- small helpers

    private static string SiF0(float value)
    {
        return value.ToString("F0", CultureInfo.InvariantCulture);
    }

    // A value that rounds to zero is written as 0.0, never as -0.0 (Mono writes the sign of a tiny negative number): the key of an object is built from its position and has to be the same text on
    // every client, and the world data has objects at y = 0.0 (the elevator surfaces of the Aurora), whose float noise can differ in sign from one load to the next
    private static string SiF1(float value)
    {
        if (Mathf.Abs(value) < 0.05f)
        {
            value = 0f;
        }

        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string SiF2(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SiF3(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string SiVec(Vector3 value)
    {
        return $"{SiF1(value.x)},{SiF1(value.y)},{SiF1(value.z)}";
    }

    private static string SiBool(bool value)
    {
        return value ? "True" : "False";
    }

    private static string SiShort(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }

        return text.Length >= 8 ? text.Substring(0, 8) : text;
    }

    private static string SiToken(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }

        StringBuilder builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == ' ' || c == '=' || c == '|' || c == '\t' || c == '\r' || c == '\n')
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string SiJoin(List<string> parts)
    {
        return parts == null || parts.Count == 0 ? "none" : string.Join("+", parts.ToArray());
    }

    private static string SiMe()
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

    private static void SiError(string command, Exception exception)
    {
        Log.Warn($"[SyncHarness] {command}: error {exception.GetType().Name}: {SiToken(exception.Message)}");
    }

    private static string SiFullId(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString() : "noid";
    }

    private static string SiMethodName(int method)
    {
        switch (method)
        {
            case 0:
                return "Manual";
            case 1:
                return "Sealed";
            case 2:
                return "Keycard";
            case 3:
                return "Powercell";
            default:
                return $"Other{method}";
        }
    }

    private static bool SiCodeOk(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 4)
        {
            return false;
        }

        foreach (char c in code)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    // The scenes that are loaded right now, names joined with + (spaces as _)
    private static string SiSceneList()
    {
        try
        {
            List<string> names = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                string name = SceneManager.GetSceneAt(i).name;
                names.Add(string.IsNullOrEmpty(name) ? "unnamed" : SiToken(name));
            }

            return names.Count > 0 ? string.Join("+", names.ToArray()) : "none";
        }
        catch (Exception exception)
        {
            return $"error:{exception.GetType().Name}";
        }
    }

    private static bool SiTryAurora(out Vector3 position)
    {
        CrashedShipExploder exploder = CrashedShipExploder.main;
        if (exploder)
        {
            position = exploder.transform.position;
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    // Seconds until the countdown of the Aurora starts (the explosion follows 24 to 27 s after), on the clock the game compares it with; "na" without an exploder
    private static string SiCountdownIn()
    {
        CrashedShipExploder exploder = CrashedShipExploder.main;
        if (!exploder || !DayNightCycle.main)
        {
            return "na";
        }

        return SiF0(exploder.timeToStartCountdown - DayNightCycle.main.timePassedAsFloat);
    }

    private static string SiExploded()
    {
        CrashedShipExploder exploder = CrashedShipExploder.main;
        return exploder ? SiBool(exploder.IsExploded()) : "na";
    }

    // ---- collecting the objects

    private static string SiPathOf(Transform transform)
    {
        StringBuilder builder = new StringBuilder();
        int depth = 0;
        while (transform && depth < 40)
        {
            if (builder.Length > 0)
            {
                builder.Insert(0, '/');
            }

            builder.Insert(0, transform.name);
            transform = transform.parent;
            depth++;
        }

        return SiToken(builder.ToString());
    }

    private static string SiSceneOf(Component component)
    {
        if (!component)
        {
            return "none";
        }

        Scene scene = component.gameObject.scene;
        return scene.IsValid() ? SiToken(scene.name) : "none";
    }

    // The scene "Aurora" (LaunchRocket.HideCrashedShip unloads it by that name) or anything within 600 m of the exploder (the interior rooms, keypads, doors, panels and crates are 160 to 480 m from it,
    // and they are world entities that the server spawns, which is not the same as objects of the scene "Aurora": the census row says in which scene each object really is)
    private static bool SiIsNear(string scene, Vector3 position)
    {
        if (scene == "Aurora")
        {
            return true;
        }

        Vector3 root;
        return SiTryAurora(out root) && Vector3.Distance(root, position) <= SI_NEAR_RADIUS;
    }

    // The position that does not change when the object is used: a door that slides itself (its StarshipDoor sits on the sliding object) is judged by the closed position it recorded in Start
    private static Vector3 SiStablePosition(Component component)
    {
        StarshipDoor door = component as StarshipDoor;
        if (door)
        {
            bool slidesItself = !door.doorObject;
            if (door.doorObject)
            {
                Transform moving = door.doorObject.transform;
                slidesItself = moving == door.transform || door.transform.IsChildOf(moving);
            }

            if (slidesItself && door.closedPos != Vector3.zero)
            {
                return door.closedPos;
            }
        }

        return component.transform.position;
    }

    private static string SiKeyOf(string kind, Component component, string path)
    {
        string text = $"{kind}|{path}|{SiVec(SiStablePosition(component))}";
        uint hash = 2166136261u;
        foreach (char c in text)
        {
            unchecked
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static void SiAddObject(List<SiObject> list, HashSet<string> seen, string kind, Component component)
    {
        if (!component)
        {
            return;
        }

        string path = SiPathOf(component.transform);
        string key = SiKeyOf(kind, component, path);
        if (!seen.Add(key))
        {
            return;
        }

        SiObject entry = new SiObject();
        entry.Kind = kind;
        entry.Key = key;
        entry.Main = component;
        entry.Path = path;
        entry.Scene = SiSceneOf(component);
        entry.Pos = component.transform.position;
        list.Add(entry);
    }

    // Every loaded (active) object of the six kinds, sorted by kind, path and key (the same order on every client). A Sealed counts as a "laser" only when a LaserCutObject sits on it too.
    private static List<SiObject> SiCollect()
    {
        List<SiObject> list = new List<SiObject>();
        HashSet<string> seen = new HashSet<string>();
        foreach (KeypadDoorConsole console in FindObjectsOfType<KeypadDoorConsole>())
        {
            SiAddObject(list, seen, "keypad", console);
        }

        foreach (KeypadDoorConsoleUnlock button in FindObjectsOfType<KeypadDoorConsoleUnlock>())
        {
            SiAddObject(list, seen, "button", button);
        }

        foreach (StarshipDoor door in FindObjectsOfType<StarshipDoor>())
        {
            SiAddObject(list, seen, "door", door);
        }

        foreach (Sealed sealedComponent in FindObjectsOfType<Sealed>())
        {
            if (sealedComponent && sealedComponent.GetComponent<LaserCutObject>())
            {
                SiAddObject(list, seen, "laser", sealedComponent);
            }
        }

        foreach (SupplyCrate crate in FindObjectsOfType<SupplyCrate>())
        {
            SiAddObject(list, seen, "crate", crate);
        }

        foreach (WeldableWallPanelGeneric panel in FindObjectsOfType<WeldableWallPanelGeneric>())
        {
            SiAddObject(list, seen, "panel", panel);
        }

        list.Sort(delegate (SiObject a, SiObject b)
        {
            int byKind = Array.IndexOf(siKinds, a.Kind).CompareTo(Array.IndexOf(siKinds, b.Kind));
            if (byKind != 0)
            {
                return byKind;
            }

            int byPath = string.CompareOrdinal(a.Path, b.Path);
            return byPath != 0 ? byPath : string.CompareOrdinal(a.Key, b.Key);
        });
        return list;
    }

    private static SiObject SiFind(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (SiObject entry in SiCollect())
        {
            if (entry.Key == key)
            {
                return entry;
            }
        }

        return null;
    }

    private static int SiCountInactive<T>() where T : Component
    {
        int count = 0;
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            if (!item || !item.gameObject.scene.IsValid() || item.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (SiIsNear(SiSceneOf(item), item.transform.position))
            {
                count++;
            }
        }

        return count;
    }

    private static int SiCountInactiveLasers()
    {
        int count = 0;
        foreach (Sealed item in Resources.FindObjectsOfTypeAll<Sealed>())
        {
            if (!item || !item.gameObject.scene.IsValid() || item.gameObject.activeInHierarchy || !item.GetComponent<LaserCutObject>())
            {
                continue;
            }

            if (SiIsNear(SiSceneOf(item), item.transform.position))
            {
                count++;
            }
        }

        return count;
    }

    // ---- the state of an object (the same builder for siprobe rows, the before / after of siact and the siwatch lines)

    private static void SiPut(List<SiField> fields, string name, string value)
    {
        SiField field = new SiField();
        field.Name = name;
        field.Value = value;
        fields.Add(field);
    }

    private static string SiStateCsv(List<SiField> fields)
    {
        List<string> parts = new List<string>();
        foreach (SiField field in fields)
        {
            parts.Add($"{field.Name}:{field.Value}");
        }

        return parts.Count == 0 ? "none" : string.Join(",", parts.ToArray());
    }

    private static string SiStateSpaced(List<SiField> fields)
    {
        List<string> parts = new List<string>();
        foreach (SiField field in fields)
        {
            parts.Add($"{field.Name}={field.Value}");
        }

        return string.Join(" ", parts.ToArray());
    }

    // The StarshipDoor objects a console unlocks: the ones under its root (KeypadDoorConsole.AcceptNumberField broadcasts "UnlockDoor" there), else under the console itself
    private static List<string> SiDoorKeysUnder(GameObject scope)
    {
        List<string> keys = new List<string>();
        if (!scope)
        {
            return keys;
        }

        foreach (StarshipDoor door in scope.GetComponentsInChildren<StarshipDoor>(true))
        {
            keys.Add(SiKeyOf("door", door, SiPathOf(door.transform)));
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static List<string> SiDoorKeysOf(KeypadDoorConsole console)
    {
        return SiDoorKeysUnder(console.root ? console.root : console.gameObject);
    }

    private static string SiDoorsLocked(GameObject scope)
    {
        List<string> parts = new List<string>();
        if (scope)
        {
            foreach (StarshipDoor door in scope.GetComponentsInChildren<StarshipDoor>(true))
            {
                parts.Add($"{SiKeyOf("door", door, SiPathOf(door.transform))}:{SiBool(door.doorLocked)}");
            }
        }

        parts.Sort(StringComparer.Ordinal);
        return SiJoin(parts);
    }

    private static List<string> SiKeypadKeysOf(StarshipDoor door)
    {
        List<string> keys = new List<string>();
        foreach (KeypadDoorConsole console in FindObjectsOfType<KeypadDoorConsole>())
        {
            GameObject scope = console.root ? console.root : console.gameObject;
            if (scope && door.transform.IsChildOf(scope.transform))
            {
                keys.Add(SiKeyOf("keypad", console, SiPathOf(console.transform)));
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    // 0 = closed, 1 = open: how far the sliding part has travelled between the closed and the open position the door recorded in Start; -1 when it has not started
    private static float SiSlide(StarshipDoor door)
    {
        if (!door.doorObject)
        {
            return -1f;
        }

        float length = Vector3.Distance(door.closedPos, door.openPos);
        if (length < 0.0001f)
        {
            return -1f;
        }

        return Mathf.Clamp01(Vector3.Distance(door.closedPos, door.doorObject.transform.position) / length);
    }

    private static List<SiField> SiFieldsOf(SiObject obj, bool forWatch)
    {
        List<SiField> fields = new List<SiField>();
        if (obj == null || !obj.Main)
        {
            return fields;
        }

        switch (obj.Kind)
        {
            case "keypad":
                SiKeypadFields((KeypadDoorConsole)obj.Main, fields, forWatch);
                break;
            case "button":
                SiButtonFields((KeypadDoorConsoleUnlock)obj.Main, fields, forWatch);
                break;
            case "door":
                SiDoorFields((StarshipDoor)obj.Main, fields, forWatch);
                break;
            case "laser":
                SiLaserFields((Sealed)obj.Main, fields);
                break;
            case "crate":
                SiCrateFields((SupplyCrate)obj.Main, fields);
                break;
            case "panel":
                SiPanelFields((WeldableWallPanelGeneric)obj.Main, fields, forWatch);
                break;
        }

        return fields;
    }

    private static void SiKeypadFields(KeypadDoorConsole console, List<SiField> fields, bool forWatch)
    {
        string ui = "none";
        if (console.keypadUI && console.keypadUI.activeSelf)
        {
            ui = "numpad";
        }
        else if (console.unlockIcon && console.unlockIcon.activeSelf)
        {
            ui = "unlocked";
        }

        SiPut(fields, "unlocked", SiBool(console.unlocked));
        SiPut(fields, "ui", ui);
        SiPut(fields, "busy", SiBool(console.tempDisable));
        if (!forWatch)
        {
            SiPut(fields, "code", SiCodeOk(console.accessCode) ? console.accessCode : "bad");
            SiPut(fields, "doors", SiJoin(SiDoorKeysOf(console)));
        }
    }

    private static void SiButtonFields(KeypadDoorConsoleUnlock button, List<SiField> fields, bool forWatch)
    {
        SiPut(fields, "unlocked", SiBool(button.unlocked));
        if (!forWatch)
        {
            SiPut(fields, "doors", SiJoin(SiDoorKeysUnder(button.root ? button.root : button.gameObject)));
        }
    }

    private static void SiDoorFields(StarshipDoor door, List<SiField> fields, bool forWatch)
    {
        Sealed sealedComponent = door.GetComponent<Sealed>();
        float slide = SiSlide(door);
        SiPut(fields, "open", SiBool(door.doorOpen));
        SiPut(fields, "locked", SiBool(door.doorLocked));
        SiPut(fields, "method", SiMethodName((int)door.doorOpenMethod));
        if (forWatch)
        {
            SiPut(fields, "slide10", slide < 0f ? "na" : Mathf.RoundToInt(slide * 10f).ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            SiPut(fields, "front", SiBool(door.requirePlayerInFrontToOpen));
            SiPut(fields, "startOpen", SiBool(door.startDoorOpen));
            SiPut(fields, "slide", slide < 0f ? "na" : SiF2(slide));
        }

        SiPut(fields, "sealed", sealedComponent ? SiBool(sealedComponent._sealed) : "na");
        if (!forWatch)
        {
            SiPut(fields, "keypads", SiJoin(SiKeypadKeysOf(door)));
        }
    }

    private static void SiLaserFields(Sealed sealedComponent, List<SiField> fields)
    {
        LaserCutObject cutObject = sealedComponent.GetComponent<LaserCutObject>();
        StarshipDoor door = sealedComponent.GetComponent<StarshipDoor>();
        SupplyCrate crate = sealedComponent.GetComponent<SupplyCrate>();
        SiPut(fields, "sealed", SiBool(sealedComponent._sealed));
        SiPut(fields, "opened", $"{SiF2(sealedComponent.openedAmount)}/{SiF2(sealedComponent.maxOpenedAmount)}");
        SiPut(fields, "cut", cutObject ? SiBool(cutObject.isCutOpen) : "na");
        SiPut(fields, "door", door ? SiBool(door.doorOpen) : "na");
        SiPut(fields, "method", door ? SiMethodName((int)door.doorOpenMethod) : "na");
        SiPut(fields, "isCrate", SiBool(crate != null));
    }

    private static void SiCrateFields(SupplyCrate crate, List<SiField> fields)
    {
        Sealed sealedComponent = crate.GetComponent<Sealed>();
        Pickupable[] all = crate.GetComponentsInChildren<Pickupable>(true);
        Pickupable[] active = crate.GetComponentsInChildren<Pickupable>(false);
        string tech = "none";
        string itemId = "none";
        if (all.Length > 0 && all[0])
        {
            tech = all[0].GetTechType().ToString();
            itemId = all[0].TryGetNitroxId(out NitroxId id) ? SiShort(id.ToString()) : "none";
        }

        SiPut(fields, "open", SiBool(crate.open));
        SiPut(fields, "sealed", sealedComponent ? SiBool(sealedComponent._sealed) : "na");
        SiPut(fields, "itemsAll", all.Length.ToString(CultureInfo.InvariantCulture));
        SiPut(fields, "itemsActive", active.Length.ToString(CultureInfo.InvariantCulture));
        SiPut(fields, "tech", tech);
        SiPut(fields, "itemId", itemId);
    }

    private static void SiPanelFields(WeldableWallPanelGeneric panel, List<SiField> fields, bool forWatch)
    {
        string health = panel.liveMixin ? $"{SiF1(panel.liveMixin.health)}/{SiF1(panel.liveMixin.maxHealth)}" : "na";
        SiPut(fields, "health", health);
        SiPut(fields, "repaired", SiBool(panel.repaired));
        if (!forWatch)
        {
            // The doors the repair of this panel unlocks: WeldableWallPanelGeneric.Start takes the parent of the panel (the panel itself without one) as sendMessageFrom, and the repair sends
            // "UnlockDoor" with BroadcastMessage from there, which reaches every StarshipDoor at or below that object
            Transform parent = panel.transform.parent;
            SiPut(fields, "doors", SiJoin(SiDoorKeysUnder(parent ? parent.gameObject : panel.gameObject)));
        }
    }

    private static string SiParentId(Component component)
    {
        Transform parent = component.transform.parent;
        int depth = 1;
        while (parent && depth < 30)
        {
            NitroxEntity entity = parent.GetComponent<NitroxEntity>();
            if (entity && entity.Id != null)
            {
                return $"{SiShort(entity.Id.ToString())}@{depth}";
            }

            parent = parent.parent;
            depth++;
        }

        return "none";
    }

    private static string SiRowText(SiObject obj, Vector3 origin)
    {
        Component main = obj.Main;
        string own = main.TryGetNitroxId(out NitroxId ownId) ? SiShort(ownId.ToString()) : "none";
        UniqueIdentifier unique = main.GetComponent<UniqueIdentifier>();
        PrefabIdentifier prefab = main.GetComponentInParent<PrefabIdentifier>();
        string uid = unique ? SiShort(unique.id) : "none";
        string cls = prefab ? SiShort(prefab.classId) : "none";
        string active = SiBool(main.gameObject.activeInHierarchy);
        string state = SiStateSpaced(SiFieldsOf(obj, false));
        return $"obj kind={obj.Kind} key={obj.Key} scene={obj.Scene} pos={SiVec(obj.Pos)} dist={SiF1(Vector3.Distance(origin, obj.Pos))} own={own} par={SiParentId(main)} uid={uid} cls={cls} act={active} {state} path={obj.Path}";
    }

    private static string SiCountsText(List<SiObject> all)
    {
        int[] loaded = new int[siKinds.Length];
        int[] inAurora = new int[siKinds.Length];
        foreach (SiObject entry in all)
        {
            int index = Array.IndexOf(siKinds, entry.Kind);
            if (index < 0)
            {
                continue;
            }

            loaded[index]++;
            if (entry.Scene == "Aurora")
            {
                inAurora[index]++;
            }
        }

        int[] inactive =
        {
            SiCountInactive<KeypadDoorConsole>(), SiCountInactive<KeypadDoorConsoleUnlock>(), SiCountInactive<StarshipDoor>(),
            SiCountInactiveLasers(), SiCountInactive<SupplyCrate>(), SiCountInactive<WeldableWallPanelGeneric>()
        };
        List<string> parts = new List<string>();
        for (int i = 0; i < siKinds.Length; i++)
        {
            parts.Add($"ld_{siKinds[i]}={loaded[i]} au_{siKinds[i]}={inAurora[i]} ia_{siKinds[i]}={inactive[i]}");
        }

        return string.Join(" ", parts.ToArray());
    }

    // ---- siprep

    private void LiveCommand_siprep(NotificationCenter.Notification n)
    {
        try
        {
            if (NoDamageConsoleCommand.main)
            {
                NoDamageConsoleCommand.main.SetNoDamageCheat(true);
            }

            GameModeUtils.ActivateCheat(GameModeOption.NoSurvival);
            Vector3 aurora;
            bool hasAurora = SiTryAurora(out aurora);
            string noDamage = NoDamageConsoleCommand.main ? SiBool(NoDamageConsoleCommand.main.GetNoDamageCheat()) : "na";
            string synced = Multiplayer.Main ? SiBool(Multiplayer.Main.InitialSyncCompleted) : "na";
            string position = Player.main ? SiVec(Player.main.transform.position) : "none";
            string auroraScene = CrashedShipExploder.main ? SiSceneOf(CrashedShipExploder.main) : "none";
            Log.Info($"[SyncHarness] siprep: player={SiMe()} mode={GameModeUtils.currentEffectiveMode} nodamage={noDamage} aurora={(hasAurora ? SiVec(aurora) : "none")} auroraScene={auroraScene} exploded={SiExploded()} countdownIn={SiCountdownIn()} pos={position} synced={synced} scenes={SiSceneList()}");
        }
        catch (Exception exception)
        {
            SiError("siprep", exception);
        }
    }

    // ---- siprobe

    private void LiveCommand_siprobe(NotificationCenter.Notification n)
    {
        siProbeSeq++;
        int seq = siProbeSeq;
        bool begun = false;
        try
        {
            string what = LiveArgString(n, 0, "all");
            Vector3 origin = Player.main ? Player.main.transform.position : Vector3.zero;
            Vector3 aurora;
            bool hasAurora = SiTryAurora(out aurora);
            List<SiObject> all = SiCollect();
            string position = Player.main ? SiVec(Player.main.transform.position) : "none";
            Log.Info($"[SyncHarness] siprobe: begin seq={seq} player={SiMe()} mode={what} player_pos={position} aurora={(hasAurora ? SiVec(aurora) : "none")} exploded={SiExploded()} countdownIn={SiCountdownIn()} synced={(Multiplayer.Main ? SiBool(Multiplayer.Main.InitialSyncCompleted) : "na")} total={all.Count} scenes={SiSceneList()} {SiCountsText(all)}");
            begun = true;

            List<SiObject> rows = new List<SiObject>();
            bool truncated = false;
            if (what == "all")
            {
                foreach (SiObject entry in all)
                {
                    if (!SiIsNear(entry.Scene, entry.Pos))
                    {
                        continue;
                    }

                    if (rows.Count >= SI_ROW_CAP)
                    {
                        truncated = true;
                        break;
                    }

                    rows.Add(entry);
                }
            }
            else
            {
                Dictionary<string, SiObject> byKey = new Dictionary<string, SiObject>();
                foreach (SiObject entry in all)
                {
                    byKey[entry.Key] = entry;
                }

                foreach (string key in what.Split(','))
                {
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    SiObject found;
                    if (byKey.TryGetValue(key, out found))
                    {
                        rows.Add(found);
                    }
                    else
                    {
                        Log.Info($"[SyncHarness] siprobe: missing key={key}");
                    }
                }
            }

            foreach (SiObject entry in rows)
            {
                try
                {
                    Log.Info($"[SyncHarness] siprobe: {SiRowText(entry, origin)}");
                }
                catch (Exception exception)
                {
                    SiError("siprobe", exception);
                }
            }

            Log.Info($"[SyncHarness] siprobe: end seq={seq} rows={rows.Count} truncated={SiBool(truncated)}");
        }
        catch (Exception exception)
        {
            SiError("siprobe", exception);
            if (begun)
            {
                Log.Info($"[SyncHarness] siprobe: end seq={seq} rows=-1 truncated=True");
            }
        }
    }

    // ---- siact

    private static void SiActLine(string key, string kind, string action, string phase, string result, string why, string before, string after, string extras)
    {
        StringBuilder text = new StringBuilder();
        text.Append($"[SyncHarness] siact: key={key} kind={kind} action={action} phase={phase} result={result} why={why} player={SiMe()} t={SiF3(Time.realtimeSinceStartup)}");
        if (!string.IsNullOrEmpty(before))
        {
            text.Append($" before={before}");
        }

        if (!string.IsNullOrEmpty(after))
        {
            text.Append($" after={after}");
        }

        if (!string.IsNullOrEmpty(extras))
        {
            text.Append(' ');
            text.Append(extras);
        }

        Log.Info(text.ToString());
    }

    private static void SiActRefused(SiObject obj, string action, string why, string before)
    {
        SiActLine(obj.Key, obj.Kind, action, "done", "refused", why, before, "", "");
    }

    // "effect" is the moment the game method ran (the Nitrox patch sends in the same frame), "done" says how it ended
    private static void SiActDone(SiObject obj, string action, bool ok, string why, string extras)
    {
        string after = SiStateCsv(SiFieldsOf(obj, false));
        if (ok)
        {
            SiActLine(obj.Key, obj.Kind, action, "effect", "True", "ok", "", after, "");
        }

        SiActLine(obj.Key, obj.Kind, action, "done", SiBool(ok), why, "", after, extras);
    }

    private static void SiActFailed(string key, string action, Exception exception)
    {
        SiActLine(key, "unknown", action, "done", "False", $"exception:{SiToken(exception.GetType().Name)}", "", "", $"message={SiToken(exception.Message)}");
    }

    private void LiveCommand_siact(NotificationCenter.Notification n)
    {
        string key = LiveArgString(n, 0, "");
        string action = LiveArgString(n, 1, "").ToLowerInvariant();
        try
        {
            SiObject obj = SiFind(key);
            if (obj == null)
            {
                SiActLine(key, "unknown", action, "done", "refused", "missing-key", "", "", "");
                return;
            }

            string before = SiStateCsv(SiFieldsOf(obj, false));
            SiActLine(obj.Key, obj.Kind, action, "begin", "pending", "ok", before, "", "");
            switch (obj.Kind)
            {
                case "keypad":
                    SiActKeypad(obj, action, before);
                    break;
                case "button":
                    SiActButton(obj, action, before);
                    break;
                case "door":
                    SiActDoor(obj, action, before);
                    break;
                case "laser":
                    SiActLaser(obj, action, before);
                    break;
                case "crate":
                    SiActCrate(obj, action, before);
                    break;
                case "panel":
                    SiActPanel(obj, action, before);
                    break;
                default:
                    SiActRefused(obj, action, "unknown-kind", before);
                    break;
            }
        }
        catch (Exception exception)
        {
            SiActFailed(key, action, exception);
        }
    }

    // keypad enter: the four digits of the access code through NumberButtonPress (what a click on a number button does), then the game's own Invoke("AcceptNumberField", 2) runs
    private void SiActKeypad(SiObject obj, string action, string before)
    {
        KeypadDoorConsole console = (KeypadDoorConsole)obj.Main;
        if (action != "enter" && action != "unlock")
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
        else if (console.unlocked)
        {
            SiActRefused(obj, action, "already-unlocked", before);
        }
        else if (!SiCodeOk(console.accessCode))
        {
            SiActRefused(obj, action, "bad-code", before);
        }
        else if (!console.numberField)
        {
            SiActRefused(obj, action, "no-numberfield", before);
        }
        else
        {
            StartCoroutine(SiEnterRoutine(obj, console, action, before));
        }
    }

    private IEnumerator SiEnterRoutine(SiObject obj, KeypadDoorConsole console, string action, string before)
    {
        if (!SiEnterStart(obj, console, action))
        {
            yield break;
        }

        float end = Time.realtimeSinceStartup + 6f;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            if (!console || (console.unlocked && console.keypadUI && !console.keypadUI.activeSelf))
            {
                break;
            }
        }

        SiEnterFinish(obj, console, action);
    }

    private static bool SiEnterStart(SiObject obj, KeypadDoorConsole console, string action)
    {
        try
        {
            if (console.tempDisable || (console.numberField.text != null && console.numberField.text.Length > 0))
            {
                console.ResetNumberField();
            }

            foreach (char digit in console.accessCode)
            {
                console.NumberButtonPress(digit - '0');
            }

            return true;
        }
        catch (Exception exception)
        {
            SiActFailed(obj.Key, action, exception);
            return false;
        }
    }

    private static void SiEnterFinish(SiObject obj, KeypadDoorConsole console, string action)
    {
        try
        {
            if (!console)
            {
                SiActLine(obj.Key, obj.Kind, action, "done", "False", "console-destroyed", "", "", "");
                return;
            }

            bool finished = console.unlocked && console.keypadUI && !console.keypadUI.activeSelf;
            string why = finished ? "ok" : (console.unlocked ? "accept-timeout" : "code-rejected");
            string extras = $"unlocked={SiBool(console.unlocked)} doorsLocked={SiDoorsLocked(console.root ? console.root : console.gameObject)}";
            SiActDone(obj, action, finished, why, extras);
        }
        catch (Exception exception)
        {
            SiActFailed(obj.Key, action, exception);
        }
    }

    // the unlock button inside the room
    private void SiActButton(SiObject obj, string action, string before)
    {
        KeypadDoorConsoleUnlock button = (KeypadDoorConsoleUnlock)obj.Main;
        if (action != "button" && action != "unlock")
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
        else if (button.unlocked)
        {
            SiActRefused(obj, action, "already-unlocked", before);
        }
        else
        {
            button.UnlockDoorButtonPress();
            SiActDone(obj, action, button.unlocked, button.unlocked ? "ok" : "not-unlocked", $"doorsLocked={SiDoorsLocked(button.root ? button.root : button.gameObject)}");
        }
    }

    // door open | close: the click of the hand (OnHandClick); when the game refuses because the player is not in front of the door, OnDoorToggle is called itself (bypass=True in the line)
    private void SiActDoor(SiObject obj, string action, string before)
    {
        StarshipDoor door = (StarshipDoor)obj.Main;
        bool wantOpen = action == "open";
        if (action != "open" && action != "close")
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
        else if ((int)door.doorOpenMethod != 0)
        {
            SiActRefused(obj, action, $"method-{SiMethodName((int)door.doorOpenMethod)}", before);
        }
        else if (door.doorLocked)
        {
            SiActRefused(obj, action, "locked", before);
        }
        else if (door.doorOpen == wantOpen)
        {
            SiActRefused(obj, action, wantOpen ? "already-open" : "already-closed", before);
        }
        else
        {
            door.OnHandClick((GUIHand)null);
            bool bypass = false;
            if (door.doorOpen != wantOpen)
            {
                door.OnDoorToggle();
                bypass = true;
            }

            bool ok = door.doorOpen == wantOpen;
            SiActDone(obj, action, ok, ok ? "ok" : "not-toggled", $"bypass={SiBool(bypass)} open={SiBool(door.doorOpen)}");
            StartCoroutine(SiSlideRoutine(obj, door, action));
        }
    }

    private IEnumerator SiSlideRoutine(SiObject obj, StarshipDoor door, string action)
    {
        yield return new WaitForSecondsRealtime(3f);
        SiSlideLine(obj, door, action);
    }

    private static void SiSlideLine(SiObject obj, StarshipDoor door, string action)
    {
        try
        {
            float slide = door ? SiSlide(door) : -1f;
            SiActLine(obj.Key, obj.Kind, action, "slide", "True", "ok", "", "", $"slide={(slide < 0f ? "na" : SiF2(slide))}");
        }
        catch (Exception exception)
        {
            SiActFailed(obj.Key, action, exception);
        }
    }

    // laser cut: four steps of a quarter of the maximum, a quarter second apart (the weld ticks of the laser cutter), the fx call the cutter makes while the door is still sealed, then a last step when
    // rounding left a rest; LaserCutObject.Update turns the opened Sealed into the cut door one frame later
    private void SiActLaser(SiObject obj, string action, string before)
    {
        Sealed sealedComponent = (Sealed)obj.Main;
        if (action != "cut")
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
        else if (!sealedComponent._sealed)
        {
            SiActRefused(obj, action, "not-sealed", before);
        }
        else
        {
            StartCoroutine(SiCutRoutine(obj, sealedComponent, action));
        }
    }

    private IEnumerator SiCutRoutine(SiObject obj, Sealed sealedComponent, string action)
    {
        bool extra = false;
        for (int step = 0; step < 4; step++)
        {
            if (!SiCutStep(obj, sealedComponent, action, sealedComponent.maxOpenedAmount * 0.25f))
            {
                yield break;
            }

            if (!sealedComponent._sealed)
            {
                break;
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        if (sealedComponent && sealedComponent._sealed)
        {
            extra = true;
            if (!SiCutStep(obj, sealedComponent, action, sealedComponent.maxOpenedAmount))
            {
                yield break;
            }
        }

        yield return new WaitForSecondsRealtime(1f);
        SiCutFinish(obj, sealedComponent, action, extra);
    }

    private static bool SiCutStep(SiObject obj, Sealed sealedComponent, string action, float amount)
    {
        try
        {
            if (!sealedComponent)
            {
                SiActLine(obj.Key, obj.Kind, action, "done", "False", "sealed-destroyed", "", "", "");
                return false;
            }

            sealedComponent.Weld(amount);
            if (sealedComponent._sealed)
            {
                LaserCutObject cutObject = sealedComponent.GetComponent<LaserCutObject>();
                if (cutObject)
                {
                    cutObject.ActivateFX();
                }
            }
            else
            {
                SiActLine(obj.Key, obj.Kind, action, "effect", "True", "ok", "", SiStateCsv(SiFieldsOf(obj, false)), "");
            }

            return true;
        }
        catch (Exception exception)
        {
            SiActFailed(obj.Key, action, exception);
            return false;
        }
    }

    private static void SiCutFinish(SiObject obj, Sealed sealedComponent, string action, bool extra)
    {
        try
        {
            if (!sealedComponent)
            {
                SiActLine(obj.Key, obj.Kind, action, "done", "False", "sealed-destroyed", "", "", "");
                return;
            }

            LaserCutObject cutObject = sealedComponent.GetComponent<LaserCutObject>();
            bool cutOpen = cutObject && cutObject.isCutOpen;
            bool ok = !sealedComponent._sealed;
            SiActLine(obj.Key, obj.Kind, action, "done", SiBool(ok), ok ? "ok" : "still-sealed", "", SiStateCsv(SiFieldsOf(obj, false)), $"cut={SiBool(cutOpen)} extraStep={SiBool(extra)}");
        }
        catch (Exception exception)
        {
            SiActFailed(obj.Key, action, exception);
        }
    }

    private static int SiInvCount(TechType tech)
    {
        if (!Inventory.main || Inventory.main.container == null)
        {
            return -1;
        }

        return Inventory.main.container.GetCount(tech);
    }

    // crate open: the first click on a closed crate (ToggleOpenState). crate take: the second click (Inventory.Pickup of the item inside); the item is looked at first (tech, id, room)
    private void SiActCrate(SiObject obj, string action, string before)
    {
        SupplyCrate crate = (SupplyCrate)obj.Main;
        Sealed sealedComponent = crate.GetComponent<Sealed>();
        if (action == "open")
        {
            if (crate.open)
            {
                SiActRefused(obj, action, "already-open", before);
            }
            else if (sealedComponent && sealedComponent._sealed)
            {
                SiActRefused(obj, action, "sealed", before);
            }
            else
            {
                crate.OnHandClick((GUIHand)null);
                SiActDone(obj, action, crate.open, crate.open ? "ok" : "not-opened", $"open={SiBool(crate.open)}");
            }
        }
        else if (action == "take")
        {
            SiTakeFromCrate(obj, crate, action, before);
        }
        else
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
    }

    private static void SiTakeFromCrate(SiObject obj, SupplyCrate crate, string action, string before)
    {
        if (!crate.open)
        {
            SiActRefused(obj, action, "not-open", before);
            return;
        }

        crate.FindInsideItemAfterStart();
        Pickupable item = crate.itemInside;
        if (!item)
        {
            SiActRefused(obj, action, "no-item", before);
            return;
        }

        TechType tech = item.GetTechType();
        string idBefore = SiFullId(item);
        int invBefore = SiInvCount(tech);
        bool room = Inventory.main && Inventory.main.container != null && Inventory.main.container.HasRoomFor(item);
        if (!room)
        {
            SiActLine(obj.Key, obj.Kind, action, "done", "refused", "no-room", before, "", $"tech={tech} invBefore={invBefore} room=False");
            return;
        }

        crate.OnHandClick((GUIHand)null);
        int invAfter = SiInvCount(tech);
        bool taken = invAfter == invBefore + 1;
        SiActDone(obj, action, taken, taken ? "ok" : "pickup-failed", $"tech={tech} invBefore={invBefore} invAfter={invAfter} itemId={SiFullId(item)} idBefore={idBefore} room=True");
    }

    // panel weldNN: LiveMixin.AddHealth of NN percent of the maximum (what the welding tool does)
    private void SiActPanel(SiObject obj, string action, string before)
    {
        WeldableWallPanelGeneric panel = (WeldableWallPanelGeneric)obj.Main;
        int percent;
        if (!action.StartsWith("weld", StringComparison.Ordinal) || !int.TryParse(action.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out percent) || percent <= 0)
        {
            SiActRefused(obj, action, "unknown-action", before);
        }
        else if (!panel.liveMixin)
        {
            SiActRefused(obj, action, "no-livemixin", before);
        }
        else if (panel.liveMixin.health >= panel.liveMixin.maxHealth)
        {
            SiActRefused(obj, action, "already-full", before);
        }
        else if (panel.liveMixin.health <= 0f && !panel.liveMixin.canResurrect)
        {
            // LiveMixin.AddHealth does nothing for a dead object that cannot be resurrected (the welding tool cannot repair it either)
            SiActRefused(obj, action, "no-health-to-start-from", before);
        }
        else
        {
            float added = panel.liveMixin.AddHealth(panel.liveMixin.maxHealth * percent / 100f);
            bool grew = added > 0.001f;
            SiActDone(obj, action, grew, grew ? "ok" : "nothing-added", $"added={SiF1(added)} health={SiF1(panel.liveMixin.health)}/{SiF1(panel.liveMixin.maxHealth)}");
        }
    }

    // ---- siwatch

    private void LiveCommand_siwatch(NotificationCenter.Notification n)
    {
        try
        {
            string keys = LiveArgString(n, 0, "");
            float seconds = LiveArgFloat(n, 1, 20f);
            if (siWatchRoutine != null)
            {
                StopCoroutine(siWatchRoutine);
                siWatchRoutine = null;
            }

            siWatchRoutine = StartCoroutine(SiWatchRoutine(keys, seconds));
        }
        catch (Exception exception)
        {
            SiError("siwatch", exception);
        }
    }

    private IEnumerator SiWatchRoutine(string keyList, float seconds)
    {
        List<SiObject> watched = new List<SiObject>();
        List<Dictionary<string, string>> last = new List<Dictionary<string, string>>();
        float limit = Mathf.Clamp(seconds, 1f, 180f);
        SiWatchStart(keyList, watched, last);
        float end = Time.realtimeSinceStartup + limit;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            SiWatchTick(watched, last);
        }

        Log.Info($"[SyncHarness] siwatch: done keys={watched.Count} seconds={SiF1(limit)}");
    }

    private static void SiWatchStart(string keyList, List<SiObject> watched, List<Dictionary<string, string>> last)
    {
        try
        {
            Dictionary<string, SiObject> byKey = new Dictionary<string, SiObject>();
            foreach (SiObject entry in SiCollect())
            {
                byKey[entry.Key] = entry;
            }

            foreach (string key in keyList.Split(','))
            {
                if (key.Length == 0)
                {
                    continue;
                }

                SiObject found;
                if (!byKey.TryGetValue(key, out found))
                {
                    Log.Info($"[SyncHarness] siwatch: missing key={key}");
                    continue;
                }

                List<SiField> fields = SiFieldsOf(found, true);
                Dictionary<string, string> state = new Dictionary<string, string>();
                foreach (SiField field in fields)
                {
                    state[field.Name] = field.Value;
                }

                watched.Add(found);
                last.Add(state);
                Log.Info($"[SyncHarness] siwatch: init key={found.Key} kind={found.Kind} player={SiMe()} t={SiF3(Time.realtimeSinceStartup)} {SiStateSpaced(fields)}");
            }
        }
        catch (Exception exception)
        {
            SiError("siwatch", exception);
        }
    }

    private static void SiWatchTick(List<SiObject> watched, List<Dictionary<string, string>> last)
    {
        try
        {
            for (int i = 0; i < watched.Count; i++)
            {
                SiObject obj = watched[i];
                if (obj == null || !obj.Main)
                {
                    continue;
                }

                Dictionary<string, string> previous = last[i];
                foreach (SiField field in SiFieldsOf(obj, true))
                {
                    string old;
                    if (!previous.TryGetValue(field.Name, out old))
                    {
                        old = "none";
                    }

                    if (old != field.Value)
                    {
                        Log.Info($"[SyncHarness] siwatch: change key={obj.Key} kind={obj.Kind} field={field.Name} old={old} new={field.Value} player={SiMe()} t={SiF3(Time.realtimeSinceStartup)}");
                        previous[field.Name] = field.Value;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            SiError("siwatch", exception);
        }
    }

    // ---- siitems

    private void LiveCommand_siitems(NotificationCenter.Notification n)
    {
        try
        {
            string text = LiveArgString(n, 0, "");
            TechType tech;
            if (!Enum.TryParse(text, true, out tech))
            {
                Log.Warn($"[SyncHarness] siitems: failed reason=unknown-TechType tech={SiToken(text)}");
                return;
            }

            int inventory = SiInvCount(tech);
            List<string> ids = new List<string>();
            if (Inventory.main && Inventory.main.container != null)
            {
                IList<InventoryItem> items = Inventory.main.container.GetItems(tech);
                if (items != null)
                {
                    foreach (InventoryItem item in items)
                    {
                        if (item != null && item.item)
                        {
                            ids.Add(SiShort(SiFullId(item.item)));
                        }
                    }
                }
            }

            int inCrates = 0;
            foreach (SupplyCrate crate in FindObjectsOfType<SupplyCrate>())
            {
                foreach (Pickupable pickupable in crate.GetComponentsInChildren<Pickupable>(true))
                {
                    if (pickupable && pickupable.GetTechType() == tech)
                    {
                        inCrates++;
                    }
                }
            }

            int loose = 0;
            foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
            {
                if (pickupable && !pickupable.attached && pickupable.GetTechType() == tech && !pickupable.GetComponentInParent<SupplyCrate>())
                {
                    loose++;
                }
            }

            bool room = Inventory.main && Inventory.main.container != null && Inventory.main.container.HasRoomFor(tech);
            Log.Info($"[SyncHarness] siitems: tech={tech} player={SiMe()} inv={inventory} ids={SiJoin(ids)} inCrates={inCrates} loose={loose} room={SiBool(room)}");
        }
        catch (Exception exception)
        {
            SiError("siitems", exception);
        }
    }

    // ---- sistand

    private void LiveCommand_sistand(NotificationCenter.Notification n)
    {
        try
        {
            string keyList = LiveArgString(n, 0, "");
            Dictionary<string, SiObject> byKey = new Dictionary<string, SiObject>();
            foreach (SiObject entry in SiCollect())
            {
                byKey[entry.Key] = entry;
            }

            foreach (string key in keyList.Split(','))
            {
                if (key.Length == 0)
                {
                    continue;
                }

                SiObject found;
                if (!byKey.TryGetValue(key, out found))
                {
                    Log.Info($"[SyncHarness] sistand: missing key={key}");
                    continue;
                }

                Log.Info(SiStandText(found));
            }
        }
        catch (Exception exception)
        {
            SiError("sistand", exception);
        }
    }

    // The direction with the most free room (eight horizontal rays of 2.5 m from half a metre above the object), 0.9 m along it (less in a cramped place), then the floor below that point
    private static string SiStandText(SiObject obj)
    {
        Vector3 center = obj.Pos + Vector3.up * 0.5f;
        float bestFree = -1f;
        Vector3 bestDirection = Vector3.forward;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            float free = 2.5f;
            RaycastHit hit;
            if (Physics.Raycast(center, direction, out hit, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                free = hit.distance;
            }

            if (free > bestFree + 0.01f)
            {
                bestFree = free;
                bestDirection = direction;
            }
        }

        float offset = Mathf.Max(0.3f, Mathf.Min(0.9f, bestFree * 0.6f));
        Vector3 above = obj.Pos + bestDirection * offset + Vector3.up * 0.5f;
        Vector3 stand = obj.Pos + Vector3.up * 0.3f;
        string via = "fallback";
        RaycastHit floor;
        if (Physics.Raycast(above, Vector3.down, out floor, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            stand = floor.point + Vector3.up * 0.2f;
            via = "ray";
        }

        string rounded = $"{Mathf.RoundToInt(stand.x)},{Mathf.RoundToInt(stand.y)},{Mathf.RoundToInt(stand.z)}";
        return $"[SyncHarness] sistand: key={obj.Key} kind={obj.Kind} stand={SiVec(stand)} int={rounded} free={SiF2(bestFree)} via={via} pos={SiVec(obj.Pos)}";
    }
}
#endif
