using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     The Seamoth Rider Bar (<see cref="SeamothRiderChanged" />): a swimming player asks to hold on to the bar of a Seamoth or lets go. The first request for a free Seamoth wins, and the server tells
///     everybody (the rider too, who only then climbs on); a request that cannot be is answered with a denial. A client may only send <see cref="RiderBarMessage.Hold" /> and
///     <see cref="RiderBarMessage.LetGo" />, the rest is the server's and is dropped.
/// </summary>
internal sealed class SeamothRiderChangedProcessor(
    SeamothRiderArbiter arbiter,
    EntityRegistry entityRegistry,
    IOptions<SubnauticaServerOptions> options,
    ILogger<SeamothRiderChangedProcessor> logger) : IAuthPacketProcessor<SeamothRiderChanged>
{
    private readonly SeamothRiderArbiter arbiter = arbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<SeamothRiderChangedProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, SeamothRiderChanged packet)
    {
        if (packet.VehicleId == null)
        {
            return;
        }

        switch (packet.Message)
        {
            case RiderBarMessage.Hold:
                SessionId session = context.Sender.SessionId;
                if (CanHold(context.Sender, packet) && arbiter.TryHold(packet.VehicleId, session))
                {
                    if (!context.Sender.IsOnline)
                    {
                        // The player left while this was being decided (the session cleaner has run already and did not find the ride): no ride is left behind for a session that is gone
                        arbiter.LetGo(packet.VehicleId, session);
                        break;
                    }
                    logger.ZLogInformation($"{context.Sender.Name} holds on to the bar of {packet.VehicleId}");
                    await context.SendToAllAsync(new SeamothRiderChanged(RiderBarMessage.Holding, packet.VehicleId, context.Sender.SessionId));
                }
                else
                {
                    await context.ReplyAsync(new SeamothRiderChanged(RiderBarMessage.Denied, packet.VehicleId, context.Sender.SessionId));
                }
                break;
            case RiderBarMessage.LetGo:
                if (arbiter.LetGo(packet.VehicleId, context.Sender.SessionId))
                {
                    await context.SendToAllAsync(new SeamothRiderChanged(RiderBarMessage.Released, packet.VehicleId, context.Sender.SessionId));
                }
                break;
            default:
                logger.ZLogWarning($"{context.Sender.Name} sent a rider message of the kind {packet.Message}, which only the server may send; dropped");
                break;
        }
    }

    /// <summary>
    /// The Seamoth exists and has the Rider Bar module, the vehicle upgrades are on, and the sender swims: it does not fly a vehicle and is not inside a base or a Cyclops. A player near their lifepod still has the lifepod as their
    /// sub root (the game keeps it until they are 15 m away), which is not a submarine: they swim.
    /// </summary>
    private bool CanHold(Player sender, SeamothRiderChanged packet)
    {
        if (!options.Value.VehicleUpgrades || !entityRegistry.TryGetEntityById(packet.VehicleId, out Entity? vehicle) || vehicle.TechType?.Name != PowerTransferRules.SEAMOTH_TECH)
        {
            return false;
        }
        if (!sender.IsOnline || sender.PlayerContext?.DrivingVehicle != null || IsInsideSub(sender))
        {
            return false;
        }
        if (!HasBar(vehicle))
        {
            // The only reason that is not the player's own doing: said, so that a bar the client has and the server does not know about can be found in the log
            string modules = string.Join(", ", vehicle.ChildEntities.OfType<InstalledModuleEntity>().Select(module => module.TechType?.Name ?? "?"));
            logger.ZLogInformation($"{sender.Name} cannot hold on to {packet.VehicleId}: the server knows no Rider Bar module in it (its modules: {(modules.Length > 0 ? modules : "none")})");
            return false;
        }
        return true;
    }

    /// <summary>The Seamoth holds a Rider Bar module in a slot (as the server knows its modules: the installed module entities that are its children).</summary>
    private static bool HasBar(Entity vehicle)
    {
        // A copy: the children of a vehicle change when modules are installed and taken out, as the slot arbiter knows
        return vehicle.ChildEntities.ToArray().Any(child => child is InstalledModuleEntity module && module.TechType?.Name == CoopItemTechNames.SEAMOTH_RIDER_BAR);
    }

    private bool IsInsideSub(Player sender)
    {
        return sender.SubRootId.HasValue && !(entityRegistry.TryGetEntityById(sender.SubRootId.Value, out Entity? sub) && sub is EscapePodEntity);
    }
}
