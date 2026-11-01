using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>Why a tow ended (the player is told in words; the log says the name).</summary>
public enum TowReleaseReason
{
    None,

    /// <summary>The player let go: the quick slot or the button on the Cyclops' lighting panel.</summary>
    Hand,

    /// <summary>The lock of the towed vehicle or of the tower moved to another player: somebody got into the towed vehicle, or took the tower over.</summary>
    LockLost,

    /// <summary>Somebody entered the towed vehicle (the tower's own player included).</summary>
    Entered,

    /// <summary>The tower or the towed vehicle began to dock or is docked.</summary>
    Docked,

    /// <summary>The tower or the towed vehicle was destroyed or is gone.</summary>
    Destroyed,

    /// <summary>The cable would be longer than the longest cable of the server (or the towed vehicle was held up for too long).</summary>
    TooLong,

    /// <summary>The Seamoth's pilot got out (the Seamoth does not drag a vehicle around by itself).</summary>
    PilotExit,

    /// <summary>The hook was taken out of its slot.</summary>
    ModuleRemoved,

    /// <summary>The vehicle upgrades were switched off, or the session ended.</summary>
    Disabled,

    /// <summary>The player who tows left, or another client says the tow is over.</summary>
    Remote,

    /// <summary>The towed vehicle is held still by the game for a reason the cable cannot change (a creature grabs it, it is being teleported).</summary>
    Frozen
}

/// <summary>What the tow hooks look at to pick a vehicle: plain values, so that the choice can be tested.</summary>
public readonly struct TowCandidate
{
    public TowCandidate(NitroxId id, bool isSeamothOrPrawn, bool isTower, bool piloted, bool docked, bool alive, bool alreadyTowed, float distance)
    {
        Id = id;
        IsSeamothOrPrawn = isSeamothOrPrawn;
        IsTower = isTower;
        Piloted = piloted;
        Docked = docked;
        Alive = alive;
        AlreadyTowed = alreadyTowed;
        Distance = distance;
    }

    public NitroxId Id { get; }
    public bool IsSeamothOrPrawn { get; }
    public bool IsTower { get; }
    public bool Piloted { get; }
    public bool Docked { get; }
    public bool Alive { get; }
    public bool AlreadyTowed { get; }

    /// <summary>The distance from the tower's cable anchor to the nearest point of the candidate's hull.</summary>
    public float Distance { get; }
}

/// <summary>
/// The rules of the tow hooks (docs/coop/VEHICLE_UPGRADES.md), free of game types so that they can be tested: which vehicle a hook picks, how hard the cable pulls, when it lets go.
/// </summary>
public static class TowRules
{
    /// <summary>The cable is slack up to this fraction of the longest cable (but see <see cref="RestLength" />): a slack cable pulls with no force, so the two hulls are not pushed apart.</summary>
    public const float REST_LENGTH_FRACTION = 0.35f;

    public const float MIN_REST_LENGTH = 5f;
    public const float MAX_REST_LENGTH = 12f;

    /// <summary>The spring of the cable: acceleration (m/s²) per metre of stretch.</summary>
    public const float SPRING = 4f;

    /// <summary>The damper of the cable: acceleration (m/s²) per m/s of speed at which the vehicles move apart.</summary>
    public const float DAMPER = 3f;

    /// <summary>The strongest pull of the cable in m/s² (the Prawn Suit's grappling arm pulls with a constant 15).</summary>
    public const float MAX_ACCELERATION = 25f;

    /// <summary>Seconds the cable may be longer than the longest cable before the tow is over.</summary>
    public const float TOO_LONG_SECONDS = 0.75f;

    /// <summary>A cable that is this many times longer than the longest cable ends the tow at once (a teleport, a respawn, a hitch of the game).</summary>
    public const float SNAP_FACTOR = 1.5f;

    /// <summary>Seconds the tower waits for the lock of the towed vehicle before the hook gives up.</summary>
    public const float LOCK_TIMEOUT_SECONDS = 5f;

    /// <summary>Metres of crush depth margin at which the tower is warned about the towed vehicle, and the seconds between two warnings.</summary>
    public const float DEPTH_WARNING_MARGIN = 15f;

    public const float DEPTH_WARNING_REPEAT_SECONDS = 10f;

    /// <summary>A hook attaches to a vehicle within half of the longest cable (<c>TowMaxDistance</c>): the cable has to be able to stretch.</summary>
    public static float AttachRange(float towMaxDistance) => towMaxDistance * 0.5f;

    /// <summary>The length at which the cable begins to pull.</summary>
    public static float RestLength(float towMaxDistance)
    {
        return Math.Max(MIN_REST_LENGTH, Math.Min(MAX_REST_LENGTH, towMaxDistance * REST_LENGTH_FRACTION));
    }

    /// <summary>
    /// The vehicle a hook picks: the nearest one that is a Seamoth or a Prawn Suit, not the tower itself, nobody sits in, not docked, not destroyed, not towed by anyone, within
    /// <paramref name="range" />. Returns false when there is none.
    /// </summary>
    public static bool TryChooseTarget(IEnumerable<TowCandidate> candidates, float range, out TowCandidate chosen)
    {
        chosen = default;
        bool found = false;
        foreach (TowCandidate candidate in candidates)
        {
            if (!IsValidTarget(candidate, range) || (found && candidate.Distance >= chosen.Distance))
            {
                continue;
            }
            chosen = candidate;
            found = true;
        }
        return found;
    }

    public static bool IsValidTarget(TowCandidate candidate, float range)
    {
        return candidate.Id != null && candidate.IsSeamothOrPrawn && !candidate.IsTower && !candidate.Piloted && !candidate.Docked && candidate.Alive && !candidate.AlreadyTowed && candidate.Distance <= range;
    }

    /// <summary>
    /// The acceleration with which the cable pulls the towed vehicle toward the tower: zero while the cable is slack or the vehicles come closer, otherwise the spring and the damper, but never more than
    /// <see cref="MAX_ACCELERATION" /> and never so much that one physics step of <paramref name="deltaTime" /> overshoots the rest length by more than a quarter of the stretch (which keeps a long step
    /// of the game, 0.1 s when nobody is near, from throwing the vehicle).
    /// </summary>
    /// <param name="length">The distance between the two anchors.</param>
    /// <param name="restLength">The length at which the cable begins to pull (<see cref="RestLength" />).</param>
    /// <param name="separationSpeed">How fast the anchors move apart, in m/s (negative while they come closer).</param>
    public static float PullAcceleration(float length, float restLength, float separationSpeed, float deltaTime)
    {
        float stretch = length - restLength;
        if (!(stretch > 0f))
        {
            return 0f;
        }

        float acceleration = SPRING * stretch + DAMPER * Math.Max(0f, separationSpeed);
        float stable = deltaTime > 0f ? stretch / (deltaTime * deltaTime) * 0.25f : MAX_ACCELERATION;
        return Math.Max(0f, Math.Min(acceleration, Math.Min(MAX_ACCELERATION, stable)));
    }

    /// <summary>Whether the cable is too long. <paramref name="secondsOver" /> is how long it has been longer than the longest cable.</summary>
    public static bool IsTooLong(float length, float towMaxDistance, float secondsOver)
    {
        return length > towMaxDistance * SNAP_FACTOR || (length > towMaxDistance && secondsOver >= TOO_LONG_SECONDS);
    }

    /// <summary>The factor on the speed of a vehicle: the server's factor while it tows, 1 otherwise or when the vehicle upgrades are off.</summary>
    public static float SpeedFactor(bool towing, bool enabled, float towSpeedFactor)
    {
        return towing && enabled ? Math.Max(0.01f, Math.Min(1f, towSpeedFactor)) : 1f;
    }

    /// <summary>What the player who pressed a hook (a quick slot or the button of a Cyclops) reads when nothing was hooked. <paramref name="vehicleName" /> is only known on the client that tried.</summary>
    public static string FailureText(CyclopsTowFailure reason, string vehicleName = null)
    {
        return reason switch
        {
            CyclopsTowFailure.NotAvailable => "Tow hook: not available on this server.",
            CyclopsTowFailure.NoSimulator => "Tow hook: nobody is running the Cyclops, try again in a moment.",
            CyclopsTowFailure.NothingInReach => "Tow hook: no vehicle to hook within reach.",
            CyclopsTowFailure.InUse => string.IsNullOrEmpty(vehicleName) ? "Tow hook: the vehicle is in use." : $"Tow hook: {vehicleName} is in use.",
            CyclopsTowFailure.Interrupted => "Tow hook: the hooking was interrupted, try again.",
            _ => "Tow hook: nothing was hooked."
        };
    }

    // ---- carrying: a towed vehicle that the game keeps still

    /// <summary>
    /// The game keeps a vehicle still (kinematic) while the world is not built around it, which is the case beyond about 37 m from the camera of the local player: a vehicle hooked at the stern of a Cyclops
    /// is 50 m from its helm, where the player who presses the tow button stands. Such a vehicle cannot be pulled by a force, so the cable carries it: the towing client moves it along with the same spring, with the
    /// drag of the water, in these numbers.
    /// </summary>
    public const float CARRY_DRAG = 1f;

    /// <summary>The fastest a carried vehicle is moved (a Seamoth's top speed is about 12 m/s, with the afterburner about 24).</summary>
    public const float CARRY_MAX_SPEED = 30f;

    /// <summary>How fast a carried vehicle turns toward where it goes, and the speed below which it does not turn at all.</summary>
    public const float CARRY_TURN_DEGREES_PER_SECOND = 90f;

    public const float CARRY_MIN_TURN_SPEED = 0.5f;

    /// <summary>Metres between two breadcrumbs of the path that a carried vehicle follows (<see cref="TowTrail" />).</summary>
    public const float TRAIL_SPACING = 0.5f;

    /// <summary>Metres of the tower's path that are kept: the longest cable twice and a margin, which is more than the target of a carried vehicle ever is behind the tower.</summary>
    public static float TrailKeepLength(float towMaxDistance)
    {
        return 2f * Math.Max(0f, towMaxDistance) + 10f;
    }

    /// <summary>
    /// The velocity of a carried vehicle after one step: the pull of the cable (<see cref="PullAcceleration" /> along the cable, toward the tower), the drag of the water, and the speed limit. The direction is the
    /// unit vector from the tower's anchor to the towed vehicle's anchor.
    /// </summary>
    public static (float X, float Y, float Z) CarryVelocity(float vx, float vy, float vz, float towerVx, float towerVy, float towerVz, float dx, float dy, float dz, float length, float restLength, float deltaTime)
    {
        if (!(deltaTime > 0f))
        {
            return (vx, vy, vz);
        }
        float separationSpeed = (vx - towerVx) * dx + (vy - towerVy) * dy + (vz - towerVz) * dz;
        float acceleration = PullAcceleration(length, restLength, separationSpeed, deltaTime);
        vx -= dx * acceleration * deltaTime;
        vy -= dy * acceleration * deltaTime;
        vz -= dz * acceleration * deltaTime;
        float keep = Math.Max(0f, 1f - CARRY_DRAG * deltaTime);
        vx *= keep;
        vy *= keep;
        vz *= keep;
        float speed = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
        if (speed > CARRY_MAX_SPEED)
        {
            float scale = CARRY_MAX_SPEED / speed;
            vx *= scale;
            vy *= scale;
            vz *= scale;
        }
        return (vx, vy, vz);
    }

    /// <summary>Whether the tower should be warned that the towed vehicle is (nearly) below its crush depth, and how strongly; null when not.</summary>
    public static string DepthWarning(float depth, float crushDepth)
    {
        if (crushDepth <= 0f)
        {
            return null;
        }
        if (depth > crushDepth)
        {
            return "below";
        }
        return depth >= crushDepth - DEPTH_WARNING_MARGIN ? "near" : null;
    }
}
