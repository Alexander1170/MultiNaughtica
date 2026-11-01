#if DEBUG
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: makes EVERY client run the same dev console command at the same moment (<c>/consoleall resact pickup AcidMushroom</c>), for races such as two players picking up
///     the same item (R9). One packet to all, so the two clients act within the difference of their links.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class ConsoleAllCommand(ILogger<ConsoleAllCommand> logger) : ICommandHandler<string, string, string>
{
    private readonly ILogger<ConsoleAllCommand> logger = logger;

    [Description("Test harness: runs a dev console command on every client at once (the command can have up to three words)")]
    public async Task Execute(ICommandContext context, [Description("console command")] string command, [Description("first argument")] string arg1 = "", [Description("second argument")] string arg2 = "")
    {
        string line = string.Join(' ', new[] { command, arg1, arg2 }.Where(part => part != ""));
        await context.SendToAllAsync(new DebugConsoleCommand(line));
        logger.ZLogInformation($"[SyncHarness] console '{line}' sent to everybody");
        await context.ReplyAsync($"Everybody runs '{line}'");
    }
}
#endif
