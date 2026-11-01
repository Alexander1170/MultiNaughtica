#if DEBUG
using System.ComponentModel;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Commands.Core;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.Commands.Debugging;

/// <summary>
///     Sync test harness: makes a player's client hit the nearest creature of a tech type (within 40 m) as that player, for example
///     <c>/damagecreature Bob Stalker 30</c>. Tests creature health and death sync, including across lock hand-overs:
///     with <c>handoverTo</c> the attacker hits in a burst of three and the creature's simulation lock is moved to that player in the same server tick
///     (the hand-over then lands while the attacker's health report is still throttled).
/// </summary>
[RequiresPermission(Perms.ADMIN)]
internal sealed class DamageCreatureCommand(EntityRegistry entityRegistry, SimulationOwnershipData simulationOwnershipData, ILogger<DamageCreatureCommand> logger) : ICommandHandler<Player, string, float, Player?>
{
    /// <summary>
    ///     Hits dealt back to back when the lock is handed over right after: the attacker's health report is then still throttled when the hand-over lands.
    /// </summary>
    private const int HANDOVER_BURST_HITS = 3;

    private readonly EntityRegistry entityRegistry = entityRegistry;
    private readonly ILogger<DamageCreatureCommand> logger = logger;
    private readonly SimulationOwnershipData simulationOwnershipData = simulationOwnershipData;

    [Description("Test harness: the player's client hits the nearest creature of a tech type (within 40 m) for the given damage, optionally as a burst of three followed by moving the creature's simulation lock to another player")]
    public async Task Execute(ICommandContext context, [Description("player who deals the hit")] Player attacker, [Description("creature TechType, e.g. Stalker")] string techType, [Description("damage per hit")] float damage, [Description("player who gets the creature's simulation lock right after a burst of hits")] Player? handoverTo = null)
    {
        int hits = handoverTo == null ? 1 : HANDOVER_BURST_HITS;

        // The creature the harness spawned (client-spawned, so not by the server) nearest to the attacker; any creature of the type when there is none
        WorldEntity? creature = null;
        float bestDistance = float.MaxValue;
        foreach (WorldEntity entity in entityRegistry.GetEntities<WorldEntity>())
        {
            if (entity.TechType == null || !entity.TechType.Name.Equals(techType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            float distance = NitroxVector3.Distance(entity.Transform.Position, attacker.Position) + (entity.SpawnedByServer ? 100000f : 0f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                creature = entity;
            }
        }

        await context.SendAsync(attacker.SessionId, new DebugDamageCreature(techType, damage, 40f, hits, Optional.OfNullable(creature?.Id)));
        logger.ZLogInformation($"[SyncHarness] {attacker.Name} damages {techType} {creature?.Id.ToString() ?? "(nearest one)"}: {hits} x {damage}");

        if (handoverTo == null || creature == null)
        {
            await context.ReplyAsync($"{attacker.Name} hits {techType} {hits} x {damage}");
            return;
        }

        Player? previousOwner = simulationOwnershipData.GetPlayerForLock(creature.Id);
        simulationOwnershipData.RevokeOwnerOfId(creature.Id);
        if (!simulationOwnershipData.TryToAcquire(creature.Id, handoverTo, SimulationLockType.TRANSIENT))
        {
            await context.ReplyAsync("Could not move the lock");
            return;
        }
        await context.SendToAllAsync(new SimulationOwnershipChange([new SimulatedEntity(creature.Id, handoverTo.SessionId, true, SimulationLockType.TRANSIENT)]));
        logger.ZLogInformation($"[SyncHarness] moved simulation of {creature.TechType} {creature.Id} from {previousOwner?.Name ?? "nobody"} to {handoverTo.Name}");
        await context.ReplyAsync($"{attacker.Name} hits {creature.Id} {hits} x {damage}, then {handoverTo.Name} simulates it");
    }
}
#endif
