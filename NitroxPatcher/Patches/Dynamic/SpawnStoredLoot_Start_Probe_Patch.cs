#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     R12c probe (Debug builds only, logging only, changes nothing): <c>SpawnStoredLoot.Start</c> rolls a locker's or crate's contents on every client that loads it.
///     This logs, per container and per client, where it is, whether it has a NitroxId and what ended up inside after the roll, so that the harness
///     (a harness script that is not part of this repository) can tell how often the two clients got different contents and how many containers have no id at all.
///     Log line: <c>[R12c] container path=... pos=... classId=... id=... lootSpawnedBefore=... items=Tech:count,...</c>
/// </summary>
public sealed partial class SpawnStoredLoot_Start_Probe_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((SpawnStoredLoot t) => t.Start());

    private const float READ_CONTENTS_AFTER_SECONDS = 4f;

    public static void Prefix(SpawnStoredLoot __instance, out bool __state)
    {
        __state = __instance.lootSpawned;
    }

    public static void Postfix(SpawnStoredLoot __instance, bool __state)
    {
        CoroutineHost.StartCoroutine(LogContentsLater(__instance, __state));
    }

    private static IEnumerator LogContentsLater(SpawnStoredLoot spawnStoredLoot, bool lootSpawnedBefore)
    {
        yield return new WaitForSeconds(READ_CONTENTS_AFTER_SECONDS);
        if (!spawnStoredLoot)
        {
            yield break;
        }

        GameObject gameObject = spawnStoredLoot.gameObject;
        Vector3 position = gameObject.transform.position;
        string classId = gameObject.TryGetComponent(out PrefabIdentifier prefabIdentifier) ? prefabIdentifier.ClassId : "none";
        string id = gameObject.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "none";
        string items = "no container";
        if (gameObject.TryGetComponent(out StorageContainer storageContainer) && storageContainer.container != null)
        {
            Dictionary<TechType, int> counts = [];
            foreach (TechType techType in storageContainer.container.GetItemTypes())
            {
                counts[techType] = storageContainer.container.GetCount(techType);
            }
            items = counts.Count == 0 ? "empty" : string.Join(",", counts.OrderBy(pair => pair.Key.ToString()).Select(pair => $"{pair.Key}:{pair.Value}"));
        }

        Log.Info($"[R12c] container path={gameObject.GetFullHierarchyPath()} pos=({position.x:F1},{position.y:F1},{position.z:F1}) classId={classId} id={id} lootSpawnedBefore={lootSpawnedBefore} items={items}");
    }
}
#endif
