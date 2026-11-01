using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The tow hook button of a Cyclops: a press of another player that this client has to carry out because it simulates the Cyclops (<see cref="VehicleTows.OnPressForwarded" />), or the answer to this
/// client's own press that it did nothing (<see cref="VehicleTows.OnPressFailed" />).
/// </summary>
internal sealed class CyclopsTowPressProcessor : IClientPacketProcessor<CyclopsTowPress>
{
    public Task Process(ClientProcessorContext context, CyclopsTowPress packet)
    {
        switch (packet.Kind)
        {
            case CyclopsTowPressKind.Forward:
                VehicleTows.OnPressForwarded(packet);
                break;
            case CyclopsTowPressKind.Failed:
                VehicleTows.OnPressFailed(packet);
                break;
        }
        return Task.CompletedTask;
    }
}
