#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Live batch check b8b9 : B8 scanner room upgrade chips restored after a join or a resync, B9 scanner room scan target and fire extinguisher fuel.
///     Every command prints one final line in all cases (<c>done</c> or <c>failed reason=...</c>) with key=value pairs, because the scenario script reads the client log.<br/>
///     - <c>maproomstate</c> logs every scanner room of the scene (id, base id, chips by type, the scan range and interval the room holds now and the ones its chips imply, scan target)<br/>
///     - <c>maproomadd &lt;TechType&gt; &lt;count&gt;</c> puts upgrade chips into the container of the scanner room nearest to the player, the way lockeradd does it (first in the inventory, where the server knows them, then moved)<br/>
///     - <c>maproomtarget &lt;TechType&gt;</c> sets the scan target of the nearest scanner room through <c>MapRoomFunctionality.StartScanning</c>, which every choice on the room's screen goes through<br/>
///     - <c>extinguishergive</c> puts a new fire extinguisher into the inventory, <c>extinguisheruse [seconds]</c> calls <c>FireExtinguisher.UseExtinguisher</c> every frame for that long (what the tool's own Update does while the
///     button is held), <c>extinguisherholster [fuel]</c> sets the fuel and calls <c>PlayerTool.OnHolster</c> (what putting the tool away calls), <c>extinguisherdrop</c> drops it, <c>extinguisherstate</c> logs every extinguisher of the
///     scene with its id and fuel, <c>extinguisherpick &lt;id prefix&gt;</c> picks a dropped one up
/// </summary>
public partial class HarnessConsoleCommands
{
    private static FireExtinguisher b8b9Extinguisher;

    private static string B8b9Num(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static float B8b9ReadFloat(object target, string fieldName, float fallback)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.GetValue(target) is float value)
        {
            return value;
        }
        return fallback;
    }

    private static string B8b9IdOf(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString() : "none";
    }

    private static string B8b9IntroActive()
    {
        PropertyInfo property = typeof(IntroLifepodDirector).GetProperty("IsActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
        {
            return "unknown";
        }
        object value = property.GetValue(null, null);
        return value != null ? value.ToString() : "unknown";
    }

    // ---- B8 / B9: scanner room

    private static List<MapRoomFunctionality> B8b9Rooms()
    {
        List<MapRoomFunctionality> rooms = [];
        foreach (MapRoomFunctionality room in Resources.FindObjectsOfTypeAll<MapRoomFunctionality>())
        {
            // FindObjectsOfTypeAll also returns prefab assets (no valid scene) and objects of hidden scenes
            if (room && room.gameObject.scene.IsValid())
            {
                rooms.Add(room);
            }
        }
        return rooms;
    }

    private static MapRoomFunctionality B8b9NearestRoom()
    {
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        MapRoomFunctionality best = null;
        float bestScore = float.MaxValue;
        foreach (MapRoomFunctionality room in B8b9Rooms())
        {
            // a room with a NitroxId that is active beats one without, then the distance decides
            float score = Vector3.Distance(room.transform.position, here);
            score += room.TryGetNitroxId(out NitroxId _) ? 0f : 1000000f;
            score += room.gameObject.activeInHierarchy ? 0f : 100000f;
            if (score < bestScore)
            {
                bestScore = score;
                best = room;
            }
        }
        return best;
    }

    private static string B8b9DescribeRoom(MapRoomFunctionality room)
    {
        Base parentBase = room.GetComponentInParent<Base>();
        string baseId = parentBase ? B8b9IdOf(parentBase) : "none";
        ItemsContainer container = room.storageContainer ? room.storageContainer.container : null;
        int rangeChips = container != null ? container.GetCount(TechType.MapRoomUpgradeScanRange) : -1;
        int speedChips = container != null ? container.GetCount(TechType.MapRoomUpgradeScanSpeed) : -1;
        int itemCount = container != null ? container.count : -1;
        List<string> items = [];
        if (container != null)
        {
            foreach (TechType techType in container.GetItemTypes())
            {
                items.Add($"{techType}:{container.GetCount(techType)}");
            }
        }
        items.Sort();

        // MapRoomFunctionality.UpdateScanRangeAndInterval: scanRange = min(500, 300 + 50 per MapRoomUpgradeScanRange), scanInterval = max(1, 14 - 3 per MapRoomUpgradeScanSpeed)
        float expectedRange = Mathf.Min(500f, 300f + Mathf.Max(0, rangeChips) * 50f);
        float expectedInterval = Mathf.Max(1f, 14f - Mathf.Max(0, speedChips) * 3f);
        float interval = B8b9ReadFloat(room, "scanInterval", -1f);
        float distance = Player.main ? Vector3.Distance(Player.main.transform.position, room.transform.position) : -1f;

        string roomId = B8b9IdOf(room);
        string rangeText = B8b9Num(room.GetScanRange());
        string expectedRangeText = B8b9Num(expectedRange);
        string intervalText = B8b9Num(interval);
        string expectedIntervalText = B8b9Num(expectedInterval);
        string distanceText = B8b9Num(distance);
        string itemText = string.Join(",", items);
        bool active = room.gameObject.activeInHierarchy;
        return $"[SyncHarness] maproomstate: room id={roomId} base={baseId} rangeChips={rangeChips} speedChips={speedChips} items=[{itemText}] itemCount={itemCount} range={rangeText} expRange={expectedRangeText} interval={intervalText} expInterval={expectedIntervalText} type={room.typeToScan} active={active} dist={distanceText}";
    }

    private void LiveCommand_maproomstate(NotificationCenter.Notification n)
    {
        int count = 0;
        foreach (MapRoomFunctionality room in B8b9Rooms())
        {
            count++;
            string line = B8b9DescribeRoom(room);
            Log.Info(line);
        }
        Log.Info($"[SyncHarness] maproomstate: {count} scanner rooms");
    }

    private void LiveCommand_maproomadd(NotificationCenter.Notification n)
    {
        if (!System.Enum.TryParse(LiveArgString(n, 0, ""), true, out TechType techType) || techType == TechType.None)
        {
            Log.Warn("[SyncHarness] maproomadd: failed reason=needs-a-TechType (MapRoomUpgradeScanRange or MapRoomUpgradeScanSpeed) and a count");
            return;
        }
        int amount = Mathf.Clamp((int)LiveArgFloat(n, 1, 1f), 1, 8);
        MapRoomFunctionality room = B8b9NearestRoom();
        if (!room || !room.storageContainer || room.storageContainer.container == null)
        {
            Log.Warn("[SyncHarness] maproomadd: failed reason=no-scanner-room-with-a-container");
            return;
        }
        StartCoroutine(B8b9AddChipsAsync(room, techType, amount));
    }

    private static IEnumerator B8b9AddChipsAsync(MapRoomFunctionality room, TechType techType, int amount)
    {
        // Like lockeradd: the chips are first in the player's inventory (the server knows them there), then they are moved into the container of the room
        // (ItemsContainer.AddItem asks the room whether it accepts the chip and raises the add event Nitrox sends to the server)
        ItemsContainer target = room.storageContainer.container;
        string roomId = B8b9IdOf(room);
        List<Pickupable> inInventory = [];
        for (int i = 0; i < amount; i++)
        {
            TaskResult<GameObject> prefabResult = new();
            yield return CraftData.InstantiateFromPrefabAsync(techType, prefabResult, false);
            GameObject item = prefabResult.Get();
            Pickupable pickupable = item ? item.GetComponent<Pickupable>() : null;
            if (!pickupable)
            {
                break;
            }
            pickupable.Initialize();
            NitroxEntity.GetIdOrGenerateNew(item);
            if (Inventory.main.container.AddItem(pickupable) == null)
            {
                Object.Destroy(item);
                break;
            }
            inInventory.Add(pickupable);
        }
        yield return new WaitForSeconds(3f);

        int moved = 0;
        int refused = 0;
        foreach (Pickupable pickupable in inInventory)
        {
            if (!pickupable || !target.HasRoomFor(pickupable))
            {
                refused++;
                continue;
            }
            Inventory.main.container.RemoveItem(pickupable, true);
            if (target.AddItem(pickupable) != null)
            {
                moved++;
            }
            else
            {
                // not accepted after all: back into the inventory, so that no orphan item is left
                refused++;
                Inventory.main.container.AddItem(pickupable);
            }
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        int total = target.GetCount(techType);
        string line = $"[SyncHarness] maproomadd: done tech={techType} requested={amount} inInventory={inInventory.Count} moved={moved} refused={refused} room={roomId} roomNowHolds={total} containerCount={target.count}";
        Log.Info(line);
    }

    private void LiveCommand_maproomtarget(NotificationCenter.Notification n)
    {
        if (!System.Enum.TryParse(LiveArgString(n, 0, ""), true, out TechType techType))
        {
            Log.Warn("[SyncHarness] maproomtarget: failed reason=needs-a-TechType (Quartz, Titanium, ...)");
            return;
        }
        MapRoomFunctionality room = B8b9NearestRoom();
        if (!room)
        {
            Log.Warn("[SyncHarness] maproomtarget: failed reason=no-scanner-room");
            return;
        }
        TechType before = room.typeToScan;
        try
        {
            room.StartScanning(techType);
        }
        catch (System.Exception exception)
        {
            string failure = $"[SyncHarness] maproomtarget: failed reason=exception {exception.GetType().Name} {exception.Message}";
            Log.Warn(failure);
            return;
        }
        string line = $"[SyncHarness] maproomtarget: done room={B8b9IdOf(room)} before={before} after={room.typeToScan}";
        Log.Info(line);
    }

    // ---- B9: fire extinguisher

    private static FireExtinguisher B8b9FindInInventory()
    {
        foreach (InventoryItem item in Inventory.main.container)
        {
            if (item != null && item.item && item.item.GetTechType() == TechType.FireExtinguisher)
            {
                FireExtinguisher extinguisher = item.item.GetComponent<FireExtinguisher>();
                if (extinguisher)
                {
                    return extinguisher;
                }
            }
        }
        return null;
    }

    private static FireExtinguisher B8b9CurrentExtinguisher()
    {
        // the one extinguishergive made (it stays the current one after it was dropped), else the first one in the inventory
        return b8b9Extinguisher ? b8b9Extinguisher : B8b9FindInInventory();
    }

    private void LiveCommand_extinguishergive(NotificationCenter.Notification n)
    {
        StartCoroutine(B8b9ExtinguisherGiveAsync());
    }

    private static IEnumerator B8b9ExtinguisherGiveAsync()
    {
        TaskResult<GameObject> prefabResult = new();
        yield return CraftData.InstantiateFromPrefabAsync(TechType.FireExtinguisher, prefabResult, false);
        GameObject item = prefabResult.Get();
        Pickupable pickupable = item ? item.GetComponent<Pickupable>() : null;
        FireExtinguisher extinguisher = item ? item.GetComponent<FireExtinguisher>() : null;
        if (!pickupable || !extinguisher)
        {
            string missing = $"[SyncHarness] extinguishergive: failed reason=no-component item={(item ? item.name : "null")} pickupable={(bool)pickupable} fireExtinguisher={(bool)extinguisher}";
            Log.Warn(missing);
            if (item)
            {
                Object.Destroy(item);
            }
            yield break;
        }

        // The id first (the container event needs it), then into the inventory: Nitrox sends the new item to the server with the metadata of its extractor
        pickupable.Initialize();
        NitroxId id = NitroxEntity.GetIdOrGenerateNew(item);
        if (Inventory.main.container.AddItem(pickupable) == null)
        {
            Log.Warn($"[SyncHarness] extinguishergive: failed reason=inventory-full id={id}");
            Object.Destroy(item);
            yield break;
        }
        b8b9Extinguisher = extinguisher;
        string line = $"[SyncHarness] extinguishergive: created id={id} fuel={B8b9Num(extinguisher.fuel)} max={B8b9Num(extinguisher.maxFuel)} inInventory={pickupable.inventoryItem != null}";
        Log.Info(line);
    }

    private void LiveCommand_extinguisheruse(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 8f), 1f, 60f);
        FireExtinguisher extinguisher = B8b9CurrentExtinguisher();
        if (!extinguisher)
        {
            Log.Warn("[SyncHarness] extinguisheruse: failed reason=no-extinguisher (run extinguishergive first)");
            return;
        }
        StartCoroutine(B8b9UseExtinguisherAsync(extinguisher, seconds));
    }

    private static bool B8b9TryUse(FireExtinguisher extinguisher, float douse, float expend)
    {
        try
        {
            extinguisher.UseExtinguisher(douse, expend);
            return true;
        }
        catch (System.Exception exception)
        {
            string failure = $"[SyncHarness] extinguisheruse: UseExtinguisher threw {exception.GetType().Name} {exception.Message}";
            Log.Warn(failure);
            return false;
        }
    }

    private static IEnumerator B8b9UseExtinguisherAsync(FireExtinguisher extinguisher, float seconds)
    {
        // The tool's own Update calls UseExtinguisher(fireDousePerSecond * dt, expendFuelPerSecond * dt) every frame while the button is held and the tool is drawn;
        // the same call from here (the Harmony postfix sits on UseExtinguisher itself)
        string id = B8b9IdOf(extinguisher);
        float douseRate = B8b9ReadFloat(extinguisher, "fireDousePerSecond", 0f);
        float expendRate = B8b9ReadFloat(extinguisher, "expendFuelPerSecond", 1f);
        float before = extinguisher.fuel;
        // the spray effect belongs to a drawn tool and is not needed for the fuel
        extinguisher.fxControl = null;
        float elapsed = 0f;
        int calls = 0;
        int failures = 0;
        while (elapsed < seconds && extinguisher && failures < 3)
        {
            float step = Time.deltaTime;
            elapsed += step;
            if (B8b9TryUse(extinguisher, douseRate * step, expendRate * step))
            {
                calls++;
            }
            else
            {
                failures++;
            }
            yield return null;
        }
        float after = extinguisher ? extinguisher.fuel : -1f;
        float max = extinguisher ? extinguisher.maxFuel : -1f;
        string line = $"[SyncHarness] extinguisheruse: done id={id} seconds={B8b9Num(elapsed)} calls={calls} failures={failures} fuelBefore={B8b9Num(before)} fuelAfter={B8b9Num(after)} max={B8b9Num(max)} expendRate={B8b9Num(expendRate)} introActive={B8b9IntroActive()}";
        Log.Info(line);
    }

    private void LiveCommand_extinguisherholster(NotificationCenter.Notification n)
    {
        FireExtinguisher extinguisher = B8b9CurrentExtinguisher();
        if (!extinguisher)
        {
            Log.Warn("[SyncHarness] extinguisherholster: failed reason=no-extinguisher");
            return;
        }
        float before = extinguisher.fuel;
        // The fuel is written directly (no UseExtinguisher call, so the use patch cannot have sent it): only the holster patch can bring this value to the server
        extinguisher.fuel = Mathf.Clamp(LiveArgFloat(n, 0, 25f), 0f, extinguisher.maxFuel);
        bool threw = false;
        try
        {
            extinguisher.OnHolster();
        }
        catch (System.Exception exception)
        {
            threw = true;
            string failure = $"[SyncHarness] extinguisherholster: OnHolster threw {exception.GetType().Name} {exception.Message}";
            Log.Warn(failure);
        }
        string line = $"[SyncHarness] extinguisherholster: done id={B8b9IdOf(extinguisher)} fuelBefore={B8b9Num(before)} fuelSet={B8b9Num(extinguisher.fuel)} threw={threw}";
        Log.Info(line);
    }

    private void LiveCommand_extinguisherdrop(NotificationCenter.Notification n)
    {
        FireExtinguisher extinguisher = B8b9CurrentExtinguisher();
        Pickupable pickupable = extinguisher ? extinguisher.GetComponent<Pickupable>() : null;
        if (!pickupable)
        {
            Log.Warn("[SyncHarness] extinguisherdrop: failed reason=no-extinguisher");
            return;
        }
        string id = B8b9IdOf(extinguisher);
        float fuel = extinguisher.fuel;
        bool dropped = Inventory.main.InternalDropItem(pickupable, false);
        string line = $"[SyncHarness] extinguisherdrop: done id={id} dropped={dropped} fuel={B8b9Num(fuel)}";
        Log.Info(line);
    }

    private void LiveCommand_extinguisherstate(NotificationCenter.Notification n)
    {
        int count = 0;
        foreach (FireExtinguisher extinguisher in Resources.FindObjectsOfTypeAll<FireExtinguisher>())
        {
            if (!extinguisher || !extinguisher.gameObject.scene.IsValid())
            {
                continue;
            }
            count++;
            Pickupable pickupable = extinguisher.GetComponent<Pickupable>();
            bool inContainer = pickupable && pickupable.inventoryItem != null;
            bool active = extinguisher.gameObject.activeInHierarchy;
            string line = $"[SyncHarness] extinguisherstate: ext id={B8b9IdOf(extinguisher)} fuel={B8b9Num(extinguisher.fuel)} max={B8b9Num(extinguisher.maxFuel)} inContainer={inContainer} active={active}";
            Log.Info(line);
        }
        Log.Info($"[SyncHarness] extinguisherstate: {count} fire extinguishers");
    }

    private void LiveCommand_extinguisherpick(NotificationCenter.Notification n)
    {
        string prefix = LiveArgString(n, 0, "");
        FireExtinguisher target = null;
        foreach (FireExtinguisher extinguisher in Resources.FindObjectsOfTypeAll<FireExtinguisher>())
        {
            if (!extinguisher || !extinguisher.gameObject.scene.IsValid())
            {
                continue;
            }
            Pickupable pickupable = extinguisher.GetComponent<Pickupable>();
            if (pickupable && pickupable.inventoryItem == null && prefix != "" && B8b9IdOf(extinguisher).StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                target = extinguisher;
                break;
            }
        }
        if (!target)
        {
            Log.Warn($"[SyncHarness] extinguisherpick: failed reason=no-loose-extinguisher-with-id-prefix prefix={prefix}");
            return;
        }
        string id = B8b9IdOf(target);
        bool picked = Inventory.main.Pickup(target.GetComponent<Pickupable>(), true);
        string line = $"[SyncHarness] extinguisherpick: done id={id} picked={picked} fuel={B8b9Num(target.fuel)}";
        Log.Info(line);
    }
}
#endif
