using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

internal sealed class FireDousedProcessor(Fires fires, Entities entities) : IClientPacketProcessor<FireDoused>
{
    private readonly Entities entities = entities;
    private readonly Fires fires = fires;

    public Task Process(ClientProcessorContext context, FireDoused packet)
    {
        // A fire that is still waiting in the spawn queue cannot be doused yet: if this packet extinguishes it, it must not spawn afterwards as a fire only this client has
        // (the same guard as EntityDestroyedProcessor)
        if (packet.Health <= 0f && entities.SpawningEntities)
        {
            entities.MarkForDeletion(packet.Id);
        }

        fires.Douse(packet.Id, packet.Health, packet.SessionId, packet.DouseRate, packet.OneShot);
        return Task.CompletedTask;
    }
}
