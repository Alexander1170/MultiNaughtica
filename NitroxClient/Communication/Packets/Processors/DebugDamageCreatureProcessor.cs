using System;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: hits the nearest creature of a tech type with the local player as dealer (LiveMixin.TakeDamage, same as a knife hit).
///     Only acts in Debug builds.
/// </summary>
internal sealed class DebugDamageCreatureProcessor : IClientPacketProcessor<DebugDamageCreature>
{
    public Task Process(ClientProcessorContext context, DebugDamageCreature packet)
    {
#if DEBUG
        if (!Player.main || !Enum.TryParse(packet.TechType, true, out TechType techType))
        {
            Log.Warn($"[SyncHarness] cannot damage creature '{packet.TechType}'");
            return Task.CompletedTask;
        }

        Creature nearest = null;
        float nearestDistance = packet.Radius;
        if (packet.TargetId.HasValue && NitroxClient.MonoBehaviours.NitroxEntity.TryGetComponentFrom(packet.TargetId.Value, out Creature target))
        {
            nearest = target;
            nearestDistance = Vector3.Distance(target.transform.position, Player.main.transform.position);
        }
        else
        {
            foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
            {
                if (!creature.isActiveAndEnabled || CraftData.GetTechType(creature.gameObject) != techType)
                {
                    continue;
                }
                float distance = Vector3.Distance(creature.transform.position, Player.main.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = creature;
                    nearestDistance = distance;
                }
            }
        }

        if (!nearest || !nearest.TryGetComponent(out LiveMixin liveMixin))
        {
            Log.Warn($"[SyncHarness] no {techType} within {packet.Radius} m to damage");
            return Task.CompletedTask;
        }

        nearest.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId creatureId);
        Log.Info($"[SyncHarness] damaging {techType} {creatureId} {packet.Hits} x {packet.Damage} ({nearestDistance:F1} m away), health before {liveMixin.health:F1}");
        for (int i = 0; i < Math.Max(1, packet.Hits); i++)
        {
            liveMixin.TakeDamage(packet.Damage, nearest.transform.position, DamageType.Normal, Player.mainObject);
        }
        Log.Info($"[SyncHarness] {techType} {creatureId} health after the hits here: {liveMixin.health:F1}, alive {liveMixin.IsAlive()}");
#endif
        return Task.CompletedTask;
    }
}
