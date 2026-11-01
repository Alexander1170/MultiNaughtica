#if DEBUG
using System.ComponentModel;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: puts a player into a vehicle without hand-driven boarding. Seamoth/Exosuit: the player's client
///     teleports next to the vehicle and clicks it (same lock request path as pressing E). Cyclops: the player is placed
///     inside on foot, or at the helm with mode <c>pilot</c>.
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class WarpIntoCommand(EntityRegistry entityRegistry, ILogger<WarpIntoCommand> logger) : ICommandHandler<Player, string, string>
{
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<WarpIntoCommand> logger = logger;

    [Description("Test harness: puts a player into a vehicle (by NitroxId, or the nearest seamoth/exosuit/cyclops)")]
    public async Task Execute(ICommandContext context, [Description("player to move")] Player targetPlayer, [Description("vehicle NitroxId or tech type (seamoth, exosuit, cyclops)")] string vehicle, [Description("walk or pilot (Cyclops only)")] string mode = "walk")
    {
        NitroxId? vehicleId = null;
        try
        {
            vehicleId = new NitroxId(vehicle);
        }
        catch (Exception)
        {
            // Not an id: treat as tech type below.
        }

        if (vehicleId == null)
        {
            float bestDistance = float.MaxValue;
            foreach (VehicleEntity vehicleEntity in entityRegistry.GetEntities<VehicleEntity>())
            {
                if (vehicleEntity.TechType == null || !vehicleEntity.TechType.Name.Equals(vehicle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                float distance = NitroxVector3.Distance(vehicleEntity.Transform.Position, targetPlayer.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    vehicleId = vehicleEntity.Id;
                }
            }
        }

        if (vehicleId == null)
        {
            await context.ReplyAsync($"No vehicle matching '{vehicle}' (give a NitroxId or seamoth/exosuit/cyclops)");
            return;
        }

        bool pilot = mode.Equals("pilot", StringComparison.OrdinalIgnoreCase);
        await context.SendAsync(targetPlayer.SessionId, new DebugWarpInto(vehicleId, pilot));
        logger.ZLogInformation($"[SyncHarness] warpinto player={targetPlayer.Name} vehicle={vehicleId} pilot={pilot}");
        await context.ReplyAsync($"Warping {targetPlayer.Name} into {vehicleId}{(pilot ? " (pilot)" : "")}");
    }
}
#endif
