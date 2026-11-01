using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     The outcome of the Sunbeam range check (vanilla <c>StoryGoalCustomEventHandler.NotifyGoalComplete("SunbeamCheckPlayerRange")</c>), which picks which of two mutually
///     exclusive PDA goals plays. Vanilla decides it from the local player alone, so with two players each client picked its own goal and both were broadcast.<br />
///     Client to server: a proposal, sent by every client when the range check goal completes on it (<see cref="InRange" /> is true if any connected player is at the landing site).<br />
///     Server to client: the outcome the server accepted (the first proposal after the range check started), sent to everyone; every client then triggers that one goal.
///     A proposal that arrives after the decision is answered with the accepted outcome to the proposer only.
/// </summary>
[Serializable]
public class SunbeamRangeDecision : Packet
{
    public const string RANGE_CHECK_GOAL = "SunbeamCheckPlayerRange";
    public const string IN_RANGE_GOAL = "PDASunbeamDestroyEventInRange";
    public const string OUT_OF_RANGE_GOAL = "PDASunbeamDestroyEventOutOfRange";

    public bool InRange { get; }

    public SunbeamRangeDecision(bool inRange)
    {
        InRange = inRange;
    }

    public override string ToString()
    {
        return $"[{nameof(SunbeamRangeDecision)} {nameof(InRange)}: {InRange}]";
    }
}
