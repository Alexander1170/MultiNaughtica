#if DEBUG
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: starts the CSV sync sampler on every connected client. See the design notes (not part of this repository) (plan step 3).
///     Logs LiteNetLib's measured round trip time to every player so the run can prove the latency it had.
/// </summary>
[Alias("sync-start")]
[RequiresPermission(Perms.ADMIN)]
internal sealed class SyncStartCommand(LiteNetLibServerService networkService, PlayerManager playerManager, ILogger<SyncStartCommand> logger) : ICommandHandler<string, float>
{
    private readonly LiteNetLibServerService networkService = networkService;
    private readonly PlayerManager playerManager = playerManager;
    private readonly ILogger<SyncStartCommand> logger = logger;

    [Description("Test harness: starts the sync sampler on all clients (each writes logs/sync-<player>.csv at 10 Hz)")]
    public async Task Execute(ICommandContext context, [Description("label written into the CSV header")] string label = "run", [Description("creatures within this many meters of each player are sampled")] float creatureRadius = 80)
    {
        await context.SendToAllAsync(new DebugSyncSamplerControl(true, label, creatureRadius));
        logger.ZLogInformation($"[SyncHarness] sampler START label={label} creatureRadius={creatureRadius}");
        LogRoundTripTimes(logger, networkService, playerManager, "start");
        await context.ReplyAsync($"Sync sampler started on all clients (label '{label}')");
    }

    /// <summary>
    ///     One log line per connected player: <c>[SyncHarness] rtt start Alice 62 ms</c>. The runner reads these lines.
    /// </summary>
    public static void LogRoundTripTimes(ILogger logger, LiteNetLibServerService networkService, PlayerManager playerManager, string moment)
    {
        foreach ((SessionId sessionId, int roundTripMs) in networkService.GetPeerRoundTripTimes())
        {
            string name = playerManager.TryGetPlayerBySessionId(sessionId, out Player? player) ? player.Name : $"session{sessionId}";
            logger.ZLogInformation($"[SyncHarness] rtt {moment} {name} {roundTripMs} ms");
        }
    }
}
#endif
