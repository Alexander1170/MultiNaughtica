using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>Why a ride ended (the player is told only when it was not their own doing; the log has the name).</summary>
public enum RiderReleaseReason
{
    None,

    /// <summary>The rider let go: the use key or the exit key.</summary>
    Hand,

    /// <summary>The Seamoth began to dock or is docked.</summary>
    Docked,

    /// <summary>The Seamoth was destroyed or is gone.</summary>
    Destroyed,

    /// <summary>The bar was taken out of its slot, or the vehicle upgrades were switched off.</summary>
    ModuleRemoved,

    /// <summary>The server ended the ride (the Seamoth docked on another client, the server restarted the books).</summary>
    Server,

    /// <summary>Something else took the player out of the locked mode (death, a teleport, a console command): the ride is over without anyone letting go.</summary>
    ModeChanged,

    /// <summary>The player was teleported (an admin, the console): the ride is over and the player goes where they were sent, not beside the hull.</summary>
    Teleported,

    /// <summary>The session ended.</summary>
    SessionEnded
}

/// <summary>What the bar's hand target says to the player who looks at it.</summary>
public enum RiderGrabDecision
{
    /// <summary>The player can hold on.</summary>
    Yes,

    /// <summary>Nothing is shown: the bar is not usable for this player right now (no bar installed, docked, the player is busy).</summary>
    No,

    /// <summary>Somebody holds on already.</summary>
    Occupied
}

/// <summary>What the game says about the player and the Seamoth when the player looks at the bar: plain values, so that the decision can be tested.</summary>
public readonly struct RiderGrabContext
{
    public RiderGrabContext(bool enabled, bool barInstalled, bool vehicleAlive, bool vehicleDocked, bool playerNormalMode, bool playerInSubOrVehicle, bool playerAlive, bool playerRiding, bool requestPending, bool someoneElseRides)
    {
        Enabled = enabled;
        BarInstalled = barInstalled;
        VehicleAlive = vehicleAlive;
        VehicleDocked = vehicleDocked;
        PlayerNormalMode = playerNormalMode;
        PlayerInSubOrVehicle = playerInSubOrVehicle;
        PlayerAlive = playerAlive;
        PlayerRiding = playerRiding;
        RequestPending = requestPending;
        SomeoneElseRides = someoneElseRides;
    }

    public bool Enabled { get; }
    public bool BarInstalled { get; }
    public bool VehicleAlive { get; }
    public bool VehicleDocked { get; }

    /// <summary>The player is in the normal mode (not piloting, not sitting, not locked).</summary>
    public bool PlayerNormalMode { get; }

    /// <summary>The player is inside a Cyclops or a base, or sits in a vehicle.</summary>
    public bool PlayerInSubOrVehicle { get; }

    public bool PlayerAlive { get; }

    /// <summary>The player already holds on to a bar.</summary>
    public bool PlayerRiding { get; }

    /// <summary>A request of this player is on its way to the server.</summary>
    public bool RequestPending { get; }

    public bool SomeoneElseRides { get; }
}

/// <summary>
/// The rules of the Seamoth Rider Bar (docs/coop/VEHICLE_UPGRADES.md), free of game types so that they can be tested: when a player can hold on, when they let go, where the bar sits on a hull,
/// how often the look is sent.
/// </summary>
public static class RiderBarRules
{
    /// <summary>Seconds after the grab before the let-go keys count: the key that grabbed is still down in that frame.</summary>
    public const float LET_GO_DEBOUNCE_SECONDS = 0.3f;

    /// <summary>Seconds a request waits for the server's answer before the player may ask again.</summary>
    public const float REQUEST_TIMEOUT_SECONDS = 3f;

    /// <summary>
    /// A grant of the server that comes after this many seconds, or when the player is farther from the bar than this many metres, is not honoured: the player moved on, and the grant is given back.
    /// </summary>
    public const float GRANT_MAX_AGE_SECONDS = 15f;

    public const float GRANT_MAX_DISTANCE = 6f;

    /// <summary>The degrees per second at which the rider's view returns to the front while the PDA is open (the game's own camera recentres the same way).</summary>
    public const float RECENTRE_DEGREES_PER_SECOND = 240f;

    /// <summary>The most often the look of the rider is sent, in seconds, the smallest change (degrees) worth a packet, and the longest silence while nothing changes.</summary>
    public const float LOOK_INTERVAL_SECONDS = 1f / 15f;

    public const float LOOK_MIN_CHANGE_DEGREES = 0.5f;
    public const float LOOK_KEEP_ALIVE_SECONDS = 1f;

    /// <summary>How far the rider can turn the head, in degrees: sideways and up or down (a swimmer cannot look through his own back).</summary>
    public const float MAX_YAW_DEGREES = 140f;

    public const float MAX_PITCH_DEGREES = 80f;

    /// <summary>The player looks at the bar: can they hold on?</summary>
    public static RiderGrabDecision CanGrab(RiderGrabContext context)
    {
        if (!context.Enabled || !context.BarInstalled || !context.VehicleAlive || context.VehicleDocked || !context.PlayerNormalMode || context.PlayerInSubOrVehicle || !context.PlayerAlive || context.PlayerRiding)
        {
            return RiderGrabDecision.No;
        }
        return context.SomeoneElseRides ? RiderGrabDecision.Occupied : context.RequestPending ? RiderGrabDecision.No : RiderGrabDecision.Yes;
    }

    /// <summary>
    /// The rider lets go: the use key or the exit key, a moment after the grab, and not while the PDA is open or the input is taken (chat, console).
    /// </summary>
    public static bool ShouldLetGo(float secondsSinceGrab, bool useKeyPressed, bool exitKeyPressed, bool pdaInUse, bool inputEnabled)
    {
        return secondsSinceGrab >= LET_GO_DEBOUNCE_SECONDS && (useKeyPressed || exitKeyPressed) && !pdaInUse && inputEnabled;
    }

    /// <summary>The server granted the hold: the player still wants it when the request is not too old and the player is still near the bar.</summary>
    public static bool GrantStillWanted(float secondsSinceRequest, float distanceToBar)
    {
        return secondsSinceRequest >= 0f && secondsSinceRequest <= GRANT_MAX_AGE_SECONDS && distanceToBar >= 0f && distanceToBar <= GRANT_MAX_DISTANCE;
    }

    /// <summary>A look is a unit quaternion: NaN, infinity or a zero quaternion would reach the animator of every other client as it is.</summary>
    public static bool IsRotation(float x, float y, float z, float w)
    {
        float lengthSquared = x * x + y * y + z * z + w * w;
        return !float.IsNaN(lengthSquared) && !float.IsInfinity(lengthSquared) && lengthSquared >= 0.9f && lengthSquared <= 1.1f;
    }

    /// <summary>A request that never got an answer may be sent again.</summary>
    public static bool RequestTimedOut(float secondsSinceRequest) => secondsSinceRequest >= REQUEST_TIMEOUT_SECONDS;

    /// <summary>Whether the look is worth a packet now: at most 15 times a second, when it changed by half a degree, and at least once a second as a keep-alive.</summary>
    public static bool ShouldSendLook(float secondsSinceLastSent, float degreesChanged)
    {
        if (secondsSinceLastSent < LOOK_INTERVAL_SECONDS)
        {
            return false;
        }
        return degreesChanged >= LOOK_MIN_CHANGE_DEGREES || secondsSinceLastSent >= LOOK_KEEP_ALIVE_SECONDS;
    }

    /// <summary>The look of the rider after a mouse move: the angle plus the move, kept inside the limits.</summary>
    public static float ClampYaw(float degrees) => Math.Max(-MAX_YAW_DEGREES, Math.Min(MAX_YAW_DEGREES, degrees));

    public static float ClampPitch(float degrees) => Math.Max(-MAX_PITCH_DEGREES, Math.Min(MAX_PITCH_DEGREES, degrees));
}

/// <summary>
/// Where the bar sits on the hull and where the rider hangs, from the box around the Seamoth's colliders (<see cref="HullBounds" />), in the space of the Seamoth: a rod across the rear of the
/// hull, in the upper half, and the rider's root a little behind it, with the hands on the rod. The sizes of the prefab cannot be read offline: these are first guesses, and the live check has a
/// command to nudge them (<c>vubar</c>).
/// </summary>
public static class RiderBarLayout
{
    /// <summary>Metres between the end of the hull and the rod.</summary>
    public const float REAR_GAP = 0.1f;

    /// <summary>The share of the half height of the hull at which the rod sits above the middle.</summary>
    public const float HEIGHT_SHARE = 0.5f;

    /// <summary>Metres behind and below the rod at which the rider's root hangs (the arms reach forward to the rod).</summary>
    public const float RIDER_BEHIND = 0.6f;

    public const float RIDER_BELOW = 0.1f;

    public const float MIN_HALF_LENGTH = 0.3f;
    public const float MAX_HALF_LENGTH = 0.6f;

    /// <summary>The rod is a capsule this many metres across (the game's hand has to be aimed at the trigger around it, which is much bigger).</summary>
    public const float ROD_DIAMETER = 0.06f;

    /// <summary>
    /// The radius of the hand trigger around the rod: 0.6 m across, ten times the rod. (It was 0.4 m; a play-test found the bar fiddly to aim at.) The game's hand looks for the nearest collider on its ray and
    /// the trigger is on the layer that it accepts, so a big trigger is found from anywhere that the ray reaches it before the hull.
    /// </summary>
    public const float GRAB_RADIUS = 0.3f;

    /// <summary>The trigger is this much longer than the rod, the half of it at each end.</summary>
    public const float GRAB_EXTRA_LENGTH = 0.8f;

    /// <summary>
    /// The trigger is moved this far from the rod away from the hull (the hull's rear end is <see cref="REAR_GAP" /> in front of the rod), so that the bigger trigger grows to the outside and does not reach farther
    /// into the hull than the old one did, where the Seamoth's own hand target (getting in) has to go on working.
    /// </summary>
    public const float GRAB_CENTRE_BEHIND = 0.1f;

    /// <summary>The height of the capsule of the hand trigger (its axis is the rod's), for a rod of this half length: the rod and a little more at each end.</summary>
    public static float GrabHeight(float halfLength) => halfLength * 2f + GRAB_EXTRA_LENGTH;

    /// <summary>
    /// How far the trigger reaches into the hull beyond the hull's rear end, in metres (negative: it stops short of the hull). The rod is <see cref="REAR_GAP" /> behind the end, the trigger's middle is
    /// <see cref="GRAB_CENTRE_BEHIND" /> further behind and its radius is <see cref="GRAB_RADIUS" />.
    /// </summary>
    public static float GrabReachIntoHull() => GRAB_RADIUS - GRAB_CENTRE_BEHIND - REAR_GAP;

    /// <summary>The rod's half length: a little over half the half width of the hull, between 0.3 and 0.6 m.</summary>
    public static float RodHalfLength(float halfWidth) => Math.Max(MIN_HALF_LENGTH, Math.Min(MAX_HALF_LENGTH, halfWidth * 0.55f));

    /// <summary>The middle of the rod in the hull's space.</summary>
    public static (float X, float Y, float Z) RodCenter(float cx, float cy, float cz, float ey, float ez)
    {
        return (cx, cy + ey * HEIGHT_SHARE, cz - ez - REAR_GAP);
    }

    /// <summary>The rider's root in the hull's space: behind the rod and a little lower.</summary>
    public static (float X, float Y, float Z) RiderRoot(float cx, float cy, float cz, float ey, float ez)
    {
        (float x, float y, float z) = RodCenter(cx, cy, cz, ey, ez);
        return (x, y - RIDER_BELOW, z - RIDER_BEHIND);
    }
}
