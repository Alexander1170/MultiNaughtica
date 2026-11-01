using System;

namespace NitroxClient.GameLogic.Drone;

/// <summary>
/// Where the drone console and the dock of the scout drone are on a Cyclops, in the hull's own frame (x to the port side is positive, y up, the bow at -z; see <c>EngineerConsoleLayout</c>, whose numbers these are
/// built on). The console is a small panel that the mod builds on the engine block's port face FORWARD of the game's Upgrade Console (the engineer's panel is 2.2 m aft of it), facing the walkway, so that neither
/// hides the other's hand target. The dock is outside the hull on the port flank, level with the console, so that the drone leaves and returns where the operator can reach the console. The outer skin is not in the
/// game's files in a form that can be read offline: the dock position is a first guess that the live check measures (Debug <c>vudrone ray</c> and <c>vudrone nudge</c>).
/// The panel and its hand target are not built any more: the drone is used from the left screen of the vehicle bay terminal (<c>DroneTerminalScreen</c>); only the dock below is in use, and the numbers of the panel stay for its tests.
/// </summary>
public static class DroneLayout
{
    /// <summary>The game's Upgrade Console in the same row: the drone console's hand target stays clear of its stretch (about a metre and a half along z).</summary>
    public const float UPGRADE_CONSOLE_Z = 7.10f;

    public const float UPGRADE_CONSOLE_HALF_Z = 0.80f;

    /// <summary>The engineer's hand target (centre z and size along z): the drone console stays clear of it too.</summary>
    public const float ENGINEER_TARGET_MIN_Z = 8.65f;

    /// <summary>The centre of the panel: on the engine's port face, 2.2 m forward of the Upgrade Console (the engineer's panel is 2.2 m aft of it).</summary>
    public static readonly (float X, float Y, float Z) PanelCenter = (1.02f, 0.50f, 4.90f);

    /// <summary>The panel's width (along z) and height: the same as the engineer's.</summary>
    public static readonly (float Width, float Height) PanelSize = (1.00f, 0.60f);

    /// <summary>The trigger that the player's hand finds: its walkway face (1.60) is in front of the panel (1.02) and of the engine's collider (0.90).</summary>
    public static readonly (float X, float Y, float Z) HandTargetCenter = (1.30f, 0.50f, 4.90f);

    public static readonly (float X, float Y, float Z) HandTargetSize = (0.60f, 1.00f, 1.30f);

    /// <summary>Where the live check puts the operator (x, z) and what it looks at (the middle of the panel).</summary>
    public static readonly (float X, float Z) StandSpot = (2.10f, 4.90f);

    public static readonly (float X, float Y, float Z) LookAt = (1.02f, 0.50f, 4.90f);

    /// <summary>The middle of the dock, outside the port flank (first guess: the hull's trigger box is 9 m wide, so its skin is about 4.5 m out).</summary>
    public static readonly (float X, float Y, float Z) DockCenter = (5.20f, 0.50f, 4.90f);

    /// <summary>The trigger of the dock (big enough to catch a drone that comes home).</summary>
    public static readonly (float X, float Y, float Z) DockTriggerSize = (3.00f, 3.00f, 3.00f);

    /// <summary>How far outward from the dock the drone is put when it leaves (metres along the dock's outward direction): clear of the trigger, so that it does not dock again at once.</summary>
    public const float UNDOCK_DISTANCE = 2.2f;

    /// <summary>Whether a point in the hull's frame is inside the hand-target trigger (for the tests and the Debug dump).</summary>
    public static bool InsideHandTarget((float X, float Y, float Z) point)
    {
        return Math.Abs(point.X - HandTargetCenter.X) <= HandTargetSize.X / 2f
               && Math.Abs(point.Y - HandTargetCenter.Y) <= HandTargetSize.Y / 2f
               && Math.Abs(point.Z - HandTargetCenter.Z) <= HandTargetSize.Z / 2f;
    }

    /// <summary>The stretch of z that the hand target covers (min, max).</summary>
    public static (float Min, float Max) HandTargetZ => (HandTargetCenter.Z - HandTargetSize.Z / 2f, HandTargetCenter.Z + HandTargetSize.Z / 2f);
}
