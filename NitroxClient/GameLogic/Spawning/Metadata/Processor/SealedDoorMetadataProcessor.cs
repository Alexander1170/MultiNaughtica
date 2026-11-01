using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

/// <summary>
/// Applies the cut state of a laser-cut door (or any other <c>Sealed</c> object) that the server holds: to a door that just spawned (a joiner, a restart, a cell that woke up) and to a
/// door that is in the world while another player cuts it. The decisions are in <see cref="SealedDoorRules"/>; a door nobody ever touched has no metadata and is never here.
/// </summary>
public class SealedDoorMetadataProcessor : EntityMetadataProcessor<SealedDoorMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, SealedDoorMetadata metadata)
    {
        Sealed door = gameObject.GetComponent<Sealed>();
        if (!door)
        {
            // Not an exception: PlaceholderGroupWorldEntitySpawner applies the metadata of all the children of a wreck in one coroutine, a throw here would end the spawn of the rest of it
            Log.Warn($"[{nameof(SealedDoorMetadataProcessor)}] {gameObject.name} has no Sealed component, {metadata} not applied");
            return;
        }

        bool freshlySpawned = IsFreshlySpawned(gameObject);
        SealedDoorRules.Result result = SealedDoorRules.Apply(door._sealed, door.openedAmount, door.maxOpenedAmount, metadata.Sealed, metadata.OpenedAmount, freshlySpawned);
        Log.Info($"Received door metadata change for {gameObject.name} with data of {metadata}: sealed {door._sealed} -> {result.Sealed}, opened {door.openedAmount} -> {result.OpenedAmount} of {door.maxOpenedAmount}, {(freshlySpawned ? "restored on spawn" : "live")}");

        door._sealed = result.Sealed;
        door.openedAmount = result.OpenedAmount;

        LaserCutObject laseredObject = gameObject.GetComponent<LaserCutObject>();
        if (laseredObject)
        {
            if (result.OpenAtOnce && !laseredObject.isCutOpen)
            {
                // What LaserCutObject.OnEnable does for a door that was saved cut open: open at once and drop the loose cut-out piece. Left to Update, a joiner would see the door
                // being cut open (and the piece in the doorway for five minutes, doorTimer).
                laseredObject.CutOpenDoor();
                if (laseredObject.destroyCutObjectOnLoad && laseredObject.cutFloatAwayObject)
                {
                    UnityEngine.Object.Destroy(laseredObject.cutFloatAwayObject);
                }
            }
            else if (result.Sealed)
            {
                laseredObject.lastCutValue = result.OpenedAmount;
                if (result.ShowCutFx)
                {
                    laseredObject.ActivateFX();
                }
            }
        }

        // A supply crate that was sealed and is cut open shares its id with its Sealed component, and the server keeps one metadata per entity: the cut (this) is the last
        // report of the opening and replaces the crate's own, so an unsealed crate is an open crate
        if (!door._sealed && gameObject.TryGetComponent(out SupplyCrate crate))
        {
            SupplyCrateMetadataProcessor.SetOpen(crate, true);
        }
    }

    /// <summary>
    /// The object was made in this frame: the Start methods that look up the Sealed component (StarshipDoor.Start, SupplyCrate.Start) have not run yet, and run one frame after the
    /// object is activated. An object that is not one of those is treated as live.
    /// </summary>
    private static bool IsFreshlySpawned(GameObject gameObject)
    {
        if (gameObject.TryGetComponent(out StarshipDoor starshipDoor))
        {
            return !starshipDoor.sealedComponent;
        }
        if (gameObject.TryGetComponent(out SupplyCrate crate))
        {
            return !crate.sealedComp;
        }
        return false;
    }
}
