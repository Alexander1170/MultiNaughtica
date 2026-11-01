#if DEBUG
using System.ComponentModel;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: writes to the SERVER LOG where the server thinks every player is (<c>/players</c>): the sub root and the parent of the player's entity, which must agree
///     (PL6, PL8), and the position. Lines start with <c>[SyncHarness] serverplayers:</c>.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class PlayersCommand(PlayerManager playerManager, EntityRegistry entityRegistry, ILogger<PlayersCommand> logger) : ICommandHandler
{
    private readonly PlayerManager playerManager = playerManager;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<PlayersCommand> logger = logger;

    [Description("Test harness: logs where the server thinks every player is (sub root, entity parent, position)")]
    public async Task Execute(ICommandContext context)
    {
        foreach (Player player in playerManager.GetAllPlayers())
        {
            string subRoot = Describe(player.SubRootId.OrNull());
            string parent = "no entity";
            string parentType = "";
            if (entityRegistry.TryGetEntityById(player.GameObjectId, out Entity entity))
            {
                parent = Describe(entity.ParentId);
                parentType = entity.ParentId != null && entityRegistry.TryGetEntityById(entity.ParentId, out Entity parentEntity) ? $" ({parentEntity.GetType().Name} {parentEntity.TechType})" : "";
            }
            logger.ZLogInformation($"[SyncHarness] serverplayers: {player.Name} online {player.IsOnline} subRoot {subRoot} entityParent {parent}{parentType} position {player.Position}");
        }
        await context.ReplyAsync("see the server log");
    }

    private static string Describe(NitroxId? id) => id == null ? "none" : id.ToString()[..8];
}
#endif
