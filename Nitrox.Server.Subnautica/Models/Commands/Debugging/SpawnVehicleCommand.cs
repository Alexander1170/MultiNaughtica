#if DEBUG
using System.ComponentModel;
using System.Linq;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Commands.Core;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: spawns a vehicle at world coordinates. The spawn is executed by the issuing player's client
///     through the same path as the <c>spawn seamoth</c> / <c>sub cyclops</c> dev console commands (so the regular
///     client-to-server entity sync is exercised), without touching the in-game console.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
[RequiresOrigin(CommandOrigin.PLAYER)]
internal sealed class SpawnVehicleCommand(ILogger<SpawnVehicleCommand> logger) : ICommandHandler<string, float, float, float>
{
    private static readonly string[] vehicleTechTypes = ["Seamoth", "Exosuit", "Cyclops"];
    private readonly ILogger<SpawnVehicleCommand> logger = logger;

    [Description("Test harness: spawns a vehicle at x y z from your client")]
    public async Task Execute(ICommandContext context, [Description("seamoth, exosuit or cyclops")] string vehicle, [Description("x")] float x, [Description("y")] float y, [Description("z")] float z)
    {
        string? techType = vehicleTechTypes.FirstOrDefault(v => v.Equals(vehicle, StringComparison.OrdinalIgnoreCase));
        if (techType == null)
        {
            await context.ReplyAsync($"Unknown vehicle '{vehicle}'. Use one of: {string.Join(", ", vehicleTechTypes)}");
            return;
        }
        NitroxVector3 position = new(x, y, z);
        await context.ReplyAsync(new DebugSpawnEntity(new NitroxTechType(techType), position, NitroxQuaternion.Identity));
        logger.ZLogInformation($"[SyncHarness] spawnvehicle {techType} at {position} requested by {context.OriginName}");
        await context.ReplyAsync($"Spawning {techType} at {position} from your client");
    }
}
#endif
