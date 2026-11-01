using System;
using System.Collections;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using UnityEngine;
using UWE;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: spawns a prefab at a world position on this client and hands it to <see cref="NitroxConsole.Spawn" />,
///     exactly like the <c>spawn</c> and <c>sub</c> dev console commands do (see SpawnConsoleCommand_SpawnAsync_Patch and
///     SubConsoleCommand_OnConsoleCommand_sub_Patch), so the entity is synced through the regular client-spawn path.
///     Only acts in Debug builds.
/// </summary>
internal sealed class DebugSpawnEntityProcessor(NitroxConsole nitroxConsole) : IClientPacketProcessor<DebugSpawnEntity>
{
    private readonly NitroxConsole nitroxConsole = nitroxConsole;

    public Task Process(ClientProcessorContext context, DebugSpawnEntity packet)
    {
#if DEBUG
        if (!Enum.TryParse(packet.TechType?.Name, true, out TechType techType) || techType == TechType.None)
        {
            Log.Error($"[SyncHarness] cannot spawn unknown TechType '{packet.TechType?.Name}'");
            return Task.CompletedTask;
        }
        CoroutineHost.StartCoroutine(SpawnAsync(techType, packet.Position.ToUnity(), packet.Rotation.ToUnity()));
#endif
        return Task.CompletedTask;
    }

#if DEBUG
    private IEnumerator SpawnAsync(TechType techType, Vector3 position, Quaternion rotation)
    {
        GameObject gameObject;
        if (techType == TechType.Cyclops)
        {
            // Same as the "sub cyclops" console command: the scene prefab is loaded and SubConsoleCommand instantiates it.
            GameObject prefab = null;
            LightmappedPrefabs.main.RequestScenePrefab("cyclops", loaded => prefab = loaded);
            yield return new WaitUntil(() => prefab != null);
            SubConsoleCommand.main.OnSubPrefabLoaded(prefab);
            gameObject = SubConsoleCommand.main.GetLastCreatedSub();
            if (!gameObject)
            {
                Log.Error("[SyncHarness] Cyclops spawn produced no sub");
                yield break;
            }
            gameObject.transform.position = position;
            gameObject.transform.rotation = rotation;
            nitroxConsole.Spawn(gameObject);
        }
        else
        {
            // Same sequence as SpawnConsoleCommand.SpawnAsync (with Nitrox's inserted callback), at a fixed position instead of in front of the player.
            CoroutineTask<GameObject> prefabTask = CraftData.GetPrefabForTechTypeAsync(techType, false);
            yield return prefabTask;
            GameObject prefab = prefabTask.GetResult();
            if (!prefab)
            {
                Log.Error($"[SyncHarness] no prefab for TechType {techType}");
                yield break;
            }
            gameObject = Utils.SpawnPrefabAt(prefab, null, position);
            gameObject.transform.rotation = rotation;
            gameObject.SetActive(true);
            nitroxConsole.Spawn(gameObject);
            LargeWorldEntity.Register(gameObject);
            CrafterLogic.NotifyCraftEnd(gameObject, techType);
            gameObject.SendMessage("StartConstruction", SendMessageOptions.DontRequireReceiver);
        }
        Log.Info($"[SyncHarness] spawned {techType} at {position}");
    }
#endif
}
