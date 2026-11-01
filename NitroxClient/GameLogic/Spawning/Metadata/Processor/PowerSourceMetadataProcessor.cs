using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Bases;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class PowerSourceMetadataProcessor : EntityMetadataProcessor<PowerSourceMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, PowerSourceMetadata metadata)
    {
        PowerSource powerSource = gameObject.GetComponentInChildren<PowerSource>(true);
        if (!powerSource)
        {
            Log.Error($"[{nameof(PowerSourceMetadataProcessor)}] Could not find a PowerSource on {gameObject.name}");
            return;
        }

        // The patches on PowerSource would send the value back
        using (PacketSuppressor<Nitrox.Model.Subnautica.Packets.EntityMetadataUpdate>.Suppress())
        {
            powerSource.SetPower(metadata.Power);
        }

        // Only the player who simulates the base burns fuel, so this is where the others (and the next simulating player) learn how far the burn has come
        // (the harness switches the fix off for its 'before' runs, which then are vanilla in this respect too)
        if (BaseSimulation.DisabledByHarness)
        {
            return;
        }

        // The player who burns (the holder of the lock) keeps his own value: an update from the previous holder that is still on its way must not set it back. A reactor that
        // has just been created has none yet (0) and takes the stored one.
        SimulationOwnership simulationOwnership = Resolve<SimulationOwnership>();
        if (powerSource.TryGetComponent(out BaseBioReactor bioReactor) && (bioReactor._toConsume <= 0f || !BaseSimulation.RunsHere(bioReactor, simulationOwnership)))
        {
            bioReactor._toConsume = metadata.ToConsume;
        }
        else if (powerSource.TryGetComponent(out BaseNuclearReactor nuclearReactor) && (nuclearReactor._toConsume <= 0f || !BaseSimulation.RunsHere(nuclearReactor, simulationOwnership)))
        {
            nuclearReactor._toConsume = metadata.ToConsume;
        }
    }
}
