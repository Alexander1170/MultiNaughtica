using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Takes the power another player used from a base we simulate (B10b) out of its generators. Done through <c>PowerSystem.ConsumeEnergy</c> like the use itself, not through
///     the patched consumers, so it is not reported again; the generators that change tell the others the new charge (<c>PowerSource_ModifyPower_Patch</c>).
/// </summary>
internal sealed class BasePowerConsumedProcessor : IClientPacketProcessor<BasePowerConsumed>
{
    public Task Process(ClientProcessorContext context, BasePowerConsumed packet)
    {
        if (!GameModeUtils.RequiresPower() || !NitroxEntity.TryGetObjectFrom(packet.BaseId, out GameObject baseObject))
        {
            return Task.CompletedTask;
        }

        BasePowerRelay relay = baseObject.GetComponentInChildren<BasePowerRelay>(true);
        if (relay)
        {
            PowerSystem.ConsumeEnergy(relay, packet.Amount, out float _);
        }
        return Task.CompletedTask;
    }
}
