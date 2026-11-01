using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server tells every client to start or stop its CSV sync sampler.
///     Sent by the <c>/sync-start</c> and <c>/sync-stop</c> server commands.
/// </summary>
[Serializable]
public class DebugSyncSamplerControl(bool enabled, string label, float creatureRadius) : Packet
{
    public bool Enabled { get; } = enabled;

    /// <summary>
    ///     Free text written into the CSV header so a run can be identified.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    ///     Creatures further than this (meters) from the local player are not sampled, to keep the CSV small.
    /// </summary>
    public float CreatureRadius { get; } = creatureRadius;
}
