using System;
using System.Collections.Generic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The small white pictures of the engineer's PDA screen that the game has no sprite for, drawn once in code (white on clear, tinted by the Image that shows them): the engines, the balance, the overdrive and
/// the vent for the bubbles, a shield, a sonar and a drop as the fallbacks of the module icons, and a flame and a burst for the fires and the breaches. Each is a shape in a square from -1 to 1 (x to the right, y up),
/// sampled four times per pixel for a soft edge. Made when first asked for and kept for the session.
/// </summary>
public static class EngineerGlyphs
{
    private const int SIZE = 96;

    private static readonly Dictionary<string, Sprite> made = [];

    public static Sprite Engines => Get("engines", Propeller);
    public static Sprite Balanced => Get("balanced", Scales);
    public static Sprite Overdrive => Get("overdrive", Chevrons);
    public static Sprite Vent => Get("vent", Steam);
    public static Sprite Shield => Get("shield", ShieldShape);
    public static Sprite Sonar => Get("sonar", Waves);
    public static Sprite Drop => Get("drop", DropShape);
    public static Sprite Flame => Get("flame", FlameShape);
    public static Sprite Burst => Get("burst", BurstShape);

    /// <summary>A plain round dot (the marker of the engineer on the picture, the badges of the counts).</summary>
    public static Sprite Disc => Get("disc", (u, v) => Radius(u, v) < 0.95f);

    /// <summary>A ring about as thick as the lit edge of the upgrade console's module slots: the edge of a bubble, tinted by its state.</summary>
    public static Sprite Ring => Get("ring", (u, v) => Radius(u, v) is > 0.84f and < 0.98f);

    /// <summary>A soft glow that fades out from the middle: drawn 1.5 times the size of a bubble behind it (the bubble covers the middle).</summary>
    public static Sprite Halo => GetColored("halo", HaloColor);

    private static Sprite Get(string name, Func<float, float, bool> inside)
    {
        return GetColored(name, (u, v) => inside(u, v) ? Color.white : Color.clear);
    }

    /// <summary>A glyph whose colour and opacity are a function of the spot, sampled four times per pixel (the colours of the samples are averaged by their opacity).</summary>
    private static Sprite GetColored(string name, Func<float, float, Color> shade)
    {
        if (made.TryGetValue(name, out Sprite sprite) && sprite)
        {
            return sprite;
        }
        Texture2D texture = new(SIZE, SIZE, TextureFormat.RGBA32, false) { name = $"NitroxEngineerGlyph-{name}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color32[] pixels = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float red = 0f;
                float green = 0f;
                float blue = 0f;
                float covered = 0f;
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = (x + 0.25f + sx * 0.5f) / SIZE * 2f - 1f;
                        float v = (y + 0.25f + sy * 0.5f) / SIZE * 2f - 1f;
                        Color sample = shade(u, v);
                        float opacity = Math.Max(0f, Math.Min(1f, sample.a));
                        red += sample.r * opacity;
                        green += sample.g * opacity;
                        blue += sample.b * opacity;
                        covered += opacity;
                    }
                }
                pixels[y * SIZE + x] = covered > 0f
                    ? new Color32((byte)Math.Round(red / covered * 255f), (byte)Math.Round(green / covered * 255f), (byte)Math.Round(blue / covered * 255f), (byte)Math.Round(covered / 4f * 255f))
                    : new Color32(255, 255, 255, 0);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0f, 0f, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100f);
        made[name] = sprite;
        return sprite;
    }

    // ---- the shapes

    private static float Radius(float u, float v) => (float)Math.Sqrt(u * u + v * v);

    private static float Distance(float u, float v, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float t = Math.Max(0f, Math.Min(1f, ((u - ax) * dx + (v - ay) * dy) / (dx * dx + dy * dy)));
        return Radius(u - (ax + t * dx), v - (ay + t * dy));
    }

    /// <summary>Three blades round a hub.</summary>
    private static bool Propeller(float u, float v)
    {
        float r = Radius(u, v);
        if (r < 0.2f)
        {
            return true;
        }
        if (r > 0.85f)
        {
            return false;
        }
        double angle = Math.Atan2(v, u);
        for (int blade = 0; blade < 3; blade++)
        {
            double centre = Math.PI / 2.0 + blade * 2.0 * Math.PI / 3.0;
            double difference = Math.Abs(Math.IEEERemainder(angle - centre, 2.0 * Math.PI));
            // a blade is widest a third of the way out and a little curled
            double width = 0.55 * Math.Sin((r - 0.2) / 0.65 * Math.PI);
            if (difference < width * 0.75 - 0.15 * r)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A beam on a post with two pans.</summary>
    private static bool Scales(float u, float v)
    {
        if (Math.Abs(v - 0.45f) < 0.07f && Math.Abs(u) < 0.75f)
        {
            return true;
        }
        if (Math.Abs(u) < 0.07f && v > -0.65f && v < 0.5f)
        {
            return true;
        }
        if (Math.Abs(v + 0.7f) < 0.07f && Math.Abs(u) < 0.4f)
        {
            return true;
        }
        foreach (float side in new[] { -0.6f, 0.6f })
        {
            // the cords down to the pan and the pan (a half disc)
            if (Distance(u, v, side, 0.45f, side - 0.3f, -0.1f) < 0.04f || Distance(u, v, side, 0.45f, side + 0.3f, -0.1f) < 0.04f)
            {
                return true;
            }
            float pu = u - side;
            float pv = v + 0.1f;
            if (pv < 0f && Radius(pu, pv) < 0.32f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Two chevrons, ">>".</summary>
    private static bool Chevrons(float u, float v)
    {
        foreach (float centre in new[] { -0.35f, 0.25f })
        {
            if (Distance(u, v, centre - 0.3f, 0.6f, centre + 0.3f, 0f) < 0.13f || Distance(u, v, centre - 0.3f, -0.6f, centre + 0.3f, 0f) < 0.13f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>An arrow up with two lines of steam beside it.</summary>
    private static bool Steam(float u, float v)
    {
        if (Math.Abs(u) < 0.1f && v > -0.6f && v < 0.15f)
        {
            return true;
        }
        if (v >= 0.15f && v < 0.75f && Math.Abs(u) < (0.75f - v) * 0.7f)
        {
            return true;
        }
        foreach (float side in new[] { -0.5f, 0.5f })
        {
            if (Math.Abs(u - side) < 0.07f && v > -0.4f && v < 0.35f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A shield: flat on top, pointed below.</summary>
    private static bool ShieldShape(float u, float v)
    {
        if (v > 0.75f || v < -0.85f)
        {
            return false;
        }
        float half = v > 0f ? 0.6f : 0.6f * (1f + v / 0.85f);
        return Math.Abs(u) < Math.Max(0f, half);
    }

    /// <summary>Three arcs of a sonar's waves round a dot.</summary>
    private static bool Waves(float u, float v)
    {
        float r = Radius(u, v - 0.0f);
        if (r < 0.14f)
        {
            return true;
        }
        if (v < -0.05f)
        {
            return false;
        }
        foreach (float ring in new[] { 0.42f, 0.68f, 0.94f })
        {
            if (Math.Abs(r - ring) < 0.06f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A drop: a round bottom drawn out to a point above.</summary>
    private static bool DropShape(float u, float v)
    {
        if (Radius(u, v + 0.25f) < 0.5f)
        {
            return true;
        }
        return v >= -0.25f && v < 0.85f && Math.Abs(u) < 0.5f * (1f - (v + 0.25f) / 1.1f);
    }

    /// <summary>A flame: a round bottom with a curled point, and a hollow in its middle.</summary>
    private static bool FlameShape(float u, float v)
    {
        bool outer = Radius(u, v + 0.3f) < 0.5f || (v >= -0.3f && v < 0.9f && Math.Abs(u - 0.15f * (v + 0.3f)) < 0.5f * Math.Pow(1f - (v + 0.3f) / 1.2f, 0.9));
        bool hollow = Radius(u, v + 0.45f) < 0.2f;
        return outer && !hollow;
    }

    /// <summary>A glow: full in the middle (where the bubble covers it) up to two thirds of the way out, then fading to nothing at the edge.</summary>
    private static Color HaloColor(float u, float v)
    {
        float r = Radius(u, v);
        float fade = r < 0.667f ? 1f : Math.Max(0f, 1f - (r - 0.667f) / 0.333f);
        return new Color(1f, 1f, 1f, fade * fade);
    }

    /// <summary>An eight-pointed burst.</summary>
    private static bool BurstShape(float u, float v)
    {
        float r = Radius(u, v);
        double angle = Math.Atan2(v, u);
        double limit = 0.3 + 0.65 * Math.Pow(Math.Abs(Math.Cos(angle * 4.0)), 3.0);
        return r < limit;
    }
}
