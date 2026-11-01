using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class IncubatorMetadataProcessor : EntityMetadataProcessor<IncubatorMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, IncubatorMetadata metadata)
    {
        // The same metadata can arrive for an incubator that already runs (the entity is sent again to a client that has it): powering up and hatching are not repeatable,
        // the second power up plays its sound again and the second hatch hatches every egg again
        if (metadata.Powered)
        {
            IncubatorActivationTerminal terminal = gameObject.GetComponentInChildren<IncubatorActivationTerminal>();
            if (terminal && terminal.incubator && !terminal.incubator.powered)
            {
                terminal.incubator.SetPowered(true);
                terminal.onUseGoal?.Trigger();
                terminal.CloseDeck();
            }
        }

        if (metadata.Hatched)
        {
            Incubator incubator = gameObject.GetComponentInChildren<Incubator>();
            if (incubator && !incubator.hatched)
            {
                incubator.hatched = true;
                incubator.OnHatched();
            }
        }
    }
}
