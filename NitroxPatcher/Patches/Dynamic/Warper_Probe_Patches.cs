#if DEBUG
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     K18 Warper, stage 0 (Debug builds only, logging only, changes nothing). The design of a shared Warper (the design notes (not part of this repository), K18) rests on facts that cannot be read out of the
///     game's code: what the Warper prefab is (its AI components, the serialized distances and damages), where each <c>WarperSpawner</c> really stands, and which of the colliders that a
///     warp ball finds the game would warp (a remote player's trigger body is found by the overlap, but the game decides afterwards in <c>GetGameObjectToWarp</c>). Each of the three
///     patches below logs one of them with the tag <c>[K18 warper]</c>; run a late-game session with two clients near a Warper and read the lines.
/// </summary>
public sealed partial class WarperSpawner_OnEnable_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((WarperSpawner t) => t.OnEnable());

    private const float READ_PLACEMENT_AFTER_SECONDS = 2f;

    public static void Postfix(WarperSpawner __instance)
    {
        // A spawner that comes from a server entity is created at the origin and placed in the same step: read where it stands once the placement is done
        CoroutineHost.StartCoroutine(LogPlacementLater(__instance));
    }

    private static IEnumerator LogPlacementLater(WarperSpawner spawner)
    {
        yield return new WaitForSeconds(READ_PLACEMENT_AFTER_SECONDS);
        if (!spawner)
        {
            yield break;
        }

        GameObject spawnerObject = spawner.gameObject;
        Vector3 position = spawnerObject.transform.position;
        NitroxEntity entityAncestor = spawnerObject.FindAncestor<NitroxEntity>();
        string entity = entityAncestor ? $"{entityAncestor.Id} ({entityAncestor.name})" : "none";
        string prefabName = spawner.warperPrefab ? spawner.warperPrefab.name : "none";
        string prefabClassId = spawner.warperPrefab && spawner.warperPrefab.TryGetComponent(out PrefabIdentifier prefabIdentifier) ? prefabIdentifier.ClassId : "none";
        string timers = $"warpPause={spawner.warpPause} warpInterval={spawner.warpInterval}";
        Log.Info($"[K18 warper] spawner path={spawnerObject.GetFullHierarchyPath()} scene={spawnerObject.scene.name} pos=({position.x:F1},{position.y:F1},{position.z:F1}) entityAncestor={entity} prefab={prefabName} prefabClassId={prefabClassId} {timers}");
    }
}

public sealed partial class Warper_WarpIn_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Warper t) => t.WarpIn(default(WarperSpawner)));

    public static void Postfix(Warper __instance)
    {
        StringBuilder components = new();
        foreach (Component component in __instance.GetComponents<Component>())
        {
            components.Append(component ? component.GetType().Name : "missing").Append(',');
        }

        string idText = __instance.TryGetNitroxId(out NitroxId id) ? id.ToString() : "none";
        Vector3 position = __instance.transform.position;
        Log.Info($"[K18 warper] warper id={idText} pos=({position.x:F1},{position.y:F1},{position.z:F1}) components={components}");

        AggressiveWhenSeeTarget aggressive = __instance.GetComponent<AggressiveWhenSeeTarget>();
        string aggressiveText = aggressive ? $"targetType={aggressive.targetType} targetShouldBeInfected={aggressive.targetShouldBeInfected} minimumVelocity={aggressive.minimumVelocity}" : "none";
        string rangedText = __instance.GetComponentInChildren<RangedAttackLastTarget>() ? "yes" : "none";
        string inspectText = __instance.GetComponentInChildren<WarperInspectPlayer>() ? "yes" : "none";
        Log.Info($"[K18 warper] warper AggressiveWhenSeeTarget={aggressiveText} RangedAttackLastTarget={rangedText} WarperInspectPlayer={inspectText}");
    }
}

public sealed partial class WarpBall_Detonate_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((WarpBall t) => t.Detonate());

    public static void Prefix(WarpBall __instance)
    {
        // Vanilla calls Detonate every frame while the warp-in of creatures is pending and returns at once through this flag: log the first call only
        if (__instance.wasDetonate)
        {
            return;
        }

        Vector3 position = __instance.transform.position;
        Collider[] hits = Physics.OverlapSphere(position, __instance.warpRadius, ~0, QueryTriggerInteraction.Collide);

        // What the overlap finds, with the remote player first so that the entry cannot be cut off, and what the game would warp of it (GetGameObjectToWarp decides)
        string[] remoteHits = hits.Where(hit => hit.GetComponentInParent<RemotePlayerIdentifier>()).Select(hit => $"{hit.name}{(hit.isTrigger ? "(trigger)" : "")}").Distinct().ToArray();
        string found = remoteHits.Length == 0 ? "no remote player" : $"REMOTE-PLAYER {string.Join(",", remoteHits)}";
        string others = string.Join(",", hits.Where(hit => !hit.GetComponentInParent<RemotePlayerIdentifier>()).Select(hit => hit.name).Distinct().Take(20));
        string warped = string.Join(",", hits.Select(hit => __instance.GetGameObjectToWarp(hit)).Where(target => target).Select(target => target.name).Distinct().Take(20));
        string warperAlive = __instance.warperGO ? "yes" : "no";
        string values = $"warpRadius={__instance.warpRadius} warpDistance={__instance.warpDistance} damage={__instance.damage} maxLifeTime={__instance.maxLifeTime} minDistanceToWarper={__instance.minDistanceToWarper}";
        Log.Info($"[K18 warper] ball detonates at ({position.x:F1},{position.y:F1},{position.z:F1}) {values} warperAlive={warperAlive} overlap: {found}; others={others}; the game would warp: {warped}");
    }
}
#endif
