using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server asks every client to write its story state (completed goals, pending radio messages, schedule,
///     PDA log and encyclopedia keys, a few flags) to <c>story-&lt;player&gt;.txt</c> in the Nitrox logs folder. The server writes
///     <c>story-server.txt</c> itself. Sent by <c>/storydump</c>, compared by a harness script that is not part of this repository.
/// </summary>
[Serializable]
public class DebugStoryDump(string label) : Packet
{
    public string Label { get; } = label;
}
