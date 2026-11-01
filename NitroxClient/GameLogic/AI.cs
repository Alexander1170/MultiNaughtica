using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using static Nitrox.Model.Subnautica.Packets.RangedAttackLastTargetUpdate;

namespace NitroxClient.GameLogic;

public sealed class AI
{
    private readonly IPacketSender packetSender;
    private readonly Dictionary<Creature, CreatureAction> actions = [];
    private readonly Dictionary<string, Type> cachedCreatureActionTypeByFullName;

    /// <summary>
    /// Seconds after which an unchanged action is sent again (see <see cref="BroadcastNewAction"/>).
    /// </summary>
    private const float ACTION_REFRESH_INTERVAL = 5f;

    // Weak keys: entries disappear with the destroyed creature, no cleanup needed.
    private readonly ConditionalWeakTable<Creature, LastBroadcast> lastBroadcastByCreature = new();

    private sealed class LastBroadcast
    {
        public string ActionTypeName;
        public float Time;
    }

    /// <summary>
    /// Contains the types derived from CreatureAction which should be synced.
    /// Actions concerning the creature movement should be ignored as it's already done through SplineFollowing
    /// </summary>
    private readonly HashSet<Type> creatureActionWhitelist =
    [
        typeof(AttackLastTarget), typeof(RangedAttackLastTarget), typeof(AttackCyclops), typeof(Poop)
    ];

    /// <summary>
    /// In the future, ensure all creatures are synced. We want each of them to be individually
    /// checked (that all their actions are synced) before marking them as synced.
    /// </summary>
    private readonly HashSet<Type> syncedCreatureWhitelist =
    [
        typeof(ReaperLeviathan), typeof(SeaDragon), typeof(SeaTreader), typeof(GhostLeviathan)
    ];

    public AI(IPacketSender packetSender)
    {
        this.packetSender = packetSender;
        Assembly assembly = Assembly.GetAssembly(typeof(CreatureAction));
        cachedCreatureActionTypeByFullName = assembly.GetTypes()
                                                     .Where(type => typeof(CreatureAction).IsAssignableFrom(type))
                                                     .ToDictionary(t => t.FullName, t => t);
    }

    public void BroadcastNewAction(NitroxId creatureId, Creature creature, CreatureAction newAction)
    {
        if (!syncedCreatureWhitelist.Contains(creature.GetType()))
        {
            return;
        }

        // ChooseBestAction returns null when no action is above 0 (e.g. a resting creature): this is sent as an empty name which clears the action on the other clients.
        string actionTypeName = newAction == null ? string.Empty : newAction.GetType().FullName;

        // ChooseBestAction runs every AI tick. Only send changes, plus a periodic refresh so that players who spawned the creature after the last change still learn its current action.
        float now = Time.unscaledTime;
        LastBroadcast last = lastBroadcastByCreature.GetValue(creature, _ => new LastBroadcast());
        if (last.ActionTypeName == actionTypeName && now - last.Time < ACTION_REFRESH_INTERVAL)
        {
            return;
        }
        last.ActionTypeName = actionTypeName;
        last.Time = now;

        packetSender.Send(new CreatureActionChanged(creatureId, actionTypeName));
    }

    public void CreatureActionChanged(NitroxId id, string creatureActionTypeName)
    {
        if (!NitroxEntity.TryGetComponentFrom(id, out Creature creature))
        {
            return;
        }
        if (string.IsNullOrEmpty(creatureActionTypeName))
        {
            actions.Remove(creature);
            return;
        }
        if (cachedCreatureActionTypeByFullName.TryGetValue(creatureActionTypeName, out Type creatureActionType) &&
            creature.TryGetComponent(creatureActionType, out Component component) && component is CreatureAction creatureAction)
        {
            actions[creature] = creatureAction;
        }
    }

    public static void AggressiveWhenSeeTargetChanged(NitroxId creatureId, NitroxId targetId, bool locked, float aggressionAmount)
    {
        if (!NitroxEntity.TryGetComponentFrom(creatureId, out AggressiveWhenSeeTarget aggressiveWhenSeeTarget) ||
            !NitroxEntity.TryGetObjectFrom(targetId, out GameObject targetObject) ||
            IsDead(aggressiveWhenSeeTarget))
        {
            return;
        }

        Creature creature = aggressiveWhenSeeTarget.creature;

        // Code from AggressiveWhenSeeTarget.ScanForAggressionTarget
        creature.Aggression.Value = aggressionAmount;
        LastTarget lastTarget = aggressiveWhenSeeTarget.lastTarget;
        lastTarget.SetTargetInternal(targetObject);
        lastTarget.targetLocked = locked;

        if (aggressiveWhenSeeTarget.sightedSound && !aggressiveWhenSeeTarget.sightedSound.GetIsPlaying())
        {
            // This call doesn't broadcast a sound packet
            aggressiveWhenSeeTarget.sightedSound.StartEvent();
        }

        if (creature.TryGetComponent(out AttackLastTarget attackLastTarget))
        {
            attackLastTarget.currentTarget = targetObject;
        }
    }

    public static void AttackCyclopsTargetChanged(NitroxId creatureId, NitroxId targetId, float aggressiveToNoiseAmount)
    {
        if (!NitroxEntity.TryGetComponentFrom(creatureId, out AttackCyclops attackCyclops) ||
            !NitroxEntity.TryGetObjectFrom(targetId, out GameObject targetObject) ||
            IsDead(attackCyclops))
        {
            return;
        }

        // Kinda stuff from AttackCyclops.UpdateAggression
        attackCyclops.aggressiveToNoise.Value = aggressiveToNoiseAmount;
        // Force currentTarget to null to ensure SetCurrentTarget detects a change
        attackCyclops.currentTarget = null;
        attackCyclops.SetCurrentTarget(targetObject, targetObject.GetComponent<CyclopsDecoy>());
    }

    public static void RangedAttackLastTargetUpdate(NitroxId creatureId, NitroxId targetId, int attackTypeIndex, ActionState state)
    {
        if (!NitroxEntity.TryGetComponentFrom(creatureId, out RangedAttackLastTarget rangedAttackLastTarget) ||
            !NitroxEntity.TryGetObjectFrom(targetId, out GameObject targetObject) ||
            IsDead(rangedAttackLastTarget))
        {
            return;
        }

        RangedAttackLastTarget.RangedAttackType attackType = rangedAttackLastTarget.attackTypes[attackTypeIndex];
        rangedAttackLastTarget.currentAttack = attackType;
        rangedAttackLastTarget.currentTarget = targetObject;

        switch (state)
        {
            case ActionState.CHARGING:
                rangedAttackLastTarget.StartCharging(attackType);
                break;
            case ActionState.CASTING:
                rangedAttackLastTarget.StartCasting(attackType);
                break;
        }
    }

    public static void CreaturePoopPerformed(NitroxId creatureId)
    {
        if (NitroxEntity.TryGetComponentFrom(creatureId, out Poop poop))
        {
            // Code from Poop.Perform
            SafeAnimator.SetBool(poop.creature.GetAnimator(), poop.animationParameterName, false);
            poop.recourceSpawned = true;
        }
    }

    public bool TryGetActionForCreature(Creature creature, out CreatureAction action)
    {
        if (!actions.TryGetValue(creature, out action))
        {
            return false;
        }

        // The simulating player can send actions before learning that the creature died, they'd make its corpse act as if it was alive
        if (IsDead(creature))
        {
            actions.Remove(creature);
            action = null;
            return false;
        }
        return true;
    }

    public bool IsCreatureActionWhitelisted(CreatureAction creatureAction)
    {
        return creatureActionWhitelist.Contains(creatureAction.GetType());
    }

    public bool IsCreatureWhitelisted(Creature creature)
    {
        return syncedCreatureWhitelist.Contains(creature.GetType());
    }

    /// <summary>
    /// Remote AI updates can still arrive for a creature which already died for us (sent by its simulating player before it learned about the death)
    /// </summary>
    private static bool IsDead(Component creatureComponent)
    {
        LiveMixin liveMixin = creatureComponent.GetComponentInParent<LiveMixin>();
        return liveMixin && !liveMixin.IsAlive();
    }
}
