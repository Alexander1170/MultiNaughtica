using System.Collections.Generic;
using NitroxClient.GameLogic.Navigator;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The shapes of the icons of the navigator's map, drawn once from distance functions into small white textures (the console tints them), so that no art asset is needed: the Cyclops' outline, an arrow for
/// a teammate, a diamond for a vehicle, a filled disc for a creature, a ring for a beacon, a square for a base.
/// </summary>
public static class NavigatorIcons
{
    public enum Shape
    {
        Dot,
        Ring,
        Triangle,
        Diamond,
        Square,
        Hull,
        Pin
    }

    private const int SIZE = 64;
    private static readonly Dictionary<Shape, Sprite> cache = [];

    public static Sprite Get(Shape shape)
    {
        if (cache.TryGetValue(shape, out Sprite sprite) && sprite)
        {
            return sprite;
        }
        sprite = Make(shape);
        cache[shape] = sprite;
        return sprite;
    }

    /// <summary>The shape that a kind of contact is drawn with.</summary>
    public static Shape ShapeOf(ContactKind kind, CreatureDanger danger)
    {
        switch (kind)
        {
            case ContactKind.Cyclops:
            case ContactKind.OtherCyclops:
                return Shape.Hull;
            case ContactKind.Teammate:
                return Shape.Triangle;
            case ContactKind.Vehicle:
                return Shape.Diamond;
            case ContactKind.Creature:
                return danger == CreatureDanger.Leviathan ? Shape.Dot : Shape.Triangle;
            case ContactKind.Beacon:
                return Shape.Ring;
            case ContactKind.Base:
                return Shape.Square;
            case ContactKind.Mark:
                return Shape.Pin;
            default:
                return Shape.Ring;
        }
    }

    /// <summary>The size in canvas pixels of a contact's icon (the hull is long, the leviathans big).</summary>
    public static Vector2 SizeOf(ContactKind kind, CreatureDanger danger)
    {
        switch (kind)
        {
            case ContactKind.Cyclops:
                return new Vector2(26f, 78f);
            case ContactKind.OtherCyclops:
                return new Vector2(14f, 42f);
            case ContactKind.Creature:
                return danger == CreatureDanger.Leviathan ? new Vector2(30f, 30f) : new Vector2(22f, 22f);
            case ContactKind.Base:
                return new Vector2(22f, 22f);
            case ContactKind.Mark:
                return new Vector2(26f, 32f);
            default:
                return new Vector2(20f, 20f);
        }
    }

    private static Sprite Make(Shape shape)
    {
        Texture2D texture = new(SIZE, SIZE, TextureFormat.RGBA32, false)
        {
            name = $"NitroxNavigatorIcon_{shape}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color32[] pixels = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        {
            for (int x = 0; x < SIZE; x++)
            {
                float u = (x + 0.5f) / SIZE * 2f - 1f;
                float v = (y + 0.5f) / SIZE * 2f - 1f;
                float distance = Distance(shape, u, v);
                byte alpha = (byte)(Mathf.Clamp01(0.5f - distance * (SIZE / 2f)) * 255f);
                pixels[y * SIZE + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false);
        return Sprite.Create(texture, new Rect(0f, 0f, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>Signed distance of a point of the unit square to the shape's edge (negative inside), in the units of that square.</summary>
    private static float Distance(Shape shape, float u, float v)
    {
        switch (shape)
        {
            case Shape.Ring:
                return Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.72f) - 0.16f;
            case Shape.Triangle:
                // pointing up: vertices (0, 0.9), (-0.8, -0.8), (0.8, -0.8)
                return Mathf.Max(-v - 0.8f, Mathf.Max(-0.905f * u + 0.426f * v - 0.383f, 0.905f * u + 0.426f * v - 0.383f));
            case Shape.Diamond:
                return (Mathf.Abs(u) + Mathf.Abs(v)) * 0.7071f - 0.8f;
            case Shape.Square:
                return Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) - 0.72f;
            case Shape.Pin:
                {
                    // a teardrop: a disc on a cone that points down
                    float disc = Mathf.Sqrt(u * u + (v - 0.3f) * (v - 0.3f)) - 0.5f;
                    float width = 0.5f * (v + 0.85f) / 1.15f;
                    float cone = Mathf.Max(Mathf.Abs(u) - width, Mathf.Max(-0.85f - v, v - 0.3f));
                    return Mathf.Min(disc, cone);
                }
            case Shape.Hull:
                {
                    // a capsule outline, long along v, with a dot at the bow end
                    float clamped = Mathf.Clamp(v, -0.5f, 0.5f);
                    float capsule = Mathf.Sqrt(u * u + (v - clamped) * (v - clamped)) - 0.42f;
                    float outline = Mathf.Abs(capsule) - 0.07f;
                    float bow = Mathf.Sqrt(u * u + (v - 0.55f) * (v - 0.55f)) - 0.16f;
                    return Mathf.Min(outline, bow);
                }
            default:
                return Mathf.Sqrt(u * u + v * v) - 0.85f;
        }
    }
}
