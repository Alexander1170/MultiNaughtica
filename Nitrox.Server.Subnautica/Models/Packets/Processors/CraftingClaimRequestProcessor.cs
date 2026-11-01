using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
///     Crafting from lockers: removes the items a crafting player claims out of the lockers of a base or a Cyclops, or tells the player that somebody was faster. The removal is the same
///     one an <c>EntityDestroyed</c> does and it is announced to everybody (the claiming player included: its copy of the items goes away with the others'); the answer is sent after
///     those packets, and the next claim is not decided before they are out (<see cref="CraftingClaimArbiter.ClaimAsync" />), so a granted answer arrives at a client whose lockers
///     already reflect the claim, and a refused client has heard of the removal that refused it.
/// </summary>
internal sealed class CraftingClaimRequestProcessor(
    CraftingClaimArbiter craftingClaimArbiter,
    EntityRegistry entityRegistry,
    WorldEntityManager worldEntityManager,
    EntitySimulation entitySimulation,
    IOptions<SubnauticaServerOptions> options,
    ILogger<CraftingClaimRequestProcessor> logger) : IAuthPacketProcessor<CraftingClaimRequest>
{
    private readonly CraftingClaimArbiter craftingClaimArbiter = craftingClaimArbiter;
    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly WorldEntityManager worldEntityManager = worldEntityManager;
    private readonly EntitySimulation entitySimulation = entitySimulation;
    private readonly IOptions<SubnauticaServerOptions> options = options;
    private readonly ILogger<CraftingClaimRequestProcessor> logger = logger;

    public async Task Process(AuthProcessorContext context, CraftingClaimRequest packet)
    {
        if (!options.Value.CraftFromLockers)
        {
            await context.ReplyAsync(new CraftingClaimResponse(packet.ClaimId, CraftingClaimOutcome.Disabled, []));
            return;
        }

        // Not a reason to refuse (the server's idea of the sub root a player is in can lag behind), but it says which side is wrong when a claim is refused
        if (packet.ScopeId != null && (!context.Sender.SubRootId.HasValue || !context.Sender.SubRootId.Value.Equals(packet.ScopeId)))
        {
            logger.ZLogWarning($"{context.Sender.Name} claims items of {packet.ScopeId} but is in {(context.Sender.SubRootId.HasValue ? context.Sender.SubRootId.Value.ToString() : "no sub root")} according to the server");
        }

        await craftingClaimArbiter.ClaimAsync(entityRegistry, packet.ScopeId, packet.ItemIds, Remove, async decision =>
        {
            if (decision.Outcome == CraftingClaimOutcome.Granted)
            {
                logger.ZLogInformation($"{context.Sender.Name} crafts from the lockers of {packet.ScopeId}: took {decision.Removed.Count} items ({string.Join(", ", decision.Removed.Select(entity => entity.TechType))})");
                foreach (Entity removed in decision.Removed)
                {
                    await context.SendToAllAsync(new EntityDestroyed(removed.Id));
                }
            }
            else
            {
                logger.ZLogInformation($"{context.Sender.Name} could not craft from the lockers of {packet.ScopeId}: {decision.Outcome}, {decision.Unavailable.Count} of {packet.ItemIds?.Count ?? 0} items unavailable");
            }

            await context.ReplyAsync(new CraftingClaimResponse(packet.ClaimId, decision.Outcome, [.. decision.Unavailable]));
        });
    }

    private Entity? Remove(NitroxId id)
    {
        if (!worldEntityManager.TryDestroyEntity(id, out Entity? entity))
        {
            return null;
        }
        entitySimulation.EntityDestroyed(id);
        return entity;
    }
}
