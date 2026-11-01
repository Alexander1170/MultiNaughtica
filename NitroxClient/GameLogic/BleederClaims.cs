using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

/// <summary>
/// K18, Bleeder. The attack of a Bleeder (<see cref="AttachAndSuck" />) is a creature action, and the actions of a creature only run on the client that simulates it. The
/// contact that starts it is a physics callback that runs on every client's copy, so before this the copy of a player who does not simulate the Bleeder wrote the contact into
/// its own state (the player's <c>BleederAttachTarget</c> became "occupied", which made them immune to every Bleeder on their client) and nothing else happened, and a Bleeder
/// that the other player simulates never leeched the player it was touching.
/// The player whose copy accepted the contact now asks for the exclusive simulation lock of the Bleeder: the attack then runs on their client against their own player, all vanilla,
/// and the usual lock machinery arbitrates (a second player cannot take an attached Bleeder; the hand-over of the creatures between players skips exclusive locks). When the
/// Bleeder lets go the lock goes back to the normal hand-over. No packet is involved.
/// </summary>
public static class BleederClaims
{
    /// <summary>
    /// Seconds between the Bleeder letting go and the lock going back: long enough for the Propulsion Cannon, which grabs the Bleeder right after it detached it, to be seen.
    /// </summary>
    private const float RELEASE_DELAY = 0.5f;

    /// <summary>
    /// Seconds between two looks at whether the Propulsion Cannon still holds a Bleeder that was claimed.
    /// </summary>
    private const float CANNON_RECHECK_INTERVAL = 1f;

    /// <summary>
    /// Seconds after the lock was granted by which the attack must have started, or the contact is undone (vanilla picks the action with a rotating check and a more urgent
    /// action can keep the attack from starting; the contact would stay written into the player's attach target and the lock would stay exclusive).
    /// </summary>
    private const float ATTACK_START_TIMEOUT = 5f;

    private static readonly Dictionary<NitroxId, float> requestTimeById = [];

    /// <summary>
    /// The copy of the Bleeder just accepted a contact with the local player (vanilla found the target free and the player attackable).
    /// </summary>
    public static void Claim(AttachAndSuck attack, NitroxId id)
    {
        SimulationOwnership simulationOwnership = NitroxServiceLocator.LocateService<SimulationOwnership>();
        bool holdsExclusiveLock = simulationOwnership.HasExclusiveLock(id);
        float? secondsSinceRequest = requestTimeById.TryGetValue(id, out float requestTime) ? Time.time - requestTime : null;
        if (!BleederClaimRules.ShouldRequestClaim(holdsExclusiveLock, secondsSinceRequest))
        {
            if (!holdsExclusiveLock)
            {
                // Vanilla wrote the contact into the state of the copy and the player and nobody is going to answer for it
                attack.SetDetached();
            }
            return;
        }

        requestTimeById[id] = Time.time;
        Log.Info($"[Bleeder] {attack.name} touches the local player, asking for its simulation lock {id}");
        simulationOwnership.RequestSimulationLock(new LockRequest<BleederClaim>(id, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, new BleederClaim(attack)));
    }

    private static void ReceivedSimulationLockResponse(NitroxId id, bool lockAcquired, BleederClaim claim)
    {
        requestTimeById.Remove(id);
        AttachAndSuck attack = claim.Attack;
        bool stillHasTarget = attack && attack.currentTarget;
        BleederClaimRules.ClaimOutcome outcome = BleederClaimRules.JudgeClaimResponse(lockAcquired, stillHasTarget);
        Log.Info($"[Bleeder] simulation lock for {id}: {(lockAcquired ? "acquired" : "denied")}, {outcome}");

        switch (outcome)
        {
            case BleederClaimRules.ClaimOutcome.Detach:
                // Another player holds the Bleeder exclusively (it is attached to them): our copy undoes the contact it accepted, so that our player is free for other Bleeders
                if (attack)
                {
                    attack.SetDetached();
                }
                break;
            case BleederClaimRules.ClaimOutcome.ReleaseLock:
                // The lock is ours until the server answers the release, and a creature of ours has to stream its position (the previous simulator was told it lost the lock)
                EntityPositionBroadcaster.WatchEntity(id);
                NitroxServiceLocator.LocateService<SimulationOwnership>().RequestSimulationLock(id, SimulationLockType.TRANSIENT);
                break;
            case BleederClaimRules.ClaimOutcome.KeepLock:
                // Nothing else starts the position stream of a creature that was simulated by somebody else
                EntityPositionBroadcaster.WatchEntity(id);
                CoroutineHost.StartCoroutine(WatchAttackStart(attack, id));
                break;
        }
    }

    private static IEnumerator WatchAttackStart(AttachAndSuck attack, NitroxId id)
    {
        yield return new WaitForSeconds(ATTACK_START_TIMEOUT);
        if (attack && BleederClaimRules.ShouldAbandonClaim(NitroxServiceLocator.LocateService<SimulationOwnership>().HasExclusiveLock(id), attack.currentTarget, attack.active))
        {
            Log.Info($"[Bleeder] the attack of {id} did not start within {ATTACK_START_TIMEOUT:F0} s of the grant, undoing the contact");
            // Its postfix gives the lock back
            attack.SetDetached();
        }
    }

    /// <summary>
    /// The Bleeder let go of its target (<c>AttachAndSuck.SetDetached</c>, the only exit of the attack): the exclusive lock goes back to the hand-over of creatures between players.
    /// </summary>
    public static void OnDetached(AttachAndSuck attack, NitroxId id)
    {
        // A claim that waits for its answer owns the lock requests of this id: the answer of a plain request would be taken for it
        if (requestTimeById.ContainsKey(id) || !NitroxServiceLocator.LocateService<SimulationOwnership>().HasExclusiveLock(id))
        {
            return;
        }

        CoroutineHost.StartCoroutine(ReleaseLater(attack, id));
    }

    private static IEnumerator ReleaseLater(AttachAndSuck attack, NitroxId id)
    {
        yield return new WaitForSeconds(RELEASE_DELAY);
        SimulationOwnership simulationOwnership = NitroxServiceLocator.LocateService<SimulationOwnership>();

        // Gone with its cell (the server takes the lock of an entity that is removed), the lock went elsewhere, or a new contact started a claim
        while (attack && !requestTimeById.ContainsKey(id) && simulationOwnership.HasExclusiveLock(id))
        {
            // A new contact that was accepted meanwhile keeps the lock
            if (attack.currentTarget)
            {
                yield break;
            }

            // The Propulsion Cannon grabs the Bleeder right after it detached it. It gives the lock back when it lets go with ReleaseGrabbedObject, but a shot never calls that
            // (PropulsionCannon.OnShoot clears the grab itself), so we wait until the cannon does not hold the Bleeder any more and give the lock back ourselves if it is still ours
            if (IsHeldByPropulsionCannon(attack.gameObject))
            {
                yield return new WaitForSeconds(CANNON_RECHECK_INTERVAL);
                continue;
            }

            LiveMixin liveMixin = attack.GetComponent<LiveMixin>();
            bool alive = !liveMixin || liveMixin.IsAlive();
            if (BleederClaimRules.ShouldReleaseLockAfterDetach(true, attack.isActiveAndEnabled, alive))
            {
                simulationOwnership.RequestSimulationLock(id, SimulationLockType.TRANSIENT);
            }
            yield break;
        }
    }

    private static bool IsHeldByPropulsionCannon(GameObject creatureObject)
    {
        return Inventory.main && Inventory.main.GetHeldTool() is PropulsionCannonWeapon weapon && weapon.propulsionCannon && weapon.propulsionCannon.grabbedObject == creatureObject;
    }

    /// <summary>
    /// The server took the simulation of an entity from us (<see cref="SimulationOwnership" />). For a Bleeder in the middle of its attack the end of the attack is done here,
    /// because vanilla does it in <c>StopPerform</c> which does not run for a creature we do not simulate. Does nothing for any other entity.
    /// </summary>
    public static void OnLockLost(NitroxId id)
    {
        requestTimeById.Remove(id);
        if (NitroxEntity.TryGetComponentFrom(id, out AttachAndSuck attack) && BleederClaimRules.NeedsDetachAfterLockLoss(attack.active, attack.currentTarget))
        {
            Log.Info($"[Bleeder] lost the simulation of {id} during its attack, ending the attack here");
            attack.SetDetached();
            attack.active = false;
        }
    }
}
