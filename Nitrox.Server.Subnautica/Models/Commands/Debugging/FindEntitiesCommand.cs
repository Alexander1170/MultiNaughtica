#if DEBUG
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Extensions;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: lists the world entities whose prefab path contains a text, for example <c>/findentities Locker 30 -78 365</c> (the 30 nearest to x -78, z 365) or <c>/findentities Starship_cargo</c>.
///     Finds out where lockers, crates and other things sit so that a scenario can walk both players there (R12c). The list goes to the server log, nearest
///     to the given point first, as <c>[SyncHarness] found ...</c> lines with a class id, the prefab path and the position.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class FindEntitiesCommand(EntityRegistry entityRegistry, IOptions<ServerStartOptions> options, ILogger<FindEntitiesCommand> logger) : ICommandHandler<string, int, float, float>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<FindEntitiesCommand> logger = logger;
    private readonly IOptions<ServerStartOptions> options = options;

    [Description("Test harness: lists world entities whose prefab path contains the text (nearest to you first) into the server log")]
    public async Task Execute(ICommandContext context, [Description("part of the prefab path, e.g. Locker")] string pathPart, [Description("how many to list")] int max = 30, [Description("x of the point the list is sorted by distance to")] float x = 0f, [Description("z of that point")] float z = 0f)
    {
        string databasePath = Path.Combine(options.Value.GetSubnauticaResourcesPath(), "StreamingAssets", "SNUnmanagedData", "prefabs.db");
        if (!File.Exists(databasePath))
        {
            await context.ReplyAsync($"prefab database not found: {databasePath}");
            return;
        }

        // Same format as PrefabPlaceholderGroupsResource.LoadPrefabDatabase: a count, then pairs of class id and prefab path
        Dictionary<string, string> pathByClassId = new();
        using (BinaryReader reader = new(File.OpenRead(databasePath)))
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string classId = reader.ReadString();
                string path = reader.ReadString();
                if (path.Contains(pathPart, StringComparison.OrdinalIgnoreCase))
                {
                    pathByClassId[classId] = path;
                }
            }
        }

        // Sorted by distance on the ground plane to (x, z)
        List<(WorldEntity Entity, string Path)> found = [];
        foreach (WorldEntity entity in entityRegistry.GetEntities<WorldEntity>())
        {
            if (entity.ClassId != null && pathByClassId.TryGetValue(entity.ClassId, out string? foundPath))
            {
                found.Add((entity, foundPath));
            }
        }

        float DistanceTo(WorldEntity entity) => MathF.Sqrt(MathF.Pow(entity.Transform.Position.X - x, 2) + MathF.Pow(entity.Transform.Position.Z - z, 2));
        found.Sort((a, b) => DistanceTo(a.Entity).CompareTo(DistanceTo(b.Entity)));
        logger.ZLogInformation($"[SyncHarness] found {found.Count} entities with a prefab path containing '{pathPart}' ({pathByClassId.Count} prefabs match), listing {Math.Min(max, found.Count)}");
        foreach ((WorldEntity entity, string path) in found.Take(max))
        {
            NitroxVector3 p = entity.Transform.Position;
            logger.ZLogInformation($"[SyncHarness] found {entity.ClassId} {path} at ({p.X:F1}, {p.Y:F1}, {p.Z:F1}) id {entity.Id}");
        }
        await context.ReplyAsync($"{found.Count} entities match '{pathPart}', see the server log");
    }
}
#endif
