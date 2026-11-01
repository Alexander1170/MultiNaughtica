using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;
[Serializable]
public class PlaySunbeamEvent(string eventKey) : Packet
{
    public string EventKey { get; } = eventKey;

    /// <summary>
    ///     An ordered list of the goals forming part of the whole Sunbeam story.
    /// </summary>
    /// <remarks>
    ///     If you modify this list, make sure to accordingly modify <see cref="SunbeamEvent" />.
    /// </remarks>
    [NonSerialized]
    public static readonly string[] SunbeamGoals =
    [
        SunbeamEvent.STORYSTART.ToStoryKey(),
        "OnPlayRadioSunbeamStart",
        "RadioSunbeam1",
        "OnPlayRadioSunbeam1",
        "RadioSunbeam2",
        "OnPlayRadioSunbeam2",
        "RadioSunbeam3",
        "OnPlayRadioSunbeam3",
        "RadioSunbeam4",
        SunbeamEvent.COUNTDOWN.ToStoryKey(),
        SunbeamEvent.GUNAIM.ToStoryKey(),
        "PrecursorGunAim",
        SunbeamRangeDecision.RANGE_CHECK_GOAL,
        SunbeamRangeDecision.OUT_OF_RANGE_GOAL,
        SunbeamRangeDecision.IN_RANGE_GOAL
    ];

    /// <summary>
    ///     Associates an understandable event name and the associated goal from <see cref="SunbeamGoals" />.
    /// </summary>
    public enum SunbeamEvent
    {
        STORYSTART,
        COUNTDOWN,
        GUNAIM
    }
}
