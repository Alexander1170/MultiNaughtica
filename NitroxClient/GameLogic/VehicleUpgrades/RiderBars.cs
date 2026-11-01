using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Extensions;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The Seamoth Rider Bar in the game (docs/coop/VEHICLE_UPGRADES.md). A bar on the hull of a Seamoth that has the module; a swimming player looks at it and uses it to hold on (one rider per Seamoth,
/// the server decides), and rides along. The rider <b>counts as inside</b> the Seamoth: the game's own locked mode (the one a pilot is in) is used, so that no oxygen is used, the water's heat does
/// not hurt, nothing can push or touch the player, tools are put away, and the player cannot steer (the locked mode steers only when its parent is the pilot's seat, and the bar's anchor is not).
/// The rider's body is a <b>child of the bar</b> on every client, so it moves with the Seamoth exactly, however the Seamoth is drawn there, and needs no pose of its own; only the look is sent
/// (<see cref="SeamothRiderLook" />). The ride ends when the rider lets go, and when the Seamoth docks or is destroyed, the bar is taken out, or the rider's player leaves.
/// </summary>
/// <remarks>
/// The rider is a child of a vehicle that can be destroyed: every path that destroys or docks a Seamoth has to release a local rider first (<see cref="OnVehicleGone" />), because the player object
/// would be destroyed with it. <see cref="LocalRider" /> watches the rest.
/// </remarks>
public static class RiderBars
{
    private static readonly Dictionary<NitroxId, SessionId> ridersByVehicle = new();
    private static bool subscribed;
    private static NitroxId pendingVehicleId;
    private static float pendingSince;
    private static SeamothRiderBar localBar;
    private static bool riding;
    private static NitroxId ridingVehicleId;
    private static float grabbedAt;
    private static float yaw;
    private static float pitch;
    private static float lastLookSent;
    private static Quaternion lastLookValue = Quaternion.identity;

    private static TechType BarTech => (TechType)CoopItems.SEAMOTH_RIDER_BAR;

    private static bool Active => VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted;

    /// <summary>
    /// True while the local player holds on to a bar. A flag of its own, not the bar: when the bar is destroyed under the rider the player still hangs in the locked mode, and has to be let go
    /// (<see cref="LocalRider" />).
    /// </summary>
    public static bool IsLocalRiding => riding;

    public static SeamothRiderBar LocalBar => localBar;
    public static float GrabbedAt => grabbedAt;

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

    /// <summary>The session ended: no ride goes on into the next one. The scene is going, so the player is not touched.</summary>
    public static void Reset()
    {
        ridersByVehicle.Clear();
        localBar = null;
        riding = false;
        ridingVehicleId = null;
        pendingVehicleId = null;
    }

    private static void OnSettingsChanged()
    {
        if (!VehicleUpgradeSettings.Enabled)
        {
            ReleaseLocal(RiderReleaseReason.ModuleRemoved, true);
        }
    }

    public static bool IsBarInstalled(Vehicle vehicle)
    {
        return VehicleUpgradeSettings.Enabled && vehicle && vehicle.modules != null && vehicle.modules.GetCount(BarTech) > 0;
    }

    public static bool TryGetRider(NitroxId vehicleId, out SessionId session)
    {
        session = default;
        return vehicleId != null && ridersByVehicle.TryGetValue(vehicleId, out session);
    }

    private static SessionId LocalSession => NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;

    // ---- the hand target

    /// <summary>What the bar says to the local player who looks at it.</summary>
    public static RiderGrabDecision DecisionFor(SeamothRiderBar bar)
    {
        Player player = Player.main;
        if (!player || !bar || !bar.Vehicle)
        {
            return RiderGrabDecision.No;
        }
        bool someoneElse = TryGetRider(bar.VehicleId, out SessionId rider) && rider != LocalSession;
        bool pending = pendingVehicleId != null && !RiderBarRules.RequestTimedOut(Time.time - pendingSince);
        return RiderBarRules.CanGrab(new RiderGrabContext(Active, IsBarInstalled(bar.Vehicle), !bar.Vehicle.liveMixin || bar.Vehicle.liveMixin.IsAlive(), bar.Vehicle.docked, player.GetMode() == Player.Mode.Normal,
                                                          player.IsInSub() || player.GetVehicle() || player.IsInSubmarine(), !player.liveMixin || player.liveMixin.IsAlive(), IsLocalRiding, pending, someoneElse));
    }

    /// <summary>The player used the bar: ask the server for it.</summary>
    public static void RequestHold(SeamothRiderBar bar)
    {
        Subscribe();
        if (DecisionFor(bar) != RiderGrabDecision.Yes || bar.VehicleId == null)
        {
            return;
        }
        Log.Info($"[VehicleUpgrades] rider bar: asking to hold on to {bar.VehicleId}");
        pendingVehicleId = bar.VehicleId;
        pendingSince = Time.time;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new SeamothRiderChanged(RiderBarMessage.Hold, bar.VehicleId, LocalSession));
    }

    // ---- the server's word

    public static void OnPacket(SeamothRiderChanged packet)
    {
        Subscribe();
        switch (packet.Message)
        {
            case RiderBarMessage.Holding:
                ridersByVehicle[packet.VehicleId] = packet.SessionId;
                if (packet.SessionId == LocalSession)
                {
                    OnGranted(packet.VehicleId);
                }
                break;
            case RiderBarMessage.Released:
                if (ridersByVehicle.TryGetValue(packet.VehicleId, out SessionId rider) && rider == packet.SessionId)
                {
                    ridersByVehicle.Remove(packet.VehicleId);
                }
                if (packet.SessionId == LocalSession && IsLocalRiding && packet.VehicleId.Equals(ridingVehicleId))
                {
                    ReleaseLocal(RiderReleaseReason.Server, false);
                }
                break;
            case RiderBarMessage.Denied:
                if (pendingVehicleId != null && pendingVehicleId.Equals(packet.VehicleId))
                {
                    pendingVehicleId = null;
                    // The server also denies a pilot, a player in a base and a server with the vehicle upgrades off: it only says somebody else holds on when that is true
                    Log.InGame(TryGetRider(packet.VehicleId, out SessionId other) && other != LocalSession ? "Rider bar: somebody holds on already." : "Rider bar: you cannot hold on right now.");
                }
                break;
        }
        // The bars of this client put the bodies of the other riders on and off
        if (NitroxEntity.TryGetComponentFrom(packet.VehicleId, out SeamothRiderBar bar) && bar)
        {
            bar.Refresh();
        }
    }

    private static void OnGranted(NitroxId vehicleId)
    {
        if (IsLocalRiding && vehicleId.Equals(ridingVehicleId))
        {
            // A second "holding" for the ride that is on (the server answers every request, and an impatient player asked twice): nothing to do, and certainly not to let go
            pendingVehicleId = null;
            return;
        }

        bool wanted = pendingVehicleId != null && pendingVehicleId.Equals(vehicleId);
        float age = Time.time - pendingSince;
        pendingVehicleId = null;
        if (!wanted || !NitroxEntity.TryGetComponentFrom(vehicleId, out SeamothRiderBar bar) || !bar || !bar.IsBuilt || !StillWanted(bar, age))
        {
            // The player moved on while the server was answering: give the bar back
            NitroxServiceLocator.LocateService<IPacketSender>().Send(new SeamothRiderChanged(RiderBarMessage.LetGo, vehicleId, LocalSession));
            return;
        }
        Enter(bar);
    }

    /// <summary>The grant comes after the request: the player is still a swimmer near the bar, alive and free to move, and the request is not stale.</summary>
    private static bool StillWanted(SeamothRiderBar bar, float secondsSinceRequest)
    {
        Player player = Player.main;
        return player && player.GetMode() == Player.Mode.Normal && !player.IsInSub() && !player.GetVehicle() && !player.cinematicModeActive && (!player.liveMixin || player.liveMixin.IsAlive()) &&
               bar.Vehicle && !bar.Vehicle.docked && IsBarInstalled(bar.Vehicle) && bar.Anchor &&
               RiderBarRules.GrantStillWanted(secondsSinceRequest, Vector3.Distance(player.transform.position, bar.Anchor.position));
    }

    // ---- the local rider

    private static void Enter(SeamothRiderBar bar)
    {
        Player player = Player.main;
        Subscribe();
        localBar = bar;
        riding = true;
        ridingVehicleId = bar.VehicleId;
        grabbedAt = Time.time;
        yaw = 0f;
        pitch = 0f;
        lastLookSent = 0f;
        lastLookValue = Quaternion.identity;

        // The locked mode of the game: a pilot's mode, but with a parent that is not the pilot's seat, so nothing steers. The camera is reset, the controller and the collider are switched off.
        player.EnterLockedMode(bar.Anchor, true);
        player.armsController.SetWorldIKTarget(bar.LeftHand, bar.RightHand);
        player.gameObject.EnsureComponent<LocalRider>();
        Log.Info($"[VehicleUpgrades] rider bar: holding on to {bar.VehicleId}");
    }

    /// <summary>
    /// Lets the local player go: puts them beside the hull (not for a teleport, which puts them elsewhere), takes them out of the locked mode (which unparents them) and tells the server. Safe to call
    /// when the player does not ride and when the bar is gone already; does not touch the player when the scene is going away. Whatever fails on the way, the player leaves the Seamoth's hierarchy.
    /// </summary>
    public static void ReleaseLocal(RiderReleaseReason reason, bool tellServer, bool placeBesideHull = true)
    {
        if (!IsLocalRiding)
        {
            return;
        }
        SeamothRiderBar bar = localBar;
        NitroxId vehicleId = ridingVehicleId;
        riding = false;
        ridingVehicleId = null;
        localBar = null;

        Player player = Player.main;
        if (player)
        {
            try
            {
                RestorePlayer(player, bar, placeBesideHull);
            }
            catch (System.Exception exception)
            {
                Log.Error(exception, "[VehicleUpgrades] rider bar: could not put the player back into the normal mode, taking them out of the Seamoth's hierarchy");
                if (player.transform.parent)
                {
                    player.transform.parent = null;
                }
            }
        }
        Log.Info($"[VehicleUpgrades] rider bar: let go of {vehicleId}: {reason}");
        if (reason != RiderReleaseReason.Hand && reason != RiderReleaseReason.Server && reason != RiderReleaseReason.SessionEnded)
        {
            Log.InGame(Describe(reason));
        }
        if (tellServer && vehicleId != null)
        {
            NitroxServiceLocator.LocateService<IPacketSender>().Send(new SeamothRiderChanged(RiderBarMessage.LetGo, vehicleId, LocalSession));
        }
    }

    /// <summary>Metres behind the anchor at which a rider is put when the search for a free spot beside the hull ended inside it.</summary>
    private const float BEHIND_THE_HULL = 1.2f;

    /// <summary>A spot closer to the hull box than this is inside the hull.</summary>
    private const float INSIDE_THE_HULL = 0.2f;

    private static void RestorePlayer(Player player, SeamothRiderBar bar, bool placeBesideHull)
    {
        LocalRider rider = player.GetComponent<LocalRider>();
        if (rider)
        {
            UnityEngine.Object.Destroy(rider);
        }
        player.armsController.SetWorldIKTarget(null, null);
        if (player.GetMode() == Player.Mode.LockedPiloting)
        {
            // Beside the hull if there is room (the game's own search for a free spot around the vehicle), else where the rider hangs; either way the player leaves the Seamoth's hierarchy first
            bool beside = placeBesideHull && bar && bar.Vehicle;
            if (beside)
            {
                player.SpawnNearby(2.5f, bar.Vehicle.gameObject);
            }
            if (!player.ToNormalMode(false))
            {
                player.transform.parent = null;
            }
            if (beside && bar.Anchor && HullBounds.TryDistanceTo(bar.Vehicle.transform, player.transform.position, out float distance) && distance < INSIDE_THE_HULL)
            {
                // The game's search skips the Seamoth's own colliders and may have found a spot in the hull: behind the rod is always outside it
                player.SetPosition(bar.Anchor.position - bar.Vehicle.transform.forward * BEHIND_THE_HULL);
            }
        }
        else if (player.transform.parent && bar && player.transform.IsChildOf(bar.transform))
        {
            player.transform.parent = null;
        }
    }

    /// <summary>A Seamoth is about to dock or to be destroyed: a rider of it lets go before it goes.</summary>
    public static void OnVehicleGone(Vehicle vehicle, RiderReleaseReason reason)
    {
        if (IsLocalRiding && vehicle && ((localBar && localBar.Vehicle == vehicle) || (vehicle.TryGetNitroxId(out NitroxId id) && id.Equals(ridingVehicleId))))
        {
            ReleaseLocal(reason, true);
        }
    }

    /// <summary>The modules of a Seamoth changed: its bar is built or taken down, and a rider of a bar that is gone lets go (first: taking the bar down takes the rider's anchor with it).</summary>
    public static void OnModulesChanged(Vehicle vehicle)
    {
        if (!vehicle)
        {
            return;
        }
        if (IsLocalRiding && localBar && localBar.Vehicle == vehicle && !IsBarInstalled(vehicle))
        {
            ReleaseLocal(RiderReleaseReason.ModuleRemoved, true);
        }
        SeamothRiderBar bar = vehicle.GetComponent<SeamothRiderBar>();
        if (bar)
        {
            bar.Refresh();
        }
    }

    // ---- while riding

    /// <summary>
    /// Free look for the rider: the game's locked mode of a vehicle turns the mouse look off (a pilot steers with the mouse), so the camera's turn is written after the game's own camera update. Only
    /// the camera is turned, relative to the bar, so it turns with the Seamoth; the limits are in <see cref="RiderBarRules" />.
    /// </summary>
    public static void ApplyFreeLook(MainCameraControl camera)
    {
        if (!IsLocalRiding || !camera.cameraOffsetTransform)
        {
            return;
        }
        Player player = Player.main;
        if (player && AvatarInputHandler.main && AvatarInputHandler.main.IsEnabled())
        {
            if (player.GetPDA().isInUse)
            {
                // The PDA is laid out for a camera that looks straight ahead: the view returns to the front while it is open, as the game's own camera does
                yaw = Mathf.MoveTowards(yaw, 0f, RiderBarRules.RECENTRE_DEGREES_PER_SECOND * Time.deltaTime);
                pitch = Mathf.MoveTowards(pitch, 0f, RiderBarRules.RECENTRE_DEGREES_PER_SECOND * Time.deltaTime);
            }
            else
            {
                Vector2 delta = GameInput.GetLookDelta();
                yaw = RiderBarRules.ClampYaw(yaw + delta.x);
                pitch = RiderBarRules.ClampPitch(pitch + delta.y);
            }
        }
        // The shake of impacts and grabs is the game's own, added to the turn like it does
        camera.cameraOffsetTransform.localEulerAngles = new Vector3(-pitch, yaw + camera.camShake, 0f);
    }

    /// <summary>Sends the look of the local rider to the others, relative to the bar, when it changed (<see cref="RiderBarRules.ShouldSendLook" />).</summary>
    public static void SendLookIfNeeded()
    {
        if (!IsLocalRiding || !localBar || !localBar.Anchor)
        {
            return;
        }
        Player player = Player.main;
        if (!player || !player.camRoot)
        {
            return;
        }

        Quaternion relative = Quaternion.Inverse(localBar.Anchor.rotation) * player.camRoot.GetAimingTransform().rotation;
        float since = Time.time - lastLookSent;
        if (!RiderBarRules.ShouldSendLook(since, Quaternion.Angle(lastLookValue, relative)))
        {
            return;
        }
        lastLookSent = Time.time;
        lastLookValue = relative;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new SeamothRiderLook(LocalSession, localBar.VehicleId, relative.ToDto(), MovementClock.Now(NitroxServiceLocator.LocateService<TimeManager>().RealTimeElapsed)));
    }

    /// <summary>The look of a remote rider arrived: the head of the copy turns to it (in the space of the bar).</summary>
    public static void OnLook(SeamothRiderLook packet)
    {
        if (!NitroxServiceLocator.LocateService<PlayerManager>().TryFind(packet.SessionId, out RemotePlayer remote) || !remote.RiderBar || !remote.RiderBar.Anchor || !remote.AnimationController)
        {
            return;
        }
        Quaternion look = packet.Look.ToUnity();
        if (!RiderBarRules.IsRotation(look.x, look.y, look.z, look.w))
        {
            return;
        }
        remote.AnimationController.AimingRotation = remote.RiderBar.Anchor.rotation * look;
    }

    private static string Describe(RiderReleaseReason reason)
    {
        return reason switch
        {
            RiderReleaseReason.Docked => "Rider bar: let go, the Seamoth is docking.",
            RiderReleaseReason.Destroyed => "Rider bar: let go, the Seamoth was destroyed.",
            RiderReleaseReason.ModuleRemoved => "Rider bar: let go, the bar is gone.",
            RiderReleaseReason.ModeChanged => "Rider bar: let go.",
            RiderReleaseReason.Teleported => "Rider bar: let go, you were moved.",
            _ => "Rider bar: let go."
        };
    }
}
