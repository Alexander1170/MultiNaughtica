using System;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The pictures of the two buttons that the helm HUD gets (<see cref="CyclopsHelmHud" />), drawn in code: a parallelogram panel in the blue of the game's own buttons with its light edge, and a white-blue glyph on it
/// (a tilted disc with contour rings for the helm map, a bolt between two arrows for the power share). The game's buttons are single sprites with the icon baked into the panel, so there is nothing to borrow an icon from,
/// and no art asset of ours to ship. Made once per session, 293 by 164 pixels like the buttons they stand among.
/// </summary>
internal static class HelmIcons
{
    private const int W = 293;
    private const int H = 164;
    private const float MARGIN = 8f;
    private const float SKEW = 26f;
    private const float CORNER = 16f;

    private static readonly Color Fill = new(0.09f, 0.38f, 0.60f, 0.55f);
    private static readonly Color Edge = new(0.50f, 0.88f, 1f, 0.95f);
    private static readonly Color GlyphColor = new(0.86f, 0.97f, 1f, 1f);

    private static Sprite map;
    private static Sprite share;

    public static Sprite Map => map ? map : map = Build(MapGlyph);

    public static Sprite Share => share ? share : share = Build(ShareGlyph);

    private static Sprite Build(Func<float, float, float> glyph)
    {
        Color32[] pixels = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                Color color = Panel(x, y);
                float coverage = Mathf.Clamp01(glyph(x + 0.5f, y + 0.5f));
                if (coverage > 0f)
                {
                    color = Color.Lerp(color, GlyphColor, coverage);
                    color.a = Mathf.Max(color.a, coverage);
                }
                pixels[y * W + x] = color;
            }
        }
        Texture2D texture = new(W, H, TextureFormat.RGBA32, false)
        {
            name = "NitroxHelmIcon",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, W, H), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "NitroxHelmIcon";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// <summary>The panel: the distance inside a parallelogram with rounded corners (leaning to the right at the top) says whether it is the fill, the light edge, or outside.</summary>
    private static Color Panel(float x, float y)
    {
        float lean = SKEW * (y / H);
        float left = MARGIN + lean;
        float right = W - MARGIN - SKEW + lean;
        float k = 1f / Mathf.Sqrt(1f + (SKEW / H) * (SKEW / H));
        float dx = Mathf.Min(x - left, right - x) * k;
        float dy = Mathf.Min(y - MARGIN, H - MARGIN - y);
        float inside = Mathf.Min(dx, dy);
        if (dx < CORNER && dy < CORNER)
        {
            float cx = CORNER - dx;
            float cy = CORNER - dy;
            inside = Mathf.Min(inside, CORNER - Mathf.Sqrt(cx * cx + cy * cy));
        }
        if (inside <= 0f)
        {
            return new Color(0f, 0f, 0f, 0f);
        }
        float edgeness = Mathf.Clamp01(1f - (inside - 3f) / 2f);
        Color color = Color.Lerp(Fill, Edge, edgeness);
        color.a *= Mathf.Clamp01(inside / 1.5f);
        return color;
    }

    /// <summary>A line of the given width round a curve whose distance from a point is known: 1 on it, falling to 0 within a pixel of its width.</summary>
    private static float Stroke(float distance, float width)
    {
        return Mathf.Clamp01(width * 0.5f + 0.5f - Mathf.Abs(distance));
    }

    private static float EllipseRing(float x, float y, float cx, float cy, float a, float b, float width)
    {
        float f = Mathf.Sqrt(((x - cx) / a) * ((x - cx) / a) + ((y - cy) / b) * ((y - cy) / b));
        return Stroke((f - 1f) * Mathf.Sqrt(a * b), width);
    }

    /// <summary>A tilted disc with two contour rings, the Cyclops in the middle and a short route to the far edge.</summary>
    private static float MapGlyph(float x, float y)
    {
        float cx = W * 0.5f + SKEW * 0.5f;
        float cy = H * 0.5f;
        float coverage = EllipseRing(x, y, cx, cy, 58f, 24f, 4f);
        coverage = Mathf.Max(coverage, EllipseRing(x, y, cx, cy, 36f, 14f, 3f));
        float dot = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        coverage = Mathf.Max(coverage, Mathf.Clamp01(6f - dot));
        // the route: a line from the middle to the upper right, with a point at its end
        float t = Mathf.Clamp01(((x - cx) * 30f + (y - cy) * 22f) / (30f * 30f + 22f * 22f));
        float lineDistance = Mathf.Sqrt((x - (cx + 30f * t)) * (x - (cx + 30f * t)) + (y - (cy + 22f * t)) * (y - (cy + 22f * t)));
        coverage = Mathf.Max(coverage, Stroke(lineDistance, 2.5f) * 0.9f);
        float end = Mathf.Sqrt((x - (cx + 30f)) * (x - (cx + 30f)) + (y - (cy + 22f)) * (y - (cy + 22f)));
        coverage = Mathf.Max(coverage, Mathf.Clamp01(5f - end));
        return coverage;
    }

    private static readonly (float X, float Y)[] Bolt = [(6f, 36f), (-18f, -2f), (-3f, -2f), (-9f, -36f), (18f, 6f), (3f, 6f)];
    private static readonly (float X, float Y)[] LeftArrow = [(-66f, 0f), (-46f, 12f), (-46f, -12f)];
    private static readonly (float X, float Y)[] RightArrow = [(66f, 0f), (46f, 12f), (46f, -12f)];
    private static readonly (float X, float Y)[][] Shapes = [Bolt, LeftArrow, RightArrow];

    /// <summary>A bolt between two arrows pointing outwards: the power goes to the others.</summary>
    private static float ShareGlyph(float x, float y)
    {
        float cx = W * 0.5f + SKEW * 0.5f;
        float cy = H * 0.5f;
        float px = x - cx;
        float py = y - cy;
        float coverage = 0f;
        foreach ((float X, float Y)[] shape in Shapes)
        {
            if (Inside(shape, px, py))
            {
                coverage = 1f;
            }
        }
        return coverage;
    }

    private static bool Inside((float X, float Y)[] polygon, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            bool crosses = (polygon[i].Y > y) != (polygon[j].Y > y);
            if (crosses && x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
