using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The tow hooks in the game (docs/coop/VEHICLE_UPGRADES.md). A Seamoth with the Seamoth Tow Hook in a quick slot hooks the nearest Seamoth or Prawn Suit that nobody sits in when the slot is switched on
/// and lets go when it is switched off; a Cyclops with the Cyclops Tow Hook does the same from a button on its lighting panel, by the light buttons, that anybody aboard can press. The <b>towing player's client</b> takes the simulation lock of the towed vehicle (an
/// exclusive request that is turned into a transient one at once, so that anybody can still get in) and simulates it: <see cref="TowLine" /> pulls it behind the tower, and its movement reaches
/// everybody like that of any simulated vehicle. Every client draws a cable between the two (<see cref="TowCable" />), which the server's <see cref="VehicleTowChanged" /> tells them about.
/// A tow is never saved. It ends when the player lets go, when somebody gets into the towed vehicle (its lock moves), when either vehicle docks or is destroyed, when the cable gets too long, when the
/// tower's lock moves (a Cyclops whose helm another player takes: they press the button again), when the hook is taken out and when the player leaves (the server says so).
/// <para>
/// The hooking of a Cyclops is always done by <b>the client that simulates the Cyclops</b>, whoever presses the button: a press of another player travels as a <see cref="CyclopsTowPress" /> through the
/// server to that client, which attaches or releases and tells everybody in <see cref="VehicleTowChanged" />; the button on every client shows the real tow state (<see cref="TowedName" />). When it
/// cannot be done the player who pressed is told why.
/// </para>
/// </summary>
public static class VehicleTows
{
    /// <summary>What a lock request for the towed vehicle needs to carry on when it is answered.</summary>
    public sealed class TowContext(NitroxId towerId, NitroxId towedId, Component tower, int slot, SessionId presser, int token) : LockRequestContext
    {
        /// <summary>The number of this attempt: an answer or a timer of an earlier attempt of the same tower must not complete or cancel this one.</summary>
        public int Token { get; } = token;

        public NitroxId TowerId { get; } = towerId;
        public NitroxId TowedId { get; } = towedId;
        public Component Tower { get; } = tower;
        public int Slot { get; } = slot;

        /// <summary>The player who pressed the Cyclops' button and has to hear why a hooking failed (not a player for a Seamoth's quick slot: the pilot is this client's own player).</summary>
        public SessionId Presser { get; } = presser;
    }

    /// <summary>A press of the button on a Cyclops that nobody simulated: this client takes the Cyclops' lock and does the hooking.</summary>
    public sealed class PressContext(SubRoot cyclops) : LockRequestContext
    {
        public SubRoot Cyclops { get; } = cyclops;
    }

    /// <summary>One tow that is on, on this client: its own (<see cref="Local" />) or somebody else's.</summary>
    public sealed class Tow
    {
        public NitroxId TowerId { get; init; }
        public NitroxId TowedId { get; init; }
        public SessionId Session { get; init; }

        /// <summary>True on the client of the towing player, which simulates the towed vehicle and pulls it.</summary>
        public bool Local { get; init; }

        /// <summary>The quick slot of the Seamoth's hook; -1 for a Cyclops.</summary>
        public int Slot { get; init; } = -1;

        public TowCable Cable { get; set; }
        public TowLine Line { get; set; }
        public float StartedAt { get; init; }
    }

    private static readonly Dictionary<NitroxId, Tow> byTowed = new();
    private static readonly Dictionary<NitroxId, NitroxId> towedByTower = new();
    /// <summary>A hooking in flight for a tower: its number, and the quick slot that began it (-1 for a Cyclops).</summary>
    private readonly record struct Attempt(int Token, int Slot);

    private static readonly Dictionary<NitroxId, Attempt> attaching = new();
    private static int lastToken;
    private static readonly Dictionary<NitroxId, float> pressesSent = new();
    private static bool subscribed;

    private static TechType SeamothHookTech => (TechType)CoopItems.SEAMOTH_TOW_HOOK;
    private static TechType CyclopsHookTech => (TechType)CoopItems.CYCLOPS_TOW_HOOK;

    public static IReadOnlyCollection<Tow> Tows => byTowed.Values;

    private static bool Active => VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;

    private static SimulationOwnership Ownership => NitroxServiceLocator.LocateService<SimulationOwnership>();

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
        VehicleUpgradeSettings.Changed += OnSettingsChanged;
    }

    /// <summary>The session ended: no tow goes on into the next one.</summary>
    public static void Reset()
    {
        foreach (Tow tow in new List<Tow>(byTowed.Values))
        {
            Remove(tow);
        }
        byTowed.Clear();
        towedByTower.Clear();
        attaching.Clear();
        pressesSent.Clear();
    }

    private static void OnSettingsChanged()
    {
        if (!VehicleUpgradeSettings.Enabled)
        {
            ReleaseLocal(TowReleaseReason.Disabled);
        }
    }

    // ---- questions the rest of the game asks

    /// <summary>True while this very tow is on (it may have been released a moment ago, in the same frame).</summary>
    public static bool IsOn(Tow tow) => byTowed.TryGetValue(tow.TowedId, out Tow current) && current == tow;

    /// <summary>True while this vehicle (or Cyclops) is the tower of a tow this client pulls.</summary>
    public static bool IsLocalTower(Component tower)
    {
        return towedByTower.Count > 0 && tower.TryGetNitroxId(out NitroxId id) && towedByTower.TryGetValue(id, out NitroxId towedId) && byTowed.TryGetValue(towedId, out Tow tow) && tow.Local;
    }

    /// <summary>The factor on the speed of the tower while it tows (<c>TowSpeedFactor</c>), 1 when it does not.</summary>
    public static float SpeedFactor(Component tower)
    {
        return TowRules.SpeedFactor(IsLocalTower(tower), VehicleUpgradeSettings.Enabled, VehicleUpgradeSettings.TowSpeedFactor);
    }

    /// <summary>The name of the vehicle the Seamoth or Cyclops tows (for the line on the screen); null when it tows nothing.</summary>
    public static string TowedName(Component tower)
    {
        if (!tower.TryGetNitroxId(out NitroxId id) || !towedByTower.TryGetValue(id, out NitroxId towedId) || !NitroxEntity.TryGetComponentFrom(towedId, out Vehicle towed) || !towed)
        {
            return null;
        }
        return towed.GetName();
    }

    public static bool HasCyclopsHook(SubRoot cyclops)
    {
        return cyclops && cyclops.isCyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(CyclopsHookTech) > 0;
    }

    public static bool HasSeamothHook(Vehicle seaMoth)
    {
        return seaMoth && seaMoth.modules != null && seaMoth.modules.GetCount(SeamothHookTech) > 0;
    }

    /// <summary>The line under "Press X to exit" for the pilot of a Seamoth that tows ("Towing X"). The Cyclops has no line: its hook is a button, and the button shows what it tows.</summary>
    public static bool TryGetLine(out string line)
    {
        line = null;
        Player player = Player.main;
        if (!player || towedByTower.Count == 0)
        {
            return false;
        }

        Vehicle vehicle = player.GetVehicle();
        if (vehicle is SeaMoth && vehicle.GetPilotingMode())
        {
            string name = TowedName(vehicle);
            if (name == null)
            {
                return false;
            }
            line = string.Format(Language.main.Get("Nitrox_TowSeamothTowing"), name);
            return true;
        }
        return false;
    }

    // ---- starting a tow

    /// <summary>The Seamoth's quick slot with the hook was switched on or off.</summary>
    public static void OnSeamothToggle(SeaMoth seaMoth, int slot, bool active)
    {
        PatchProbe.Fired("SeaMoth.OnUpgradeModuleToggle");
        if (!seaMoth.TryGetNitroxId(out NitroxId towerId))
        {
            return;
        }

        if (!active)
        {
            // Only the slot that hooked lets go: a second hook in another slot that is switched off changes nothing
            if (towedByTower.TryGetValue(towerId, out NitroxId towedId) && byTowed.TryGetValue(towedId, out Tow tow) && tow.Local && tow.Slot == slot)
            {
                Release(tow, seaMoth.GetPilotingMode() ? TowReleaseReason.Hand : TowReleaseReason.PilotExit);
            }
            else if (attaching.TryGetValue(towerId, out Attempt attempt) && attempt.Slot == slot)
            {
                attaching.Remove(towerId);
            }
            return;
        }

        if (!Active || !HasSeamothHook(seaMoth) || !seaMoth.GetPilotingMode())
        {
            Say(Active ? "Tow hook: only the pilot can use it." : "Tow hook: not available on this server.");
            Untoggle(seaMoth, slot);
            return;
        }
        BeginAttach(seaMoth, towerId, seaMoth.transform, seaMoth.transform.forward, slot);
    }

    // ---- the button of a Cyclops

    /// <summary>The tow hook button of a Cyclops is shown: the vehicle upgrades are on and the Cyclops has the hook.</summary>
    public static bool IsButtonAvailable(SubRoot cyclops)
    {
        return VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && HasCyclopsHook(cyclops);
    }

    /// <summary>
    /// The local player pressed the tow hook button on the lighting panel of a Cyclops. If this client simulates the Cyclops it hooks or lets go itself; otherwise the press goes through the server to the
    /// client that does, and if nobody simulates the Cyclops this player takes it over (see <see cref="OnPressFailed" />).
    /// </summary>
    public static void OnCyclopsButton(SubRoot cyclops)
    {
        if (!cyclops || !cyclops.TryGetNitroxId(out NitroxId towerId))
        {
            return;
        }
        if (!Active || !HasCyclopsHook(cyclops))
        {
            Say(TowRules.FailureText(CyclopsTowFailure.NotAvailable));
            return;
        }

        if (Ownership.HasAnyLockType(towerId))
        {
            PressAsSimulator(cyclops, towerId, default);
            return;
        }
        pressesSent[towerId] = Time.time;
        Send(CyclopsTowPress.Press(towerId));
    }

    /// <summary>The server passed on a press of another player (or of this one, if the lock moved meanwhile): this client simulates the Cyclops and does what a press at the helm would do.</summary>
    public static void OnPressForwarded(CyclopsTowPress press)
    {
        if (NitroxEntity.TryGetComponentFrom(press.CyclopsId, out SubRoot cyclops) && cyclops && Ownership.HasAnyLockType(press.CyclopsId))
        {
            PressAsSimulator(cyclops, press.CyclopsId, press.Presser);
            return;
        }
        // The lock moved while the press was on its way
        Send(CyclopsTowPress.Failed(press.CyclopsId, press.Presser, CyclopsTowFailure.NoSimulator));
    }

    /// <summary>The press of this player did nothing, and why.</summary>
    public static void OnPressFailed(CyclopsTowPress press)
    {
        bool mine = pressesSent.TryGetValue(press.CyclopsId, out float sentAt) && Time.time - sentAt < TowRules.LOCK_TIMEOUT_SECONDS * 2f;
        pressesSent.Remove(press.CyclopsId);
        if (press.Reason == CyclopsTowFailure.NoSimulator && mine && Active && NitroxEntity.TryGetComponentFrom(press.CyclopsId, out SubRoot cyclops) && cyclops)
        {
            // Nobody simulates the Cyclops: this player takes it over with a transient lock (which anybody can take from them) and does the hooking
            Ownership.RequestSimulationLock(new LockRequest<PressContext>(press.CyclopsId, SimulationLockType.TRANSIENT, OnPressLock, new PressContext(cyclops)));
            return;
        }
        Say(TowRules.FailureText(press.Reason));
    }

    private static void OnPressLock(NitroxId cyclopsId, bool acquired, PressContext context)
    {
        if (acquired && context.Cyclops)
        {
            PressAsSimulator(context.Cyclops, cyclopsId, default);
            return;
        }
        Say(TowRules.FailureText(CyclopsTowFailure.NoSimulator));
    }

    /// <summary>The button was pressed and this client simulates the Cyclops: lets go of the vehicle it tows, or hooks the nearest one in reach. <paramref name="presser" /> hears why it failed.</summary>
    private static void PressAsSimulator(SubRoot cyclops, NitroxId towerId, SessionId presser)
    {
        if (!Active || !HasCyclopsHook(cyclops))
        {
            Reject(towerId, presser, CyclopsTowFailure.NotAvailable);
            return;
        }
        if (towedByTower.TryGetValue(towerId, out NitroxId towedId) && byTowed.TryGetValue(towedId, out Tow tow))
        {
            if (tow.Local)
            {
                Release(tow, TowReleaseReason.Hand);
            }
            else
            {
                // A tow of this Cyclops that another client pulls is left over from before the helm changed hands: the server ends it, the next press hooks
                Reject(towerId, presser, CyclopsTowFailure.Interrupted);
            }
            return;
        }
        Vector3 forward = cyclops.subAxis ? cyclops.subAxis.forward : cyclops.transform.forward;
        BeginAttach(cyclops, towerId, cyclops.transform, forward, -1, presser);
    }

#if DEBUG
    /// <summary>
    /// For the live check, which cannot aim at a world-space button: presses the tow hook button of the nearest Cyclops that has the hook, as a click on it would (the same path, whoever simulates the
    /// Cyclops). Does nothing in a build without Debug.
    /// </summary>
    public static void DebugCyclopsButton()
    {
        SubRoot nearest = null;
        float best = float.MaxValue;
        foreach (SubRoot cyclops in UnityEngine.Object.FindObjectsOfType<SubRoot>())
        {
            float distance = Player.main ? Vector3.Distance(Player.main.transform.position, cyclops.transform.position) : 0f;
            if (HasCyclopsHook(cyclops) && distance < best)
            {
                nearest = cyclops;
                best = distance;
            }
        }
        if (!nearest)
        {
            Log.Warn("[VehicleUpgrades] tow: no Cyclops with a tow hook");
            return;
        }
        OnCyclopsButton(nearest);
    }
#endif

    /// <summary>A hooking did not happen: the player who pressed hears why (this client's player at once, another player by way of the server).</summary>
    private static void Reject(NitroxId towerId, SessionId presser, CyclopsTowFailure reason, string vehicleName = null)
    {
        if (presser.IsPlayer && presser != NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId)
        {
            Send(CyclopsTowPress.Failed(towerId, presser, reason));
            return;
        }
        Say(TowRules.FailureText(reason, vehicleName));
    }

    private static void Send<T>(T packet) where T : Packet
    {
        NitroxServiceLocator.LocateService<IPacketSender>().Send(packet);
    }

    private static void BeginAttach(Component tower, NitroxId towerId, Transform towerRoot, Vector3 towerForward, int slot, SessionId presser = default)
    {
        Subscribe();
        if (towedByTower.ContainsKey(towerId) || attaching.ContainsKey(towerId))
        {
            if (slot >= 0)
            {
                // A second hook in another slot: one at a time
                Say("Tow hook: one hook at a time.");
                Untoggle(tower, slot);
            }
            return;
        }

        if (!TryChooseTarget(tower, towerRoot, towerForward, out Vehicle target, out NitroxId targetId))
        {
            Reject(towerId, presser, CyclopsTowFailure.NothingInReach);
            Untoggle(tower, slot);
            return;
        }

        int token = ++lastToken;
        attaching[towerId] = new Attempt(token, slot);
        LockRequest<TowContext> request = new(targetId, SimulationLockType.EXCLUSIVE, OnLockAnswer, new TowContext(towerId, targetId, tower, slot, presser, token));
        NitroxServiceLocator.LocateService<SimulationOwnership>().RequestSimulationLock(request);
        Log.Info($"[VehicleUpgrades] tow: lock request for {targetId} ({target.GetName()}) by {towerId}");
        CoroutineHost.StartCoroutine(WatchLock(towerId, token, presser, tower, slot));
    }

    private static IEnumerator WatchLock(NitroxId towerId, int token, SessionId presser, Component tower, int slot)
    {
        yield return new WaitForSeconds(TowRules.LOCK_TIMEOUT_SECONDS);
        // Only the attempt this timer was started for: a later hooking of the same tower is not its business
        if (attaching.TryGetValue(towerId, out Attempt attempt) && attempt.Token == token)
        {
            attaching.Remove(towerId);
            Reject(towerId, presser, CyclopsTowFailure.Interrupted);
            Untoggle(tower, slot);
            Log.Warn($"[VehicleUpgrades] tow: lock request by {towerId} timed out");
        }
    }

    private static void OnLockAnswer(NitroxId targetId, bool acquired, TowContext context)
    {
        if (!attaching.TryGetValue(context.TowerId, out Attempt attempt) || attempt.Token != context.Token)
        {
            // The request timed out or was cancelled: if the lock came all the same, give it back (a transient one is what a parked vehicle has)
            if (acquired)
            {
                Ownership.RequestSimulationLock(targetId, SimulationLockType.TRANSIENT);
            }
            return;
        }

        if (!acquired)
        {
            attaching.Remove(context.TowerId);
            string name = NitroxEntity.TryGetComponentFrom(targetId, out Vehicle other) && other ? other.GetName() : null;
            Reject(context.TowerId, context.Presser, CyclopsTowFailure.InUse, name);
            Log.Info($"[VehicleUpgrades] tow: lock of {targetId} refused");
            Untoggle(context.Tower, context.Slot);
            return;
        }

        Log.Info($"[VehicleUpgrades] tow: lock of {targetId} granted");
        CoroutineHost.StartCoroutine(FinishAttach(context));
    }

    private static IEnumerator FinishAttach(TowContext context)
    {
        // The grant destroyed the vehicle's movement replicator, which goes at the end of the frame: pulling the vehicle now would fight the replicator's last write
        yield return null;

        // A hooking that was cancelled in the frame after the lock came (the pilot got out, the slot was switched off) gives the lock back
        if (!attaching.TryGetValue(context.TowerId, out Attempt attempt) || attempt.Token != context.Token)
        {
            Log.Info($"[VehicleUpgrades] tow: cancelled after the lock came, giving it back");
            Ownership.RequestSimulationLock(context.TowedId, SimulationLockType.TRANSIENT);
            yield break;
        }
        attaching.Remove(context.TowerId);
        Vehicle towed = NitroxEntity.TryGetComponentFrom(context.TowedId, out Vehicle found) ? found : null;
        bool valid = Active && context.Tower && towed && !towed.docked && !towed.GetPilotingMode() && Ownership.HasAnyLockType(context.TowerId) && Ownership.HasAnyLockType(context.TowedId) && !byTowed.ContainsKey(context.TowedId) &&
                     (context.Slot >= 0 ? HasSeamothHook((Vehicle)context.Tower) && ((Vehicle)context.Tower).GetPilotingMode() && ((Vehicle)context.Tower).IsToggled(context.Slot) : HasCyclopsHook((SubRoot)context.Tower));
        if (!valid)
        {
            Log.Info($"[VehicleUpgrades] tow: not valid any more after the lock, giving it back");
            Ownership.RequestSimulationLock(context.TowedId, SimulationLockType.TRANSIENT);
            Reject(context.TowerId, context.Presser, CyclopsTowFailure.Interrupted);
            Untoggle(context.Tower, context.Slot);
            yield break;
        }

        // Exclusive made the vehicle "in use" for everybody: now anybody who gets in takes the lock from this client, which is what ends the tow
        Ownership.RequestSimulationLock(context.TowedId, SimulationLockType.TRANSIENT);

        SessionId session = NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
        Tow tow = new() { TowerId = context.TowerId, TowedId = context.TowedId, Session = session, Local = true, Slot = context.Slot, StartedAt = Time.time };
        Add(tow);
        tow.Line = towed.gameObject.AddComponent<TowLine>();
        tow.Line.Initialize(tow, context.Tower);
        Send(new VehicleTowChanged(context.TowerId, context.TowedId, true, session));
        Say($"Towing {towed.GetName()}.");
        Log.Info($"[VehicleUpgrades] tow: attached {context.TowedId} to {context.TowerId}");
    }

    private static bool TryChooseTarget(Component tower, Transform towerRoot, Vector3 towerForward, out Vehicle target, out NitroxId targetId)
    {
        target = null;
        targetId = null;
        if (!HullBounds.TryGetAnchor(towerRoot, towerForward, false, out Vector3 anchor))
        {
            anchor = towerRoot.position;
        }

        List<TowCandidate> candidates = [];
        Dictionary<NitroxId, Vehicle> vehicles = new();
        foreach (Vehicle vehicle in UnityEngine.Object.FindObjectsOfType<Vehicle>())
        {
            if (!vehicle.TryGetNitroxId(out NitroxId id))
            {
                continue;
            }
            float distance = HullBounds.TryDistanceTo(vehicle.transform, anchor, out float toHull) ? toHull : Vector3.Distance(anchor, vehicle.transform.position);
            candidates.Add(new TowCandidate(id, vehicle is SeaMoth || vehicle is Exosuit, vehicle.transform == towerRoot, IsPiloted(vehicle), vehicle.docked, !vehicle.liveMixin || vehicle.liveMixin.IsAlive(),
                                            byTowed.ContainsKey(id) || towedByTower.ContainsKey(id), distance));
            vehicles[id] = vehicle;
        }

        if (!TowRules.TryChooseTarget(candidates, TowRules.AttachRange(VehicleUpgradeSettings.TowMaxDistance), out TowCandidate chosen))
        {
            return false;
        }
        targetId = chosen.Id;
        target = vehicles[chosen.Id];
        return true;
    }

    private static bool IsPiloted(Vehicle vehicle)
    {
        if (vehicle.GetPilotingMode())
        {
            return true;
        }
        if (vehicle.TryGetNitroxId(out NitroxId vehicleId) && RiderBars.TryGetRider(vehicleId, out _))
        {
            // Somebody holds on to its rider bar: the rider counts as inside, and would be dragged behind the tower
            return true;
        }
        foreach (RemotePlayer remotePlayer in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
        {
            if (remotePlayer.Vehicle == vehicle)
            {
                return true;
            }
        }
        return false;
    }

    // ---- ending a tow

    /// <summary>Lets go of the tow that this client pulls (if any), whatever the reason.</summary>
    public static void ReleaseLocal(TowReleaseReason reason)
    {
        foreach (Tow tow in new List<Tow>(byTowed.Values))
        {
            if (tow.Local)
            {
                Release(tow, reason);
            }
        }
    }

    /// <summary>Ends a tow on this client; on the towing player's client the server and the others are told.</summary>
    public static void Release(Tow tow, TowReleaseReason reason)
    {
        if (!byTowed.ContainsKey(tow.TowedId))
        {
            return;
        }
        Remove(tow);
        Log.Info($"[VehicleUpgrades] tow: released {tow.TowedId} from {tow.TowerId}: {reason}");

        if (!tow.Local)
        {
            return;
        }
        Send(new VehicleTowChanged(tow.TowerId, tow.TowedId, false, tow.Session));
        Say(Describe(reason));
        if (tow.Slot >= 0 && NitroxEntity.TryGetComponentFrom(tow.TowerId, out SeaMoth seaMoth) && seaMoth)
        {
            seaMoth.ToggleSlot(tow.Slot, false);
        }
    }

    public static void Release(NitroxId towedId, TowReleaseReason reason)
    {
        if (byTowed.TryGetValue(towedId, out Tow tow))
        {
            Release(tow, reason);
        }
    }

    /// <summary>The lock of a vehicle moved to another player: if it is part of a tow this client pulls, the tow is over (somebody got into the towed vehicle, or took the tower over).</summary>
    public static void OnVehicleLockLost(NitroxId id)
    {
        if (byTowed.Count == 0)
        {
            return;
        }
        foreach (Tow tow in new List<Tow>(byTowed.Values))
        {
            if (tow.Local && (tow.TowedId.Equals(id) || tow.TowerId.Equals(id)))
            {
                Release(tow, tow.TowedId.Equals(id) ? TowReleaseReason.Entered : TowReleaseReason.LockLost);
            }
        }
    }

    /// <summary>A pilot got into a vehicle on this client (the tower's own player included, who keeps the lock): the tow of that vehicle is over.</summary>
    public static void OnPilotEntered(Vehicle vehicle)
    {
        if (byTowed.Count > 0 && vehicle.TryGetNitroxId(out NitroxId id) && byTowed.TryGetValue(id, out Tow tow) && tow.Local)
        {
            Release(tow, TowReleaseReason.Entered);
        }
    }

    /// <summary>A vehicle began to dock or is docked: the tow it is part of is over.</summary>
    public static void OnDocking(Vehicle vehicle)
    {
        if (byTowed.Count > 0 && vehicle.TryGetNitroxId(out NitroxId id))
        {
            ReleaseWhere(id, TowReleaseReason.Docked);
        }
    }

    public static void OnVehicleKilled(Vehicle vehicle)
    {
        if (byTowed.Count > 0 && vehicle.TryGetNitroxId(out NitroxId id))
        {
            ReleaseWhere(id, TowReleaseReason.Destroyed);
        }
    }

    /// <summary>The modules of a Seamoth or Cyclops changed: a tow without its hook is over.</summary>
    public static void OnModulesChanged(Component tower)
    {
        if (byTowed.Count == 0 || !tower.TryGetNitroxId(out NitroxId id) || !towedByTower.TryGetValue(id, out NitroxId towedId) || !byTowed.TryGetValue(towedId, out Tow tow) || !tow.Local)
        {
            return;
        }
        bool installed = tower is Vehicle seaMoth ? HasSeamothHook(seaMoth) : tower is SubRoot cyclops && HasCyclopsHook(cyclops);
        if (!installed)
        {
            Release(tow, TowReleaseReason.ModuleRemoved);
        }
    }

    private static void ReleaseWhere(NitroxId vehicleId, TowReleaseReason reason)
    {
        foreach (Tow tow in new List<Tow>(byTowed.Values))
        {
            if (tow.Local && (tow.TowedId.Equals(vehicleId) || tow.TowerId.Equals(vehicleId)))
            {
                Release(tow, reason);
            }
        }
    }

    // ---- tows of the other players

    /// <summary>The server says a tow was hooked (drawn here) or ended.</summary>
    public static void OnPacket(VehicleTowChanged packet)
    {
        Subscribe();
        if (!packet.Attached)
        {
            if (byTowed.TryGetValue(packet.TowedId, out Tow tow) && tow.TowerId.Equals(packet.TowerId))
            {
                // The server ended a tow of this client (the player on another client got into the towed vehicle, or the server refused it): this client lets go without telling anybody again
                Remove(tow);
                if (tow.Local)
                {
                    Say(Describe(TowReleaseReason.Remote));
                    if (tow.Slot >= 0 && NitroxEntity.TryGetComponentFrom(tow.TowerId, out SeaMoth seaMoth) && seaMoth)
                    {
                        seaMoth.ToggleSlot(tow.Slot, false);
                    }
                }
            }
            return;
        }

        if (byTowed.TryGetValue(packet.TowedId, out Tow existing))
        {
            if (existing.Local)
            {
                // Another player hooked a vehicle that this client pulls: the server arbitrated, this client lost
                Release(existing, TowReleaseReason.LockLost);
            }
            else
            {
                Remove(existing);
            }
        }
        Add(new Tow { TowerId = packet.TowerId, TowedId = packet.TowedId, Session = packet.SessionId, Local = false, StartedAt = Time.time });
    }

    // ---- bookkeeping

    private static void Add(Tow tow)
    {
        Subscribe();
        byTowed[tow.TowedId] = tow;
        towedByTower[tow.TowerId] = tow.TowedId;
        tow.Cable = TowCable.Create(tow);
    }

    private static void Remove(Tow tow)
    {
        byTowed.Remove(tow.TowedId);
        if (towedByTower.TryGetValue(tow.TowerId, out NitroxId towed) && towed.Equals(tow.TowedId))
        {
            towedByTower.Remove(tow.TowerId);
        }
        if (tow.Line)
        {
            UnityEngine.Object.Destroy(tow.Line);
        }
        if (tow.Cable)
        {
            UnityEngine.Object.Destroy(tow.Cable.gameObject);
        }
    }

    /// <summary>
    /// Switches the Seamoth's slot off again after a hooking that did not happen. One frame later: the game announces a toggle to the quick slots <b>after</b> its callbacks ran, with the state it had before them,
    /// so a slot that is switched off from inside the callback is drawn lit again.
    /// </summary>
    private static void Untoggle(Component tower, int slot)
    {
        if (slot >= 0 && tower is Vehicle vehicle && vehicle)
        {
            CoroutineHost.StartCoroutine(UntoggleNextFrame(vehicle, slot));
        }
    }

    private static IEnumerator UntoggleNextFrame(Vehicle vehicle, int slot)
    {
        yield return null;
        if (vehicle && vehicle.IsToggled(slot) && !IsTowing(vehicle, slot))
        {
            vehicle.ToggleSlot(slot, false);
        }
    }

    /// <summary>This Seamoth tows with the hook in this slot.</summary>
    private static bool IsTowing(Vehicle vehicle, int slot)
    {
        return vehicle.TryGetNitroxId(out NitroxId id) && towedByTower.TryGetValue(id, out NitroxId towedId) && byTowed.TryGetValue(towedId, out Tow tow) && tow.Slot == slot;
    }

    private static void Say(string text)
    {
        Log.InGame(text);
    }

    private static string Describe(TowReleaseReason reason)
    {
        return reason switch
        {
            TowReleaseReason.Hand => "Tow hook: let go.",
            TowReleaseReason.LockLost => "Tow hook: the tow is over, another player took over a vehicle.",
            TowReleaseReason.Entered => "Tow hook: somebody got into the towed vehicle.",
            TowReleaseReason.Docked => "Tow hook: let go, a vehicle is docking.",
            TowReleaseReason.Destroyed => "Tow hook: a vehicle was destroyed.",
            TowReleaseReason.TooLong => "Tow hook: the cable was too long, let go.",
            TowReleaseReason.Frozen => "Tow hook: the towed vehicle is held still, let go.",
            TowReleaseReason.PilotExit => "Tow hook: let go, the pilot got out.",
            TowReleaseReason.ModuleRemoved => "Tow hook: the hook was taken out.",
            TowReleaseReason.Disabled => "Tow hook: switched off on this server.",
            TowReleaseReason.Remote => "Tow hook: the server ended the tow.",
            _ => "Tow hook: let go."
        };
    }
}
