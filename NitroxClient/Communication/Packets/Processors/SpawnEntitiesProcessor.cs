using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class SpawnEntitiesProcessor(Entities entities, SimulationOwnership simulationOwnership, Terrain terrain) : IClientPacketProcessor<SpawnEntities>
{
    private readonly Entities entities = entities;
    private readonly SimulationOwnership simulationOwnership = simulationOwnership;
    private readonly Terrain terrain = terrain;

    public Task Process(ClientProcessorContext context, SpawnEntities packet)
    {
        List<Entity> entitiesToSpawn = packet.Entities;
        if (packet.ForceRespawn)
        {
            // The reply to a cell load asks to respawn everything the cell holds, including creatures and items this client still has alive (it simulates them or keeps them
            // alive out of their cell): destroying those resets their AI and makes them pop, so they are left alone.
            if (packet.SpawnedCells.Count > 0)
            {
                entitiesToSpawn = packet.Entities.Where(entity => !IsKeptAlive(entity)).ToList();
            }
            entities.CleanupExistingEntities(entitiesToSpawn);
        }

        if (entitiesToSpawn.Count > 0)
        {
            foreach (SimulatedEntity simulatedEntity in packet.Simulations)
            {
                simulationOwnership.RegisterNewerSimulation(simulatedEntity.Id, simulatedEntity);
            }

            foreach (Entity entity in entitiesToSpawn)
            {
                entities.ForgetDeletion(entity);
            }

            // Packet processing is done in the main thread so there's no issue calling this
            // We need a cold start so that all cleaned up entities (if force respawn is true) have time to be fully destroyed
            entities.EnqueueEntitiesToSpawn(entitiesToSpawn, packet.SpawnedCells, packet.ForceRespawn);
            return Task.CompletedTask;
        }

        // Even if there was nothing to be spawned in the cell, we need to know about it as fully spawned
        foreach (AbsoluteEntityCell spawnedEntityCell in packet.SpawnedCells)
        {
            terrain.AddFullySpawnedCell(spawnedEntityCell);
        }
        return Task.CompletedTask;
    }

    private bool IsKeptAlive(Entity entity)
    {
        return NitroxEntity.TryGetObjectFrom(entity.Id, out UnityEngine.GameObject gameObject) &&
               (simulationOwnership.HasAnyLockType(entity.Id) || gameObject.GetComponent<OutOfCellEntity>());
    }
}
