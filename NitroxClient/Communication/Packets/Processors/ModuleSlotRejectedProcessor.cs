using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.GameLogic;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     The server refused the install of a module because another player put a different module into the same slot first (V0, U4): the module goes back into the inventory,
///     see <see cref="EquipmentSlots.TakeBackModule" />.
/// </summary>
internal sealed class ModuleSlotRejectedProcessor(EquipmentSlots equipmentSlots) : IClientPacketProcessor<ModuleSlotRejected>
{
    private readonly EquipmentSlots equipmentSlots = equipmentSlots;

    public Task Process(ClientProcessorContext context, ModuleSlotRejected packet)
    {
        equipmentSlots.TakeBackModule(packet.ItemId, packet.ParentId, packet.Slot);
        return Task.CompletedTask;
    }
}
