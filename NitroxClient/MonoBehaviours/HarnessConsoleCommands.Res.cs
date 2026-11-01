#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Live batch check res , logging only, nothing here changes the game except the opt-in trigger of <c>r12dprobe</c>.<br/>
///     - <c>r11databoxes [radius]</c> lists the loaded databoxes (<c>BlueprintHandTarget</c>) within the radius (default 120 m) of the local player: the blueprint they give (<c>unlockTechType</c>),
///     NitroxId, position, whether the box was used and whether this client knows the blueprint (R11: a databox of a blueprint the shared PDA knows must not be in a freshly generated batch)<br/>
///     - <c>r12dprobe [radius] [trigger]</c> counts the resources around the local player that have no NitroxId on their own GameObject (the lookup the break and kill patches do, which warns
///     "Could not find NitroxEntity for breakable entity"): <c>BreakableResource</c>, <c>Drillable</c>, <c>ResourceTracker</c>, loose <c>Pickupable</c>, <c>PickPrefab</c>, <c>SpawnOnKill</c>,
///     grouped by prefab name and class id, with the number of them whose parent has an id (the id sits on the entity root and the component on a child). With <c>trigger</c> &gt; 0 it
///     also calls the real <c>BreakableResource.BreakIntoResources()</c> on one id-less outcrop per prefab name (3 at most), which makes the Nitrox patch write the real warning (R12d).
///     The per-kind numbers count one row per kind an object has a component of; the <c>done</c> line therefore also gives <c>distinctObjects</c> (GameObjects without an id, each once) and
///     <c>newObjects</c> (of those, the ones no earlier probe of the session listed)<br/>
///     Log lines: <c>[SyncHarness] r11databoxes: ...</c> and <c>[SyncHarness] r12dprobe: ...</c> (key=value pairs, invariant culture).
/// </summary>
public partial class HarnessConsoleCommands
{
    private const int RES_PROBE_MAX_GROUPS = 25;
    private const int RES_PROBE_MAX_TRIGGERS = 3;

    // instance ids of the GameObjects without a NitroxId that an r12dprobe of this game session has already listed (to count an object that two probes both see only once)
    private static readonly HashSet<int> resProbeSeenNoId = new HashSet<int>();

    private sealed class ResProbeGroup
    {
        public string Name;
        public string ClassId;
        public int Count;
        public int AncestorHasId;
        public string Example;
        public string At;
    }

    private static string ResNumber(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string ResPosition(Vector3 position) => $"({ResNumber(position.x)}, {ResNumber(position.y)}, {ResNumber(position.z)})";

    private static string ResProbeName(GameObject target)
    {
        string name = target.name;
        return name.EndsWith("(Clone)", StringComparison.Ordinal) ? name.Substring(0, name.Length - "(Clone)".Length).Trim() : name;
    }

    private static string ResProbePath(Transform transform)
    {
        string path = transform.name;
        Transform parent = transform.parent;
        int guard = 0;
        while (parent && guard < 40)
        {
            path = $"{parent.name}/{path}";
            parent = parent.parent;
            guard++;
        }
        return path;
    }

    public void LiveCommand_r11databoxes(NotificationCenter.Notification n)
    {
        float radius = LiveArgFloat(n, 0, 120f);
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        int total = 0;
        int listed = 0;
        foreach (BlueprintHandTarget target in FindObjectsOfType<BlueprintHandTarget>())
        {
            if (!target)
            {
                continue;
            }

            total++;
            Vector3 position = target.transform.position;
            float distance = Vector3.Distance(position, here);
            if (distance > radius)
            {
                continue;
            }

            listed++;
            string id = target.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "noid";
            bool known = KnownTech.Contains(target.unlockTechType);
            string line = $"tech={target.unlockTechType} id={id} at={ResPosition(position)} dist={ResNumber(distance)} used={target.used} known={known} name={target.gameObject.name.Replace(' ', '_')}";
            Log.Info($"[SyncHarness] r11databoxes: {line}");
        }

        string summary = $"{listed} databoxes within {ResNumber(radius)} m of {ResPosition(here)}, {total} loaded in all";
        Log.Info($"[SyncHarness] r11databoxes: {summary}");
    }

    /// <summary>
    ///     One category of r12dprobe: counts the components of type T within the radius that have / have no NitroxId on their own GameObject and logs the id-less ones grouped by prefab name.<br/>
    ///     One GameObject can carry components of several kinds (an outcrop is a BreakableResource and a ResourceTracker, a loose scrap a Pickupable and a ResourceTracker), so the
    ///     per-category numbers add up to more than the number of objects: <paramref name="distinctNoId"/> collects the instance ids of the GameObjects without an id over all categories.
    /// </summary>
    /// <returns>The number of components without an id</returns>
    private static int ResProbeCategory<T>(string category, Vector3 here, float radius, Func<T, bool> include, List<T> noIdCollector, HashSet<int> distinctNoId) where T : Component
    {
        int total = 0;
        int withId = 0;
        int noId = 0;
        int noIdWithAncestorId = 0;
        Dictionary<string, ResProbeGroup> groups = new Dictionary<string, ResProbeGroup>();
        foreach (T item in FindObjectsOfType<T>())
        {
            Component component = item;
            if (!component || Vector3.Distance(component.transform.position, here) > radius || (include != null && !include(item)))
            {
                continue;
            }

            total++;
            if (component.TryGetNitroxId(out NitroxId _))
            {
                withId++;
                continue;
            }

            noId++;
            bool ancestorHasId = component.gameObject.GetComponentInParent<NitroxEntity>(true);
            noIdWithAncestorId += ancestorHasId ? 1 : 0;
            if (noIdCollector != null)
            {
                noIdCollector.Add(item);
            }

            if (distinctNoId != null)
            {
                distinctNoId.Add(component.gameObject.GetInstanceID());
            }

            string name = ResProbeName(component.gameObject);
            PrefabIdentifier prefab = component.gameObject.GetComponentInParent<PrefabIdentifier>(true);
            string classId = prefab ? prefab.ClassId : "none";
            string key = $"{name}|{classId}";
            ResProbeGroup entry;
            if (!groups.TryGetValue(key, out entry))
            {
                entry = new ResProbeGroup { Name = name, ClassId = classId, Example = ResProbePath(component.transform), At = ResPosition(component.transform.position) };
                groups[key] = entry;
            }

            entry.Count++;
            entry.AncestorHasId += ancestorHasId ? 1 : 0;
        }

        string counts = $"category={category} total={total} withId={withId} noId={noId} noIdButAncestorHasId={noIdWithAncestorId} radius={ResNumber(radius)}";
        Log.Info($"[SyncHarness] r12dprobe: {counts}");

        List<ResProbeGroup> sorted = new List<ResProbeGroup>(groups.Values);
        sorted.Sort((a, b) => b.Count.CompareTo(a.Count));
        for (int i = 0; i < sorted.Count && i < RES_PROBE_MAX_GROUPS; i++)
        {
            ResProbeGroup shown = sorted[i];
            string row = $"category={category} name=\"{shown.Name}\" count={shown.Count} ancestorHasId={shown.AncestorHasId} classId={shown.ClassId} at={shown.At} example=\"{shown.Example}\"";
            Log.Info($"[SyncHarness] r12dprobe: noid {row}");
        }

        return noId;
    }

    public void LiveCommand_r12dprobe(NotificationCenter.Notification n)
    {
        float radius = LiveArgFloat(n, 0, 60f);
        bool trigger = LiveArgFloat(n, 1, 0f) > 0f;
        if (!Player.main)
        {
            Log.Warn("[SyncHarness] r12dprobe: error there is no local player");
            return;
        }

        Vector3 here = Player.main.transform.position;
        List<BreakableResource> noIdBreakables = new List<BreakableResource>();
        HashSet<int> distinctNoId = new HashSet<int>();
        int noIdTotal = 0;
        int categories = 0;

        try
        {
            noIdTotal += ResProbeCategory<BreakableResource>("BreakableResource", here, radius, null, noIdBreakables, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=BreakableResource {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            noIdTotal += ResProbeCategory<Drillable>("Drillable", here, radius, null, null, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=Drillable {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            noIdTotal += ResProbeCategory<ResourceTracker>("ResourceTracker", here, radius, null, null, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=ResourceTracker {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            // loose items only: what a player carries is an inventory entity, not a world resource
            noIdTotal += ResProbeCategory<Pickupable>("Pickupable", here, radius, pickupable => !pickupable.attached, null, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=Pickupable {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            noIdTotal += ResProbeCategory<PickPrefab>("PickPrefab", here, radius, null, null, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=PickPrefab {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            noIdTotal += ResProbeCategory<SpawnOnKill>("SpawnOnKill", here, radius, null, null, distinctNoId);
            categories++;
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] r12dprobe: error category=SpawnOnKill {exception.GetType().Name}: {exception.Message}");
        }

        // distinctObjects: GameObjects without an id, each counted once over all kinds (noIdTotal adds one row per kind an object has a component of). newObjects: of those, the ones no earlier
        // probe of this game session listed (the spots of r12d lie 47 to 50 m apart and the radius is 60 m, so a later probe sees some of the same objects again)
        int newObjects = 0;
        foreach (int instanceId in distinctNoId)
        {
            if (resProbeSeenNoId.Add(instanceId))
            {
                newObjects++;
            }
        }

        if (trigger)
        {
            // The real call: the Nitrox prefix BreakableResource_BreakIntoResources_Patch logs "Could not find NitroxEntity for breakable entity <path>" for a breakable without an id
            HashSet<string> triggered = new HashSet<string>();
            foreach (BreakableResource breakable in noIdBreakables)
            {
                if (triggered.Count >= RES_PROBE_MAX_TRIGGERS)
                {
                    break;
                }

                if (!breakable)
                {
                    continue;
                }

                string name = ResProbeName(breakable.gameObject);
                if (!triggered.Add(name))
                {
                    continue;
                }

                string what = $"name=\"{name}\" at={ResPosition(breakable.transform.position)} path=\"{ResProbePath(breakable.transform)}\"";
                Log.Info($"[SyncHarness] r12dprobe: trigger BreakIntoResources {what}");
                try
                {
                    breakable.BreakIntoResources();
                }
                catch (Exception exception)
                {
                    Log.Warn($"[SyncHarness] r12dprobe: error trigger {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        Log.Info($"[SyncHarness] r12dprobe: done at={ResPosition(here)} radius={ResNumber(radius)} categories={categories} noIdTotal={noIdTotal} distinctObjects={distinctNoId.Count} newObjects={newObjects} trigger={trigger}");
    }
}
#endif
