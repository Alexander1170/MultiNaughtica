using System;
using System.Globalization;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// What the navigator has done to the map of the console with the mouse: the turn about the map's own axis (yaw), the tilt of the map towards the navigator, and the zoom. Pure numbers, no Unity types, so that the
/// steps are tested. The console turns the terrain hologram and its icons by <see cref="Yaw" />, tilts the map's frame by <see cref="Tilt" /> and scales both by <see cref="Zoom" />.
/// <para>
/// <b>The map turns freely, all the way round.</b> Neither angle has a limit: the yaw spins the map about its own axis for as many turns as the mouse is dragged, and the tilt rolls it over the top and under, from
/// the view straight down at the terrain (the map facing the navigator) to the view across it (the map flat, seen from the side) and on to the underside and back, so that every view of the terrain from every
/// side can be had. The two numbers only say where the map is in its turn, so they wrap (<see cref="NormalizeYaw" />) and nothing stops or springs back at any point of the circle. Only the zoom has limits.
/// </para>
/// </summary>
public sealed class NavigatorMapView
{
    /// <summary>
    /// The turn (degrees about the vertical, on top of the frame's own turn towards the viewer) that the player has given the map: 0 is the map as the real world lies, tilted towards the viewer, so that what is away from
    /// the viewer in the room is away from them on the map (heading up). The hologram floats in the middle of the build room and the player walks round it, so the map is registered to the world and only its tilt
    /// follows the viewer (<see cref="NavigatorConsoleLayout.FrameYawToward" />).
    /// </summary>
    public const float DEFAULT_YAW = 0f;

    /// <summary>
    /// Degrees from the horizontal that the map is tilted towards the navigator at first (90 would be upright, the map facing the navigator; 0 is flat, seen across; beyond 90 it leans on over the top, and a
    /// negative tilt leans it away). There is no least or greatest tilt: it is an angle in a full circle.
    /// </summary>
    public const float DEFAULT_TILT = 58f;

    /// <summary>The map is first drawn so that the whole range fits the disc; zooming only goes closer.</summary>
    public const float MIN_ZOOM = 1f;

    public const float MAX_ZOOM = 8f;

    /// <summary>The factor of one tick of the mouse wheel.</summary>
    public const float ZOOM_PER_TICK = 1.2f;

    public const float YAW_PER_PIXEL = 0.35f;
    public const float TILT_PER_PIXEL = 0.25f;

    public NavigatorMapView()
    {
        Yaw = DEFAULT_YAW;
        Tilt = DEFAULT_TILT;
        Zoom = MIN_ZOOM;
    }

    /// <summary>Degrees, kept between -180 (not included) and 180: the map has no end to its turn, this is only where it is in it.</summary>
    public float Yaw { get; private set; }

    /// <summary>Degrees, kept between -180 (not included) and 180 like the yaw: the map rolls right round, so there is no end to this either.</summary>
    public float Tilt { get; private set; }

    public float Zoom { get; private set; }

    /// <summary>
    /// How many times the view has changed (a drag, a turn of the wheel, a set, a load or a reset that moved something; one that changed nothing does not count). Whoever keeps the view somewhere compares it with
    /// the number it saved last, instead of comparing angles every frame (<see cref="NavigatorViewKeeper" />).
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Whether the side of the map that the terrain stands on is turned to the navigator (the tilt from flat, through upright, to flat again over the top), or the map has been rolled over so that the navigator
    /// looks at its underside. The turn about the map's own axis is the other way round on the screen between the two, which <see cref="Drag" /> sets right.
    /// </summary>
    public bool ShowsItsFront => FacesViewer(Tilt);

    /// <summary>Puts the view back to how the map first stands. Only the navigator asks for this (the R key and the Reset view button): the console never does it by itself, not when someone sits down at it or gets up.</summary>
    public void Reset()
    {
        Apply(DEFAULT_YAW, DEFAULT_TILT, MIN_ZOOM);
    }

    private void Apply(float yaw, float tilt, float zoom)
    {
        if (yaw == Yaw && tilt == Tilt && zoom == Zoom)
        {
            return;
        }
        Yaw = yaw;
        Tilt = tilt;
        Zoom = zoom;
        Revision++;
    }

    /// <summary>Sets the whole view at once (the Debug command of the live check): the angles are taken round the circle, the zoom is kept within its limits.</summary>
    public void Set(float yaw, float tilt, float zoom)
    {
        Apply(NormalizeYaw(yaw), IsNumber(tilt) ? NormalizeYaw(tilt) : DEFAULT_TILT, IsNumber(zoom) ? Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, zoom)) : MIN_ZOOM);
    }

    /// <summary>
    /// The view as one line of text, "yaw,tilt,zoom" (degrees, degrees and times, with a dot for the decimals whatever the language of the machine): how the console keeps it between sessions
    /// (<see cref="NavigatorViewKeeper" />). <see cref="TryLoad" /> reads it back.
    /// </summary>
    public string ToText()
    {
        return string.Join(",", Yaw.ToString("G9", CultureInfo.InvariantCulture), Tilt.ToString("G9", CultureInfo.InvariantCulture), Zoom.ToString("G9", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Takes the view from the text <see cref="ToText" /> wrote (the angles go round the circle and the zoom stays within its limits, as for <see cref="Set" />). False, with the view as it was, when the text is empty
    /// or is not three numbers: a saved view that cannot be read is not an error, the map just stands as it first did.
    /// </summary>
    public bool TryLoad(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string[] parts = text.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }
        float[] values = new float[3];
        for (int i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !IsNumber(values[i]))
            {
                return false;
            }
        }
        Set(values[0], values[1], values[2]);
        return true;
    }

    /// <summary>
    /// The mouse moved <paramref name="dx" /> and <paramref name="dy" /> pixels with the drag button held: across spins the map about its own axis, up and down rolls it over (dragging up lays the map down towards the
    /// horizontal, and on past it). Neither stops: the map goes round as often as the mouse is dragged. Dragging right turns the map clockwise as the navigator sees it, whichever side of the map faces them: with the
    /// map rolled over the same turn about its own axis would look the other way round, so it is reversed while the underside is shown (the way a turntable view in a modelling program does it).
    /// </summary>
    public void Drag(float dx, float dy)
    {
        if (!IsNumber(dx) || !IsNumber(dy))
        {
            return;
        }
        float turn = dx * YAW_PER_PIXEL;
        Apply(NormalizeYaw(Yaw + (ShowsItsFront ? turn : -turn)), NormalizeYaw(Tilt - dy * TILT_PER_PIXEL), Zoom);
    }

    /// <summary>
    /// Whether the navigator, who looks across at the map from the side that the tilt leans towards, sees the side of the map with the terrain on it at this tilt: from flat (0) over upright (90) and on to flat again
    /// (180) it is that side, and from there round through the underside back to flat it is the other. The two meet where the map is seen edge-on.
    /// </summary>
    public static bool FacesViewer(float tilt)
    {
        return NormalizeYaw(tilt) >= 0f;
    }

    /// <summary>The mouse wheel turned by <paramref name="ticks" /> (positive is away from the navigator, which zooms in).</summary>
    public void Scroll(float ticks)
    {
        if (!IsNumber(ticks))
        {
            return;
        }
        Apply(Yaw, Tilt, Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, Zoom * (float)Math.Pow(ZOOM_PER_TICK, ticks))));
    }

    /// <summary>Metres from the middle of the map to its rim at the current zoom, for a map that shows <paramref name="range" /> metres at zoom 1.</summary>
    public float VisibleRadius(float range)
    {
        return Math.Max(0f, range) / Zoom;
    }

    /// <summary>An angle in degrees taken to the circle from -180 (not included) to 180 (the yaw and the tilt both use it); anything that is not a number is 0.</summary>
    public static float NormalizeYaw(float degrees)
    {
        if (!IsNumber(degrees))
        {
            return 0f;
        }
        float wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped <= -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    private static bool IsNumber(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
