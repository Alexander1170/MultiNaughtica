using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// The numbers behind the height of a player who walks in a Cyclops as another client draws him (bug 6, PLAYERS.md PL9 (6)): how far a body stands above the floor of the hull, when that
/// is too much, and what a measured difference of about a meter is. Plain numbers, no game types, so that it can be tested; <c>PassengerHeightWatch</c> measures and logs, the Debug
/// command <c>passengerheight</c> measures the same way.
/// </summary>
/// <remarks>
/// A player's root (the transform of <c>Player.main</c>, the head, the camera is <see cref="CAMERA_OFFSET_DEFAULT" /> above it) stands <c>standheight + cameraOffset</c> above the floor while he
/// walks: <see cref="STANDING_ROOT_ABOVE_FEET_DEFAULT" /> with the values of the vanilla constructor of <c>PlayerController</c> (standheight 1.5, cameraOffset 0.05 from the collider that
/// Nitrox gives a remote body, <c>RemotePlayer.UpdateAnimationAndCollider</c>). The game reads them from the prefab at run time and so does the measuring code; these defaults are only
/// what a test and the first guess use. The two controller heights of vanilla (stand 1.5, swim 0.5) differ by <see cref="StanceStep" /> = 1.0 m, which is also the amount the game lifts
/// the root of a player who gets up from the water.
/// </remarks>
public static class PassengerHeightRules
{
    /// <summary>
    /// <c>PlayerController.cameraOffset</c> as far as it can be read from the remote body's collider (its top is the root minus this).
    /// </summary>
    public const float CAMERA_OFFSET_DEFAULT = 0.05f;

    /// <summary>
    /// <c>PlayerController.standheight</c> of the vanilla constructor (the prefab may set another value; the measuring code reads the real one).
    /// </summary>
    public const float STAND_HEIGHT_DEFAULT = 1.5f;

    /// <summary>
    /// <c>PlayerController.swimheight</c> of the vanilla constructor.
    /// </summary>
    public const float SWIM_HEIGHT_DEFAULT = 0.5f;

    /// <summary>
    /// The distance from the root of a walking player to the floor under his feet, by the defaults above.
    /// </summary>
    public const float STANDING_ROOT_ABOVE_FEET_DEFAULT = STAND_HEIGHT_DEFAULT + CAMERA_OFFSET_DEFAULT;

    /// <summary>
    /// A body whose feet are further above the floor than this (meters) is not walking on it: a step of the stairs, the skin of the collider and the soles of the model are below it.
    /// </summary>
    public const float FLOATING_GAP = 0.45f;

    /// <summary>
    /// Below this (meters) a difference between two readings of the same thing is no difference (the check of the live run asks for 10 cm).
    /// </summary>
    public const float SAME_HEIGHT_TOLERANCE = 0.10f;

    /// <summary>
    /// A gap this far (meters) from the stance step is called the stance step.
    /// </summary>
    public const float STANCE_STEP_TOLERANCE = 0.15f;

    /// <summary>
    /// A body has to stand above the floor for this long (seconds) before a line is written: a jump lasts well below it.
    /// </summary>
    public const float SUSTAIN_SECONDS = 2f;

    /// <summary>
    /// At most one line per body and this many seconds about the same floating.
    /// </summary>
    public const float REPORT_INTERVAL = 30f;

    /// <summary>
    /// The line of "where the pawn of a player who boards came to rest" is written this many seconds after the pawn was made (it starts at the root of the body, the walking motor lets it
    /// fall to what is below; the watch looks once a second, so the line comes between this and one second more).
    /// </summary>
    public const float BOARDING_SETTLE_SECONDS = 2f;

    /// <summary>
    /// The pawn is old enough for the line of where it came to rest (<see cref="BOARDING_SETTLE_SECONDS" />). <see cref="float.NaN" /> (a pawn the watch did not see being made) is not.
    /// </summary>
    public static bool IsBoardingSettled(float secondsSincePawnCreated)
    {
        return !float.IsNaN(secondsSincePawnCreated) && secondsSincePawnCreated >= BOARDING_SETTLE_SECONDS;
    }

    /// <summary>
    /// A copy that has had a pawn for this many seconds and has not received one pawn packet is not a walker the pawn rule can see (<see cref="IsPawnSilent" />). The sender announces a
    /// player who stands still twice a second, so this is long.
    /// </summary>
    public const float PAWN_SILENT_AFTER_SECONDS = 3f;

    /// <summary>
    /// The copy has had a pawn for <paramref name="secondsSincePawnCreated" /> seconds and still is no walker (<paramref name="walking" />: the first <c>PlayerInCyclopsMovement</c> makes
    /// it one). Then the sender sends movement of another kind while his body is in the hull (world movement: no pawn on his side), the copy is positioned by that and drawn in the pose
    /// of his last report, which is exactly what the pawn rule does not cover. <see cref="float.NaN" /> (a pawn the watch did not see being made) is not judged.
    /// </summary>
    public static bool IsPawnSilent(float secondsSincePawnCreated, bool walking)
    {
        return !walking && !float.IsNaN(secondsSincePawnCreated) && secondsSincePawnCreated >= PAWN_SILENT_AFTER_SECONDS;
    }

    /// <summary>
    /// The gap to the floor that a body is judged by: the larger of that of the expected feet (the root minus a standing height, what the position says) and that of the lowest foot bone
    /// (what is drawn, <see cref="float.NaN" /> without foot bones or floor). A root that is right in a pose that is wrong, and a root that is wrong in a pose that is right, both make
    /// the body float for the player who looks at it.
    /// </summary>
    public static float JudgedGap(float expectedFeetGap, float drawnFeetGap)
    {
        if (float.IsNaN(drawnFeetGap))
        {
            return expectedFeetGap;
        }

        return float.IsNaN(expectedFeetGap) ? drawnFeetGap : Math.Max(expectedFeetGap, drawnFeetGap);
    }

    /// <summary>
    /// What a measured difference of height is.
    /// </summary>
    public enum OffsetKind
    {
        /// <summary>No difference worth the name (below <see cref="SAME_HEIGHT_TOLERANCE" />).</summary>
        NONE,

        /// <summary>The difference between the stand and the swim height of the controller: a body in the pose of the other stance, or the root of one stance on the floor of the other.</summary>
        STANCE_STEP,

        /// <summary>Something else, which has to be looked at (a step of the floor, a body on a piece of furniture, a mismatch between the copy of the hull and the hull).</summary>
        OTHER
    }

    /// <summary>
    /// The step the root of a player makes between the swimming and the walking controller (vanilla lifts the root by this when a swimmer gets up from the water).
    /// </summary>
    public static float StanceStep(float standHeight, float swimHeight)
    {
        return standHeight - swimHeight;
    }

    /// <summary>
    /// How far the feet are above the floor found by a ray that starts <paramref name="rayOriginAboveFeet" /> above the (expected) feet and hit the floor <paramref name="hitDistance" /> along
    /// it: negative when the feet are in the floor.
    /// </summary>
    public static float FloorGap(float rayOriginAboveFeet, float hitDistance)
    {
        return hitDistance - rayOriginAboveFeet;
    }

    /// <summary>
    /// The height of a root above the floor, both in the same space (the space of the hull).
    /// </summary>
    public static float RootAboveFloor(float rootY, float floorY)
    {
        return rootY - floorY;
    }

    /// <summary>
    /// The feet are further above the floor than a player who walks on it can have them. A body without a floor below it (<see cref="float.NaN" />) is not judged.
    /// </summary>
    public static bool IsFloating(float gap)
    {
        return !float.IsNaN(gap) && gap > FLOATING_GAP;
    }

    /// <summary>
    /// Names a measured difference of height (the same player on two clients, or the same body in two poses): about zero, about the stance step (what the swimming pose against the walking
    /// pose, or the root of the one against the floor of the other, would give), or something else.
    /// </summary>
    public static OffsetKind Classify(float difference, float standHeight, float swimHeight)
    {
        float magnitude = Math.Abs(difference);
        if (magnitude < SAME_HEIGHT_TOLERANCE)
        {
            return OffsetKind.NONE;
        }

        return Math.Abs(magnitude - StanceStep(standHeight, swimHeight)) <= STANCE_STEP_TOLERANCE ? OffsetKind.STANCE_STEP : OffsetKind.OTHER;
    }

    /// <summary>
    /// What the difference between the walking pose and the swimming pose of the same model says about the theory of bug 6 (a copy in the swimming pose around the root of a walker has its
    /// legs about the stance step too high).
    /// </summary>
    public enum PoseTheory
    {
        /// <summary>The difference could not be measured (no foot bones, the animator did not move).</summary>
        UNKNOWN,

        /// <summary>The swimming pose has its feet about the stance step above those of the walking pose: a copy drawn in it around the root of a walker would have its legs about a meter too high, as the report says.</summary>
        SUPPORTED,

        /// <summary>The two poses differ by something else: the swimming pose cannot make the meter of the report, the cause is not (only) the pose.</summary>
        REFUTED
    }

    /// <summary>
    /// Judges the theory from <paramref name="feetDelta" />, the lowest foot of the walking pose below the root minus that of the swimming pose (positive: the swimming pose has its feet higher).
    /// The pawn rule makes the copy of a walker stand whatever it reported, so no reading of a copy in the Cyclops can fail with the rule in force: this difference is the one number that can
    /// say that the pose is not what the report was about.
    /// </summary>
    public static PoseTheory JudgePoseTheory(float feetDelta, float standHeight, float swimHeight)
    {
        if (float.IsNaN(feetDelta))
        {
            return PoseTheory.UNKNOWN;
        }

        return feetDelta > 0f && Classify(feetDelta, standHeight, swimHeight) == OffsetKind.STANCE_STEP ? PoseTheory.SUPPORTED : PoseTheory.REFUTED;
    }

    /// <summary>
    /// The two readings of the height of the same player (the root in the space of the hull, on his own client and on the other one) agree.
    /// </summary>
    public static bool Agree(float ownHeight, float otherHeight)
    {
        return Math.Abs(ownHeight - otherHeight) < SAME_HEIGHT_TOLERANCE;
    }

    /// <summary>
    /// Decides when a body that stands above the floor is written to the log: after it has done so for <see cref="SUSTAIN_SECONDS" />, and then at most every
    /// <see cref="REPORT_INTERVAL" /> seconds. One per body.
    /// </summary>
    public sealed class FloatTracker
    {
        private float floatingFor;
        private float sinceReport = float.PositiveInfinity;

        /// <summary>
        /// How long (seconds) the body has stood above the floor without a pause.
        /// </summary>
        public float FloatingFor => floatingFor;

        /// <summary>
        /// One measurement, <paramref name="deltaTime" /> seconds after the last one. <paramref name="gap" /> is the gap to the floor, <see cref="float.NaN" /> when there was no floor to
        /// measure. True when a line is due.
        /// </summary>
        public bool Step(float gap, float deltaTime)
        {
            sinceReport += deltaTime;
            if (!IsFloating(gap))
            {
                floatingFor = 0f;
                return false;
            }

            floatingFor += deltaTime;
            if (floatingFor < SUSTAIN_SECONDS || sinceReport < REPORT_INTERVAL)
            {
                return false;
            }

            sinceReport = 0f;
            return true;
        }
    }
}
