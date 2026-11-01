using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;
using Terrain = NitroxClient.GameLogic.Terrain;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerTeleportedProcessor : IClientPacketProcessor<PlayerTeleported>
{
    public Task Process(ClientProcessorContext context, PlayerTeleported packet)
    {
        // A rider of a Seamoth's bar hangs in the locked mode on the Seamoth's anchor: it lets go first, or the lerp of the locked mode pulls the player back to the Seamoth and the teleport is lost
        RiderBars.ReleaseLocal(RiderReleaseReason.Teleported, true, false);
        Player.main.OnPlayerPositionCheat();

        Vehicle currentVehicle = Player.main.currentMountedVehicle;
        if (currentVehicle)
        {
            currentVehicle.TeleportVehicle(packet.DestinationTo.ToUnity(), currentVehicle.transform.rotation);
            Player.main.WaitForTeleportation();
            return Task.CompletedTask;
        }

        Vector3 destination = packet.DestinationTo.ToUnity();
        Player.main.SetPosition(destination);

        if (packet.SubRootID.HasValue && NitroxEntity.TryGetComponentFrom(packet.SubRootID.Value, out SubRoot subRoot))
        {
            Player.main.SetCurrentSub(subRoot, true);
            return Task.CompletedTask;
        }

        if (Terrain.IsLoadedAt(destination))
        {
            // The terrain is there already, so the player has nothing to fall through: don't take the controls away for a world that may take its time to settle
            Log.Info($"The terrain at {destination.ToString("F0")} is loaded already, not freezing the player");
            Player.main.WaitForTeleportation();
            return Task.CompletedTask;
        }

        // Freeze the player while it's loading its new position, for Terrain.TELEPORT_WORLD_LOAD_TIMEOUT at most
        Player.main.cinematicModeActive = true;
        Player.main.WaitForTeleportation();

        CoroutineHost.StartCoroutine(Terrain.SafeWaitForWorldLoad(Terrain.TELEPORT_WORLD_LOAD_TIMEOUT));
        return Task.CompletedTask;
    }
}
