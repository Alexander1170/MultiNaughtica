using System;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>
/// Where the engineer console is in the engine room of the Cyclops, in the hull's own frame (x to the pilot's right is negative, so the port side is positive; y up; z from the bow back is positive: the helm at
/// z -25 faces the bow at -z, the engine room is at z 1 to 14). The numbers are read from the game's own scene file of the Cyclops (the "cyclops" scene bundle), so they hold for every Cyclops of this game version.
/// Pure numbers (tuples), so that the placement is tested.
/// </summary>
/// <remarks>
/// The engine room's one console, <c>submarine_engine_console_01</c> at (0.66, -0.83, 7.10) with its screen at (0.73, 0.79, 7.11), is NOT free for the engineer: it is the game's Upgrade Console, where the modules are
/// installed, with a hand target of its own; a second hand target over it would hide it. The engineer's console is a small panel that the mod builds on the same face of the engine, 2.2 m aft of it (the engine
/// block's collider spans x -1.0 to 0.9 and z 1.0 to 11.0, the walkway on the port side runs from x 0.9 to the wall at about 2.8, the deck is at y -0.83), facing the walkway: the player stands in the walkway and
/// looks to starboard at it, the way they look at the Upgrade Console. The facing of the Upgrade Console (port, +x) is inferred from the slope of its keys; the live check moves the panel with a Debug command.
/// </remarks>
public static class EngineerConsoleLayout
{
    /// <summary>The port face of the engine block's collider: the panel hangs on it.</summary>
    public const float ENGINE_PORT_FACE_X = 0.9f;

    /// <summary>The wall on the port side of the walkway.</summary>
    public const float PORT_WALL_X = 2.8f;

    public const float DECK_Y = -0.83f;

    /// <summary>The game's Upgrade Console in the same row: the engineer's hand target stays clear of its stretch (it is about a metre and a half wide along z).</summary>
    public const float UPGRADE_CONSOLE_Z = 7.10f;

    public const float UPGRADE_CONSOLE_HALF_Z = 0.80f;

    /// <summary>The centre of the panel (its middle, at about the height of the eyes of a standing player minus a hand).</summary>
    public static readonly (float X, float Y, float Z) PanelCenter = (1.02f, 0.50f, 9.30f);

    /// <summary>The panel's width (along z) and height.</summary>
    public static readonly (float Width, float Height) PanelSize = (1.00f, 0.60f);

    /// <summary>The trigger that the player's hand finds: its walkway face (1.60) is in front of the panel (1.02) and of the engine's collider (0.90), so a ray from the eyes enters it first.</summary>
    public static readonly (float X, float Y, float Z) HandTargetCenter = (1.30f, 0.50f, 9.30f);

    public static readonly (float X, float Y, float Z) HandTargetSize = (0.60f, 1.00f, 1.30f);

    /// <summary>Where the live check puts the engineer (x, z) and what it looks at (the middle of the panel).</summary>
    public static readonly (float X, float Z) StandSpot = (2.10f, 9.30f);

    public static readonly (float X, float Y, float Z) LookAt = (1.02f, 0.50f, 9.30f);

    /// <summary>Whether a point in the hull's frame is inside the hand-target trigger (for the tests and the Debug dump).</summary>
    public static bool InsideHandTarget((float X, float Y, float Z) point)
    {
        return Math.Abs(point.X - HandTargetCenter.X) <= HandTargetSize.X / 2f
               && Math.Abs(point.Y - HandTargetCenter.Y) <= HandTargetSize.Y / 2f
               && Math.Abs(point.Z - HandTargetCenter.Z) <= HandTargetSize.Z / 2f;
    }
}
