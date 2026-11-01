using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: runs a dev console command for the local player on request of the server (/console). Only acts in Debug builds.
/// </summary>
internal sealed class DebugConsoleCommandProcessor : IClientPacketProcessor<DebugConsoleCommand>
{
    public Task Process(ClientProcessorContext context, DebugConsoleCommand packet)
    {
#if DEBUG
        if (DevConsole.instance == null)
        {
            Log.Warn($"[SyncHarness] cannot run console command '{packet.Command}': the dev console is not available");
            return Task.CompletedTask;
        }
        Log.Info($"[SyncHarness] running console command '{packet.Command}'");
        DevConsole.SendConsoleCommand(packet.Command);
#endif
        return Task.CompletedTask;
    }
}
