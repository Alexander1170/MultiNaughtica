using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class LiveMixin_TakeDamage_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((LiveMixin t) => t.TakeDamage(default(float), default(Vector3), default(DamageType), default(GameObject)));

    public static bool Prefix(out float __state, LiveMixin __instance, float originalDamage, Vector3 position, DamageType type, GameObject dealer)
    {
        // Persist the previous health value
        __state = __instance.health;

        // Only the simulating player changes a creature's health (see CreatureHealthManager)
        if (CreatureHealthManager.IsHealthSynced(__instance, out NitroxId creatureId))
        {
            return Resolve<CreatureHealthManager>().ShouldApplyDamage(__instance, creatureId, originalDamage, position, type, dealer);
        }

        if (!Resolve<LiveMixinManager>().IsWhitelistedUpdateType(__instance))
        {
            return true; // everyone should process this locally
        }

        if (Resolve<LiveMixinManager>().ShouldApplyNextHealthUpdate(__instance, dealer))
        {
            return !IsSecondReportOfCreatureHit(__instance, originalDamage, dealer);
        }

        RelayCreatureAttackOnUnownedVehicle(__instance, originalDamage, position, type, dealer);
        return false;
    }

    public static void Postfix(float __state, LiveMixin __instance, float originalDamage, Vector3 position, DamageType type, GameObject dealer, bool __runOriginal)
    {
        if (!__runOriginal)
        {
            return;
        }

        // IsRemoteHealthChanging means we're replicating an action from the server and BaseCell is managed by BaseLeakManager
        if (Resolve<LiveMixinManager>().IsRemoteHealthChanging || __instance.GetComponent<BaseCell>())
        {
            return;
        }

        // PvP damage is always 0 so we need to check for it before the regular case
        if (HandlePvP(__instance, dealer, originalDamage))
        {
            return;
        }

        // Remote players' bodies are invincible, the victim has to be told that a creature we simulate hit them
        if (HandleCreatureAttackOnRemotePlayer(__instance, dealer, originalDamage, position, type))
        {
            return;
        }

        // At this point, if the victim didn't take damage, there's no point in broadcasting it
        if (__state == __instance.health)
        {
            return;
        }

        Resolve<CreatureHealthManager>().OnHealthChanged(__instance);
        BroadcastDefaultTookDamage(__instance);
    }

    private static bool HandlePvP(LiveMixin liveMixin, GameObject dealer, float damage)
    {
        if (!liveMixin.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            return false;
        }

        // Dealer must be the local player, and we need to know about the item they're holding
        if (dealer != Player.mainObject || !Inventory.main.GetHeldObject())
        {
            return false;
        }
        
        PvPAttack.AttackType attackType;
        switch (Inventory.main.GetHeldTool())
        {
            case HeatBlade:
                attackType = PvPAttack.AttackType.HeatbladeHit;
                break;
            case Knife:
                attackType = PvPAttack.AttackType.KnifeHit;
                break;
            default:
                // We don't want to send non-registered attacks
                return false;
        }

        Resolve<IPacketSender>().Send(new PvPAttack(remotePlayerIdentifier.RemotePlayer.SessionId, damage, attackType));
        return true;
    }

    /// <summary>
    /// Creatures whose attacks are synced (<see cref="AI.IsCreatureWhitelisted"/>) are replicated on the victim's side: their local copy bites the local player.
    /// Every other creature only attacks the remote body on the simulating player's side, so the hit is relayed to the victim.
    /// </summary>
    private static bool HandleCreatureAttackOnRemotePlayer(LiveMixin liveMixin, GameObject dealer, float damage, Vector3 position, DamageType type)
    {
        // The electric attack of the Ampeel has no dealer
        dealer = dealer ? dealer : ShockerMeleeAttack_OnTouch_Patch.CurrentShocker;
        if (!dealer || !liveMixin.TryGetComponent(out RemotePlayerIdentifier remotePlayerIdentifier))
        {
            return false;
        }

        Creature creature = dealer.GetComponentInParent<Creature>();
        if (!creature)
        {
            return false;
        }

        if (damage > 0f && !Resolve<AI>().IsCreatureWhitelisted(creature) &&
            creature.TryGetNitroxId(out NitroxId creatureId) && Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            Log.Info($"[CreatureAttackedPlayer] relaying bite of creature {creatureId} ({creature.name}) on {remotePlayerIdentifier.RemotePlayer.PlayerName}: damage {damage}");
            Resolve<IPacketSender>().Send(new CreatureAttackedPlayer(remotePlayerIdentifier.RemotePlayer.SessionId, creatureId, damage, type, position.ToDto()));
        }
        return true;
    }

    /// <summary>
    /// The creature behind damage to a vehicle: the dealer, or the creature whose attack deals damage without naming itself (the Ampeel's shock, the grip of a Reaper, ...).
    /// </summary>
    private static GameObject GetVehicleDamageDealer(GameObject dealer)
    {
        if (dealer)
        {
            return dealer;
        }
        return ShockerMeleeAttack_OnTouch_Patch.CurrentShocker ? ShockerMeleeAttack_OnTouch_Patch.CurrentShocker : CreatureDamageSource.Current;
    }

    /// <summary>
    /// Only a vehicle's simulating player may change its health, so a hit from a creature we simulate on a vehicle someone else simulates would be lost. It is relayed
    /// to the vehicle's simulating player, for every creature: a synced creature (<see cref="AI.IsCreatureWhitelisted"/>) is replicated there and its copy may bite the
    /// vehicle itself, but not when it is not loaded (the holder is far away) or when the attack only runs on the creature's simulator (the Sea Dragon's shove,
    /// the explosion of a Crashfish); the receiving side tells the two reports of one hit apart (<see cref="CreatureVehicleHitMatcher"/>).
    /// </summary>
    private static void RelayCreatureAttackOnUnownedVehicle(LiveMixin vehicleLiveMixin, float damage, Vector3 position, DamageType type, GameObject dealer)
    {
        dealer = GetVehicleDamageDealer(dealer);
        if (!dealer || !(damage > 0f) || Resolve<LiveMixinManager>().IsRemoteHealthChanging || !vehicleLiveMixin.TryGetNitroxId(out NitroxId vehicleId))
        {
            return;
        }

        Creature creature = dealer.GetComponentInParent<Creature>();
        if (creature && creature.TryGetNitroxId(out NitroxId creatureId) && Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            Resolve<IPacketSender>().Send(new CreatureAttackedVehicle(vehicleId, creatureId, damage, type, position.ToDto()));
        }
    }

    /// <summary>
    /// A hit of a creature simulated by another player on a vehicle we simulate reaches us twice when our copy of the creature bites too: once from this patch and once
    /// as <c>CreatureAttackedVehicle</c>. Whichever is first changes the health, the other is dropped (<see cref="CreatureVehicleHitMatcher"/>).
    /// </summary>
    private static bool IsSecondReportOfCreatureHit(LiveMixin vehicleLiveMixin, float damage, GameObject dealer)
    {
        CreatureVehicleHitMatcher matcher = Resolve<CreatureVehicleHitMatcher>();
        if (!(damage > 0f) || matcher.IsApplyingRelayedHit || Resolve<LiveMixinManager>().IsRemoteHealthChanging)
        {
            return false;
        }

        dealer = GetVehicleDamageDealer(dealer);
        if (!dealer || !vehicleLiveMixin.TryGetNitroxId(out NitroxId vehicleId))
        {
            return false;
        }

        // A creature we simulate hits our vehicle for good and no packet comes for it
        Creature creature = dealer.GetComponentInParent<Creature>();
        if (!creature || !creature.TryGetNitroxId(out NitroxId creatureId) || Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            return false;
        }

        return !matcher.ShouldApply(vehicleId, creatureId, CreatureVehicleHitMatcher.HitOrigin.Local, Time.time);
    }

    private static void BroadcastDefaultTookDamage(LiveMixin liveMixin)
    {
        // Let others know if we have a lock on this entity
        if (liveMixin.TryGetIdOrWarn(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(liveMixin.gameObject);

            if (metadata.HasValue)
            {
                Resolve<Entities>().BroadcastMetadataUpdate(id, metadata.Value);
            }
        }
    }
}
