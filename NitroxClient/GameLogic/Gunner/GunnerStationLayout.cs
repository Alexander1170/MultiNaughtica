using System;

namespace NitroxClient.GameLogic.Gunner;

/// <summary>
/// Where the gunner station is in the Cyclops, in the hull's own frame (x to the pilot's right is negative, y up, z from the bow back is positive: the pilot at z -24.9 faces the bow at -z). The numbers are read
/// from the game's own scene file of the Cyclops (the "cyclops" scene bundle), so they hold for every Cyclops of this game version. Pure numbers (tuples), so that the placement is tested.
/// </summary>
/// <remarks>
/// The right desk is NOT the mirror image of the navigator's left one: its solid collider spans x -3.26 to -1.89, y -1.11 to 0.32 and z -22.00 to -19.12 (a low desk standing on the deck, 0.63 m lower than the
/// left one), the game's sub-name screen is at its bow end (z -22.17 to -21.31) and the Cyclops lighting panel with the buttons of the Power Share and the Tow Hook is at its stern end (z -19.71 to -18.56). Only
/// the stretch between them is free for the hand target.
/// </remarks>
public static class GunnerStationLayout
{
    public const float DESK_MIN_X = -3.262f;

    /// <summary>The face of the desk that the player stands in front of (towards the aisle).</summary>
    public const float DESK_AISLE_X = -1.890f;

    public const float DESK_MIN_Z = -22.004f;
    public const float DESK_MAX_Z = -19.123f;
    public const float DESK_TOP_Y = 0.319f;

    /// <summary>The game's sub-name screen, at the desk's bow end: the hand target stays clear of it.</summary>
    public const float SUB_NAME_MIN_Z = -22.17f;

    public const float SUB_NAME_MAX_Z = -21.31f;

    /// <summary>The row of the lighting panel's buttons (the game's two, ours two more), at the desk's stern end: the hand target stays clear of it.</summary>
    public const float LIGHTING_ROW_MIN_Z = -19.71f;

    public const float LIGHTING_ROW_MAX_Z = -18.56f;

    /// <summary>The trigger that the player's hand finds: its front face (-1.65) is in front of the desk's solid face (-1.89), so a ray from the eyes enters it first, and it fits between the two screens.</summary>
    public static readonly (float X, float Y, float Z) HandTargetCenter = (-2.15f, 0.55f, -20.625f);

    public static readonly (float X, float Y, float Z) HandTargetSize = (1.00f, 1.50f, 1.15f);

    /// <summary>Where the live check puts the gunner (x, z) and what it looks at (the desk top at the wall).</summary>
    public static readonly (float X, float Z) StandSpot = (-1.10f, -20.60f);

    public static readonly (float X, float Y, float Z) LookAt = (-2.45f, 0.32f, -20.60f);

    /// <summary>The turret's mount: the vanilla camera anchor of the conning tower, the only spot with a view all round.</summary>
    public static readonly (float X, float Y, float Z) MountPosition = (-0.04f, 9.93f, -3.15f);

    /// <summary>Whether a point in the hull's frame is inside the hand-target trigger (for the tests and the Debug dump).</summary>
    public static bool InsideHandTarget((float X, float Y, float Z) point)
    {
        return Math.Abs(point.X - HandTargetCenter.X) <= HandTargetSize.X / 2f
               && Math.Abs(point.Y - HandTargetCenter.Y) <= HandTargetSize.Y / 2f
               && Math.Abs(point.Z - HandTargetCenter.Z) <= HandTargetSize.Z / 2f;
    }
}
