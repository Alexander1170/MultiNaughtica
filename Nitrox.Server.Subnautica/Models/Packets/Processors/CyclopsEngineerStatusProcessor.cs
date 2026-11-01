using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The engine heat that the engineer at a console wants to see (<see cref="CyclopsEngineerStatus" />): only the client that simulates the Cyclops has it. The server takes it from that client only (the player who
///     holds the Cyclops' lock), only while somebody sits at the console, at most once in <see cref="CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS" /> for a Cyclops, with the numbers clamped, and hands it to the
///     engineer alone.
/// </summary>
internal sealed class CyclopsEngineerStatusProcessor(
    CyclopsEngineerArbiter arbiter,
    SimulationOwnershipData simulationOwnershipData,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CyclopsEngineerStatusProcessor> logger) : IAuthPacketProcessor<CyclopsEngineerStatus>
{
    private readonly CyclopsEngineerArbiter arbiter = arbiter;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CyclopsEngineerStatusProcessor> logger = logger;
    private readonly Lock gate = new();
    private readonly Dictionary<NitroxId, long> lastSent = [];

    /// <summary>The clock of the floor between two statuses, in milliseconds; a test sets its own.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    public async Task Process(AuthProcessorContext context, CyclopsEngineerStatus packet)
    {
        if (packet.CyclopsId == null || !options.Value.VehicleUpgrades)
        {
            return;
        }
        if (!arbiter.TryGetEngineer(packet.CyclopsId, out CyclopsEngineerArbiter.Seat seat))
        {
            return;
        }
        Player? simulator = simulationOwnershipData.GetPlayerForLock(packet.CyclopsId);
        if (simulator == null || simulator.SessionId != context.Sender.SessionId)
        {
            logger.ZLogDebug($"{context.Sender.Name} sent the engine heat of a Cyclops it does not simulate; dropped");
            return;
        }
        if (!TryAccept(packet.CyclopsId))
        {
            return;
        }

        float power = float.IsNaN(packet.PowerPercent) || float.IsInfinity(packet.PowerPercent) ? 0f : Math.Max(0f, Math.Min(100f, packet.PowerPercent));
        await context.SendAsync(new CyclopsEngineerStatus(packet.CyclopsId, CyclopsEngineerRules.ClampHeat(packet.Heat), packet.Heating, packet.EngineOn, power), seat.Session);
    }

    private bool TryAccept(NitroxId cyclopsId)
    {
        long now = Clock();
        lock (gate)
        {
            if (lastSent.TryGetValue(cyclopsId, out long last) && now - last < CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS)
            {
                return false;
            }
            lastSent[cyclopsId] = now;
            if (lastSent.Count > 32)
            {
                foreach (NitroxId old in lastSent.Where(pair => now - pair.Value >= 60_000).Select(pair => pair.Key).ToList())
                {
                    lastSent.Remove(old);
                }
            }
            return true;
        }
    }
}
