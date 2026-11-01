using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server asks the receiving client to run a dev console command (for example
///     <c>goal story Goal_Disable_Gun</c>) as if its player had typed it, so the client-to-server sync of the effect is exercised
///     without driving the in-game console. Sent by <c>/console</c>.
/// </summary>
[Serializable]
public class DebugConsoleCommand(string command) : Packet
{
    public string Command { get; } = command;
}
