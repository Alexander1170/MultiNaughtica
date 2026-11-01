using System.Collections;
using NitroxClient.GameLogic.InitialSync.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic.InitialSync;

/// <summary>
///     Makes sure the new items of the co-op build are registered with the game before any entity is spawned: inventories, vehicles with their modules, bases with their lockers and the Cyclops
///     all come after this (<see cref="GlobalRootInitialSyncProcessor" /> depends on it). The registration already ran before the client joined the server (<c>Multiplayer.StartSession</c>), so
///     this is the second line of defence and normally does nothing; it also applies the settings the join delivered, once the player processor has stored them.
/// </summary>
public sealed class CoopItemsInitialSyncProcessor : InitialSyncProcessor
{
    public CoopItemsInitialSyncProcessor()
    {
        AddDependency<PlayerInitialSyncProcessor>();
        // After the server's list of known technologies is restored: the unlock rules then see it (a world that knows Seamoth Depth Module MK3 knows MK4 and MK5 at the first join)
        AddDependency<PdaInitialSyncProcessor>();

        AddStep(EnsureRegistered);
    }

    private static IEnumerator EnsureRegistered(InitialPlayerSync packet)
    {
        if (!CoopItemRegistry.Registered)
        {
            yield return CoopItemRegistry.EnsureRegisteredAsync();
        }
        // The settings of the server arrived with PlayerInitialSyncProcessor, which has run, and so did the restore of the known technologies: bring the craft nodes, unlock rules and depth tables to
        // the state the settings ask for (the unlock rules then unlock what the restored technologies allow)
        CoopItemRegistry.ApplySettings();
    }
}
