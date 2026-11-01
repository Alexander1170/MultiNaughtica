using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// The helm's own copy of the navigator's map (<see cref="NitroxClient.MonoBehaviours.Cyclops.CyclopsHelmMap" />) and the buttons that the helm HUD gets, as plain numbers, in the hull's frame (x to the pilot's right is
/// negative, y up, z from the bow back is positive). The pilot's eyes are where the game's own <c>PilotingChair</c> puts them (<c>sittingPosition</c> of the steering console in the Cyclops scene).
/// <para>
/// <b>The map</b> hangs where the game's own sonar map of the helm hung (an earlier build put it at the crosshair, straight ahead of the eyes; it hangs again where it was, to the
/// pilot's right: <c>SonarMap_Small</c> of the Cyclops scene), a disc <see cref="SHRINK" /> times the size of the old one (whose radius for the display's 130 m was 1.25 m, from the game's own scale; the play-test of
/// coop.12 found it too big), tilted towards the pilot's seat, with the bow ahead. It shows at most the 130 m that the old display showed, which keeps the second hologram's terrain light.
/// </para>
/// </summary>
public static class HelmLayout
{
    /// <summary>The pilot's eyes in the chair.</summary>
    public static readonly (float X, float Y, float Z) SeatEye = (-0.05f, 0.93f, -23.75f);

    /// <summary>The middle of the map: where the game's own little hologram of the helm hangs (<c>SonarMap_Small</c> of the Cyclops scene), 1.3 m to the pilot's right (x is negative there), 0.6 m ahead of the eyes and 0.4 m below them.</summary>
    public static readonly (float X, float Y, float Z) MapCenter = (-1.347f, 0.525f, -24.365f);

    /// <summary>The radius of the helm display's hologram before the play-test: the game's 130 m scan radius at its own 0.00962 display units per metre.</summary>
    public const float OLD_RADIUS = 1.25f;

    /// <summary>The user asked for the map to be about 15 percent smaller than it was.</summary>
    public const float SHRINK = 0.85f;

    public const float RADIUS = OLD_RADIUS * SHRINK;

    /// <summary>The helm's map never shows more than this many metres (what the old helm display showed).</summary>
    public const float MAX_RANGE = 130f;

    /// <summary>The turn of the map's frame about the vertical: the frame is tilted towards its +x, so turned until +x points at the pilot's seat (see <see cref="NavigatorConsoleLayout.FrameYawToward" />): back and to the left of the map.</summary>
    public static readonly float FRAME_YAW = NavigatorConsoleLayout.FrameYawToward(SeatEye, MapCenter, 0f);

    /// <summary>Degrees from the horizontal that the map is tilted towards the pilot.</summary>
    public const float TILT = 50f;

    /// <summary>The map shows while the local player is within this many metres of it (the cockpit), and stays until they are farther than the hide distance.</summary>
    public const float SHOW_DISTANCE = 9f;

    public const float HIDE_DISTANCE = 12f;

    /// <summary>The range that the helm's map is built for: what the navigator's hologram would show, but not more than <see cref="MAX_RANGE" />, and at least a few metres.</summary>
    public static float RangeFor(float wanted)
    {
        if (float.IsNaN(wanted))
        {
            return MAX_RANGE;
        }
        return Math.Max(10f, Math.Min(MAX_RANGE, wanted));
    }

    // ---- the HUD's right-hand column of buttons

    /// <summary>
    /// The rows of the column that the game fills, from the top: the camera, the silent running, the decoy, the sonar and the shield. The sonar and the shield buttons are only there while the Cyclops has the module;
    /// the rows without one are free for the buttons that this adds. After the shield's row the column goes on (one more row each).
    /// </summary>
    public const int SONAR_ROW = 3;

    public const int SHIELD_ROW = 4;

    /// <summary>The free rows of the column, in order, as many as asked for: the sonar's and the shield's while their modules are out, then the rows below the shield's.</summary>
    public static IReadOnlyList<int> FreeRows(bool sonarButtonThere, bool shieldButtonThere, int count)
    {
        List<int> rows = [];
        if (!sonarButtonThere)
        {
            rows.Add(SONAR_ROW);
        }
        if (!shieldButtonThere)
        {
            rows.Add(SHIELD_ROW);
        }
        int next = SHIELD_ROW + 1;
        while (rows.Count < count)
        {
            rows.Add(next++);
        }
        return rows.GetRange(0, Math.Min(count, rows.Count));
    }

    /// <summary>
    /// Where a row of the column is, from the positions of the sonar's row and the shield's (the column curves, so the step from one to the next is the step between those two): the position of
    /// <paramref name="row" /> counted from the sonar's, which is row 3.
    /// </summary>
    public static (float X, float Y) RowPosition((float X, float Y) sonar, (float X, float Y) shield, int row)
    {
        float stepX = shield.X - sonar.X;
        float stepY = shield.Y - sonar.Y;
        int from = row - SONAR_ROW;
        return (sonar.X + stepX * from, sonar.Y + stepY * from);
    }
}

/// <summary>Whether the "+" next to the power bolt of the helm HUD shows: the Cyclops Solar Charger is really adding power.</summary>
public static class SolarIndicatorRules
{
    /// <summary>
    /// True when a Cyclops that has a counting Solar Charger module, in a game mode that uses power, with the upgrades on, would add energy this moment (the rate at this depth and in this light is above zero) and has room
    /// for it (its cells are not full). It is worked out by every client from what every client knows (the depth, the time of day, the modules and the cells' charge), so everybody aboard sees the same.
    /// </summary>
    public static bool IsCharging(bool upgradesOn, int countingModules, bool gameUsesPower, float ratePerSecond, float power, float maxPower)
    {
        if (!upgradesOn || countingModules <= 0 || !gameUsesPower)
        {
            return false;
        }
        if (float.IsNaN(ratePerSecond) || ratePerSecond <= 0f)
        {
            return false;
        }
        return maxPower <= 0f || power < maxPower - 0.01f;
    }
}
