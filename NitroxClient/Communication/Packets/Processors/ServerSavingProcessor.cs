using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The server saves the world: the small "Saving..." indicator in the top left corner (item a, <see cref="AutosaveIndicator" />).
/// </summary>
internal sealed class ServerSavingProcessor : IClientPacketProcessor<ServerSaving>
{
    public Task Process(ClientProcessorContext context, ServerSaving packet)
    {
        Log.Info($"[{nameof(AutosaveIndicator)}] The server saves (save {packet.Number}): showing the indicator");
        AutosaveIndicator.Show(packet.Number);
        return Task.CompletedTask;
    }
}
