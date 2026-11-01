using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The requests this client has sent and not heard back about, one per pair of vehicles (<see cref="Nitrox.Model.Subnautica.Packets.PowerTransfer" />). A request is sent about once a second,
/// but only when the one before is answered or has timed out (<see cref="PowerTransferRules.ANSWER_TIMEOUT_SECONDS" />: a lost packet, a giver that is loading or paused, a lock that moved), so
/// a slow giver never gets a pile of requests and a dead one is asked again after a few seconds. Time is passed in (the clock of the game), so that the rule can be tested.
/// </summary>
public sealed class PowerTransferTracker
{
    private readonly Dictionary<(NitroxId Giver, NitroxId Receiver), double> started = new();

    public int Count => started.Count;

    /// <summary>True when a request for this pair may be sent now (and records it); false while the last one is still waiting for its answer.</summary>
    public bool TryBegin(NitroxId giver, NitroxId receiver, double now)
    {
        if (IsInFlight(giver, receiver, now))
        {
            return false;
        }
        started[(giver, receiver)] = now;
        return true;
    }

    public bool IsInFlight(NitroxId giver, NitroxId receiver, double now)
    {
        return started.TryGetValue((giver, receiver), out double since) && now - since < PowerTransferRules.ANSWER_TIMEOUT_SECONDS;
    }

    /// <summary>The answer to a request for this pair came (granted or not): the next request may go out at its turn.</summary>
    public void Complete(NitroxId giver, NitroxId receiver)
    {
        started.Remove((giver, receiver));
    }

    /// <summary>The session ended.</summary>
    public void Clear()
    {
        started.Clear();
    }
}
