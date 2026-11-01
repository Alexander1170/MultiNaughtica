#if DEBUG
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Live batch check k15a : the creatures that spawner prefabs make (K15 family A, <see cref="NitroxClient.GameLogic.SpawnerCreatures" />).<br/>
///     - <c>k15aspawners [radius] [filter]</c> lists the loaded <c>PrefabSpawnBase</c> components within the radius (default 1000 m) whose object name, prefab or spawned object contains the filter
///     (default <c>Skyray</c>, <c>*</c> = all): the entity the spawner sits in, its slot key and the creature id that K15 gives that slot, and what the spawner holds now<br/>
///     - <c>k15askyrays [radius]</c> lists every loaded creature whose tech type contains <c>Skyray</c> within the radius: its id (or <c>none</c>), position, which spawner holds it, and counts
///     the ones without an id and the ids that are used by more than one object<br/>
///     Both log <c>[SyncHarness] k15aspawners: ...</c> / <c>[SyncHarness] k15askyrays: ...</c> lines of <c>key=value</c> pairs; the last field of an item of k15aspawners is <c>slot=</c> and runs to the end of the line.
/// </summary>
public partial class HarnessConsoleCommands
{
    private const int K15A_MAX_ITEMS = 40;
    private const string K15A_DEFAULT_FILTER = "Skyray";

    private static string K15aNumber(float value) => value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

    private static string K15aPosition(Vector3 position) => $"{K15aNumber(position.x)},{K15aNumber(position.y)},{K15aNumber(position.z)}";

    private static string K15aWord(string text) => string.IsNullOrEmpty(text) ? "none" : text.Replace(' ', '_');

    private static bool K15aContains(string text, string part) => text != null && text.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static string K15aEntityId(GameObject target)
    {
        NitroxEntity entity = target ? target.GetComponent<NitroxEntity>() : null;
        return entity && entity.Id != null ? entity.Id.ToString() : "none";
    }

    private static bool K15aSpawnerMatches(PrefabSpawnBase spawner, string filter)
    {
        if (string.IsNullOrEmpty(filter) || filter == "*")
        {
            return true;
        }

        if (K15aContains(spawner.gameObject.name, filter))
        {
            return true;
        }

        PrefabSpawn plain = spawner as PrefabSpawn;
        if (plain && plain.prefab && K15aContains(plain.prefab.name, filter))
        {
            return true;
        }

        GameObject spawned = spawner.spawnedObj;
        return spawned && (K15aContains(spawned.name, filter) || K15aContains(CraftData.GetTechType(spawned).ToString(), filter));
    }

    public void LiveCommand_k15aspawners(NotificationCenter.Notification n)
    {
        float radius = LiveArgFloat(n, 0, 1000f);
        string filter = LiveArgString(n, 1, K15A_DEFAULT_FILTER);
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;

        System.Collections.Generic.List<PrefabSpawnBase> matching = new();
        System.Collections.Generic.Dictionary<string, int> kinds = new();
        int total = 0;
        foreach (PrefabSpawnBase spawner in UnityEngine.Object.FindObjectsOfType<PrefabSpawnBase>())
        {
            if (!spawner || Vector3.Distance(spawner.transform.position, here) > radius)
            {
                continue;
            }

            total++;
            string kind = spawner.gameObject.name;
            kinds[kind] = kinds.TryGetValue(kind, out int count) ? count + 1 : 1;
            if (K15aSpawnerMatches(spawner, filter))
            {
                matching.Add(spawner);
            }
        }

        string begin = $"[SyncHarness] k15aspawners: begin radius={K15aNumber(radius)} filter={filter} player={K15aPosition(here)} total={total} matching={matching.Count}";
        Log.Info(begin);

        // What kinds of spawner objects are loaded here (the most common first): tells what a filter that found nothing was up against
        System.Collections.Generic.List<string> names = new(kinds.Keys);
        names.Sort((a, b) => kinds[b].CompareTo(kinds[a]));
        System.Collections.Generic.List<string> kindParts = new();
        for (int i = 0; i < names.Count && i < 12; i++)
        {
            kindParts.Add($"{K15aWord(names[i])}x{kinds[names[i]]}");
        }
        string kindsText = kindParts.Count > 0 ? string.Join(" ", kindParts) : "none";
        Log.Info($"[SyncHarness] k15aspawners: kinds {kindsText}");

        int index = 0;
        foreach (PrefabSpawnBase spawner in matching)
        {
            if (index >= K15A_MAX_ITEMS)
            {
                break;
            }

            index++;
            NitroxEntity entity = NitroxClient.GameLogic.SpawnerCreatures.GetSpawnerEntity(spawner);
            bool hasEntity = entity && entity.Id != null;
            string entityText = hasEntity ? entity.Id.ToString() : "none";
            string slot = hasEntity ? NitroxClient.GameLogic.SpawnerCreatures.GetSlotKey(spawner, entity) : "-";
            string creatureText = hasEntity ? SpawnerCreatureId.For(entity.Id, slot).ToString() : "none";

            GameObject spawned = spawner.spawnedObj;
            string spawnedName = spawned ? K15aWord(spawned.name) : "none";
            string tech = spawned ? CraftData.GetTechType(spawned).ToString() : "none";
            string spawnedId = K15aEntityId(spawned);
            bool watched = spawner.GetComponents<SpawnerCreatureWatcher>().Length > 0;
            Vector3 at = spawner.transform.position;
            string distance = K15aNumber(Vector3.Distance(at, here));

            string item = $"[SyncHarness] k15aspawners: item {index} name={K15aWord(spawner.gameObject.name)} kind={spawner.GetType().Name} spawnType={spawner.spawnType} intermittent={K15aNumber(spawner.intermittentSpawnTime)} pos={K15aPosition(at)} dist={distance} entity={entityText} creature={creatureText} spawned={spawnedName} tech={tech} spawnedId={spawnedId} watcher={watched} slot={slot}";
            Log.Info(item);
        }

        Log.Info($"[SyncHarness] k15aspawners: end items={index}");
    }

    public void LiveCommand_k15askyrays(NotificationCenter.Notification n)
    {
        float radius = LiveArgFloat(n, 0, 1000f);
        Vector3 here = Player.main ? Player.main.transform.position : Vector3.zero;
        Log.Info($"[SyncHarness] k15askyrays: begin radius={K15aNumber(radius)} player={K15aPosition(here)}");

        // Which spawner holds which creature as its own (name and the entity it sits in)
        System.Collections.Generic.Dictionary<GameObject, string> holderNames = new();
        System.Collections.Generic.Dictionary<GameObject, string> holderEntities = new();
        foreach (PrefabSpawnBase spawner in UnityEngine.Object.FindObjectsOfType<PrefabSpawnBase>())
        {
            if (!spawner || !spawner.spawnedObj)
            {
                continue;
            }

            NitroxEntity holderEntity = NitroxClient.GameLogic.SpawnerCreatures.GetSpawnerEntity(spawner);
            holderNames[spawner.spawnedObj] = K15aWord(spawner.gameObject.name);
            holderEntities[spawner.spawnedObj] = holderEntity && holderEntity.Id != null ? holderEntity.Id.ToString() : "none";
        }

        System.Collections.Generic.Dictionary<string, int> perId = new();
        int total = 0;
        int withId = 0;
        foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
        {
            if (!creature)
            {
                continue;
            }

            string tech = CraftData.GetTechType(creature.gameObject).ToString();
            if (!K15aContains(tech, "Skyray"))
            {
                continue;
            }

            float distance = Vector3.Distance(creature.transform.position, here);
            if (distance > radius)
            {
                continue;
            }

            total++;
            string id = K15aEntityId(creature.gameObject);
            if (id != "none")
            {
                withId++;
                perId[id] = perId.TryGetValue(id, out int seen) ? seen + 1 : 1;
            }

            Transform parent = creature.transform.parent;
            string parentName = parent ? K15aWord(parent.name) : "none";
            string owner = holderNames.TryGetValue(creature.gameObject, out string holderName) ? holderName : "none";
            string ownerEntity = holderEntities.TryGetValue(creature.gameObject, out string holderEntityId) ? holderEntityId : "none";
            string item = $"[SyncHarness] k15askyrays: item {total} id={id} tech={tech} pos={K15aPosition(creature.transform.position)} dist={K15aNumber(distance)} parent={parentName} owner={owner} ownerEntity={ownerEntity}";
            Log.Info(item);
        }

        int duplicates = 0;
        foreach (System.Collections.Generic.KeyValuePair<string, int> pair in perId)
        {
            if (pair.Value > 1)
            {
                duplicates++;
            }
        }

        Log.Info($"[SyncHarness] k15askyrays: end total={total} withId={withId} noId={total - withId} distinct={perId.Count} duplicates={duplicates}");
    }
}
#endif
