#if DEBUG
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: makes a player's client run a dev console command (e.g. <c>/console Alice goal story Goal_Disable_Gun</c>) as if that
///     player had typed it. The effect then travels through the normal client-to-server sync, which is what scenarios want to measure.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class ConsoleCommand(ILogger<ConsoleCommand> logger) : ICommandHandler<Player, string, string, string>
{
    private readonly ILogger<ConsoleCommand> logger = logger;

    [Description("Test harness: runs a dev console command on a player's client (the command can have up to three words)")]
    public async Task Execute(ICommandContext context, [Description("player whose client runs it")] Player target, [Description("console command")] string command, [Description("first argument")] string arg1 = "", [Description("second argument")] string arg2 = "")
    {
        string line = string.Join(' ', new[] { command, arg1, arg2 }.Where(part => part != ""));
        await context.SendAsync(target.SessionId, new DebugConsoleCommand(line));
        logger.ZLogInformation($"[SyncHarness] console '{line}' sent to {target.Name}");
        await context.ReplyAsync($"{target.Name} runs '{line}'");
    }
}
#endif
