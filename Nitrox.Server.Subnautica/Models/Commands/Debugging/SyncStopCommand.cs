#if DEBUG
using System.ComponentModel;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Services;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: stops the CSV sync sampler on every connected client and flushes the files. Logs the measured round trip times again.
/// </summary>
[Alias("sync-stop")]
[RequiresPermission(Perms.ADMIN)]
internal sealed class SyncStopCommand(LiteNetLibServerService networkService, PlayerManager playerManager, ILogger<SyncStopCommand> logger) : ICommandHandler
{
    private readonly LiteNetLibServerService networkService = networkService;
    private readonly PlayerManager playerManager = playerManager;
    private readonly ILogger<SyncStopCommand> logger = logger;

    [Description("Test harness: stops the sync sampler on all clients")]
    public async Task Execute(ICommandContext context)
    {
        SyncStartCommand.LogRoundTripTimes(logger, networkService, playerManager, "stop");
        await context.SendToAllAsync(new DebugSyncSamplerControl(false, "", 0));
        logger.ZLogInformation($"[SyncHarness] sampler STOP");
        await context.ReplyAsync("Sync sampler stopped on all clients");
    }
}
#endif
