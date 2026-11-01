using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
/// The simulating client's Crabsquid fired its EMP blast: our copy makes the same blast where it is, so that our vehicles, tools and Cyclops in range are hit too. Only the blast:
/// the creature's own attack state (it targets lights, swims toward them, waits between blasts) stays with the simulating client.
/// </summary>
internal sealed class EmpFiredProcessor : IClientPacketProcessor<EmpFired>
{
    public Task Process(ClientProcessorContext context, EmpFired packet)
    {
        if (!NitroxEntity.TryGetObjectFrom(packet.CreatureId, out GameObject creatureObject))
        {
            return Task.CompletedTask;
        }

        EMPAttack empAttack = creatureObject.GetComponentInChildren<EMPAttack>(true);
        LiveMixin liveMixin = creatureObject.GetComponentInParent<LiveMixin>();
        if (!empAttack || (liveMixin && !liveMixin.IsAlive()))
        {
            return Task.CompletedTask;
        }

        using (PacketSuppressor<EmpFired>.Suppress())
        {
            empAttack.CastEMPBlast();
        }
        Log.Debug($"[EmpFired] {creatureObject.name} fired its EMP blast on another client, made the blast here too");
        return Task.CompletedTask;
    }
}
