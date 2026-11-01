using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Gui.HUD;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class PlayerStatsProcessor(PlayerManager playerManager) : IClientPacketProcessor<PlayerStats>
{
    private readonly PlayerManager playerManager = playerManager;

    public Task Process(ClientProcessorContext context, PlayerStats playerStats)
    {
        if (playerManager.TryFind(playerStats.SessionId, out RemotePlayer remotePlayer))
        {
            // kept first and whatever else is missing (the bars exist only once the body does): the player list shows these numbers
            remotePlayer.RecordVitals(playerStats.Health, playerStats.Food, playerStats.Water, Time.unscaledTime);
            RemotePlayerVitals vitals = remotePlayer.vitals;
            if (vitals)
            {
                vitals.SetOxygen(playerStats.Oxygen, playerStats.MaxOxygen);
                vitals.SetHealth(playerStats.Health);
                vitals.SetFood(playerStats.Food);
                vitals.SetWater(playerStats.Water);
            }
            remotePlayer.UpdateHealthAndInfection(playerStats.Health, playerStats.InfectionAmount);
        }
        return Task.CompletedTask;
    }
}
