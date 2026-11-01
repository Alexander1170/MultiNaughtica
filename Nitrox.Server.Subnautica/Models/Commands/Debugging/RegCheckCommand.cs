#if DEBUG
using System.ComponentModel;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: writes to the SERVER LOG what the server knows about up to three entity ids (<c>/regcheck id1 id2 id3</c>): type, tech type, parent, position, and for a drillable
///     the chunk health in its metadata. <c>/query</c> only answers the chat of the caller, a scenario needs a line it can grep (R1, R3, R9, R12a, R12b).
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class RegCheckCommand(EntityRegistry entityRegistry, ILogger<RegCheckCommand> logger) : ICommandHandler<string, string, string>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<RegCheckCommand> logger = logger;

    [Description("Test harness: logs what the server knows about up to three entity ids")]
    public async Task Execute(ICommandContext context, [Description("entity id")] string id1, [Description("entity id")] string id2 = "", [Description("entity id")] string id3 = "")
    {
        foreach (string text in new[] { id1, id2, id3 })
        {
            if (text == "")
            {
                continue;
            }
            NitroxId id;
            try
            {
                id = new NitroxId(text);
            }
            catch (Exception)
            {
                logger.ZLogInformation($"[SyncHarness] regcheck {text}: not a NitroxId");
                continue;
            }
            if (!entityRegistry.TryGetEntityById(id, out Entity entity))
            {
                logger.ZLogInformation($"[SyncHarness] regcheck {id}: ABSENT from the registry");
                continue;
            }
            string position = entity is WorldEntity worldEntity ? worldEntity.Transform.Position.ToString() : "-";
            logger.ZLogInformation($"[SyncHarness] regcheck {id}: PRESENT {entity.GetType().Name} {entity.TechType} parent {entity.ParentId?.ToString() ?? "none"} position {position} metadata {entity.Metadata?.ToString() ?? "none"}");
        }
        await context.ReplyAsync("see the server log");
    }
}
#endif
