#if DEBUG
using System.ComponentModel;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: spawns a creature (any TechType, e.g. Peeper, Stalker, ReaperLeviathan) at world coordinates.
///     Executed by the issuing player's client through the dev console's spawn path, so the creature is then synced like
///     any client-spawned entity.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
[RequiresOrigin(CommandOrigin.PLAYER)]
internal sealed class SpawnCreatureCommand(ILogger<SpawnCreatureCommand> logger) : ICommandHandler<string, float, float, float>
{
    private readonly ILogger<SpawnCreatureCommand> logger = logger;

    [Description("Test harness: spawns a creature (TechType name) at x y z from your client")]
    public async Task Execute(ICommandContext context, [Description("TechType, e.g. Peeper or Stalker")] string techType, [Description("x")] float x, [Description("y")] float y, [Description("z")] float z)
    {
        if (!Enum.TryParse(techType, true, out TechType parsedTechType) || parsedTechType == TechType.None)
        {
            await context.ReplyAsync($"Unknown TechType '{techType}'");
            return;
        }
        NitroxVector3 position = new(x, y, z);
        await context.ReplyAsync(new DebugSpawnEntity(parsedTechType.ToDto(), position, NitroxQuaternion.Identity));
        logger.ZLogInformation($"[SyncHarness] spawncreature {parsedTechType} at {position} requested by {context.OriginName}");
        await context.ReplyAsync($"Spawning {parsedTechType} at {position} from your client");
    }
}
#endif
