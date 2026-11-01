using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

/// <summary>
/// Applies the repair of a damaged wall panel that another player (or an earlier session) finished. The decisions are in <see cref="WeldablePanelRules"/>.
/// </summary>
public class WeldableWallPanelGenericMetadataProcessor : EntityMetadataProcessor<WeldableWallPanelGenericMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, WeldableWallPanelGenericMetadata metadata)
    {
        WeldableWallPanelGeneric weldableWallPanelGeneric = gameObject.GetComponent<WeldableWallPanelGeneric>();
        if (!weldableWallPanelGeneric || !weldableWallPanelGeneric.liveMixin)
        {
            // Not an exception: PlaceholderGroupWorldEntitySpawner applies the metadata of all the children of a wreck in one coroutine, a throw here would end the spawn of the rest of it
            Log.Warn($"[{nameof(WeldableWallPanelGenericMetadataProcessor)}] {gameObject.name} has no WeldableWallPanelGeneric with a LiveMixin, {metadata} not applied");
            return;
        }

        LiveMixin liveMixin = weldableWallPanelGeneric.liveMixin;
        // WeldableWallPanelGeneric.Start has not run yet on a panel made in this frame (it sets sendMessageFrom first thing)
        bool freshlySpawned = !weldableWallPanelGeneric.sendMessageFrom;
        WeldablePanelRules.Result result = WeldablePanelRules.Apply(freshlySpawned, liveMixin.health, metadata.LiveMixInHealth, liveMixin.maxHealth);

        if (result.HealBy > 0f)
        {
            // The way the welder repairs: also takes the sparks away and sends OnRepair at full health. Not a broadcast: a panel has no simulation lock, the AddHealth patch only reports for those.
            float target = liveMixin.health + result.HealBy;
            liveMixin.AddHealth(result.HealBy);
            if (liveMixin.health < target)
            {
                // AddHealth does nothing for a panel at 0 health that cannot be resurrected
                liveMixin.health = target;
            }
        }
        else
        {
            liveMixin.health = result.Health;
        }
    }
}
