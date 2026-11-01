using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Bases;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

/// <summary>
///     Applies the water of a base that the player who simulates it sent (see <see cref="BaseFloodRules" /> for what is applied and when). A client that joins later, a remote
///     build or a resync gets the stored water instead of a dry base that floods again from the start.
/// </summary>
public class BaseFloodMetadataProcessor : EntityMetadataProcessor<BaseFloodMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, BaseFloodMetadata metadata)
    {
        BaseFloodSim baseFloodSim = gameObject.GetComponent<BaseFloodSim>();

        if (!baseFloodSim)
        {
            Log.Error($"[{nameof(BaseFloodMetadataProcessor)}] Could not find BaseFloodSim on {gameObject.name}");
            return;
        }

        // A simulation that has no cells yet (the base is still being built) cannot take a grid: the holder repeats it later
        if (!baseFloodSim.IsInitialized())
        {
            return;
        }

        // Applying a grid of another size would throw inside the simulation's own restore logic and break its Update() loop (no more leaks and flooding) from then on
        if (metadata.FlatValueGrid == null || metadata.FlatValueGrid.Length != baseFloodSim.shape.Size)
        {
            Log.Debug($"[{nameof(BaseFloodMetadataProcessor)}] Water grid of {metadata.FlatValueGrid?.Length ?? 0} cells does not fit the {baseFloodSim.shape.Size} cells of {gameObject.name}, ignored");
            return;
        }

        // Refresh flatValueGrid from the simulated state, the sum is what this client has now
        baseFloodSim.OnProtoSerialize(null);
        bool localIsHolder = gameObject.TryGetNitroxId(out NitroxId baseId) && Resolve<SimulationOwnership>().HasAnyLockType(baseId);
        if (!BaseFloodRules.ShouldApply(baseFloodSim.shape.Size, metadata.FlatValueGrid, BaseFloodRules.Sum(baseFloodSim.flatValueGrid), localIsHolder))
        {
            return;
        }

        baseFloodSim.flatValueGrid = metadata.FlatValueGrid;
        baseFloodSim.OnProtoDeserialize(null);
        // The game would restore it at the start of the next Update, with the shape it has by then: a base that is resized in between makes the restore read beyond the grid on every
        // following frame (and the postfix of the simulation never runs again). Now, with the shape that the size was checked against.
        baseFloodSim.RestoreSerializedWaterLevels();
    }
}
