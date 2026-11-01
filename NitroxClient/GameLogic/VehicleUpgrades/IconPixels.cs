using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The pixel work for the icons of the new items (free of Unity types, so that it can be tested): the icon of the vanilla item a module is built on is tinted and gets the number of its tier in a
/// corner, so that a Depth Module MK4 can be told apart from an MK3 in the inventory. Pixels are RGBA bytes, four per pixel, row 0 at the bottom (the order of Unity's textures).
/// </summary>
public static class IconPixels
{
    // 3 x 5 digits, one row per entry, the left pixel is bit 2
    private static readonly int[][] Digits =
    [
        [7, 5, 5, 5, 7], // 0
        [2, 6, 2, 2, 7], // 1
        [7, 1, 7, 4, 7], // 2
        [7, 1, 7, 1, 7], // 3
        [5, 5, 7, 1, 1], // 4
        [7, 4, 7, 1, 7], // 5
        [7, 4, 7, 5, 7], // 6
        [7, 1, 1, 1, 1], // 7
        [7, 5, 7, 5, 7], // 8
        [7, 5, 7, 1, 7]  // 9
    ];

    public const int DIGIT_WIDTH = 3;
    public const int DIGIT_HEIGHT = 5;

    /// <summary>The colour a tier is tinted with: the vanilla tier (3 and below) keeps its colours, the next tiers go from a cold blue to violet.</summary>
    public static (float r, float g, float b) TintFor(int tier)
    {
        return tier switch
        {
            <= 3 => (1f, 1f, 1f),
            4 => (0.55f, 0.85f, 1f),
            5 => (0.85f, 0.65f, 1f),
            _ => (1f, 0.8f, 0.5f)
        };
    }

    /// <summary>Multiplies the colour of every pixel that is not fully transparent with the tint. The alpha channel stays.</summary>
    public static void Tint(byte[] rgba, (float r, float g, float b) tint)
    {
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }
            rgba[i] = ToByte(rgba[i] * tint.r);
            rgba[i + 1] = ToByte(rgba[i + 1] * tint.g);
            rgba[i + 2] = ToByte(rgba[i + 2] * tint.b);
        }
    }

    /// <summary>
    /// Draws the digit (0 to 9) as white with a black shadow into the lower right corner of the image. <paramref name="scale" /> is the size of one font pixel.
    /// Returns false when the digit is out of range or does not fit into the image; the image is not changed then.
    /// </summary>
    public static bool DrawDigit(byte[] rgba, int width, int height, int digit, int scale)
    {
        if (digit < 0 || digit > 9 || scale < 1 || rgba.Length < width * height * 4)
        {
            return false;
        }
        int glyphWidth = DIGIT_WIDTH * scale;
        int glyphHeight = DIGIT_HEIGHT * scale;
        int margin = scale;
        if (glyphWidth + margin * 2 > width || glyphHeight + margin * 2 > height)
        {
            return false;
        }
        int originX = width - glyphWidth - margin;
        int originY = margin;
        // The shadow first, one font pixel to the lower left, then the digit itself
        DrawGlyph(rgba, width, height, digit, scale, originX - scale, originY - scale, 0);
        DrawGlyph(rgba, width, height, digit, scale, originX, originY, 255);
        return true;
    }

    private static void DrawGlyph(byte[] rgba, int width, int height, int digit, int scale, int originX, int originY, byte value)
    {
        int[] rows = Digits[digit];
        for (int row = 0; row < DIGIT_HEIGHT; row++)
        {
            // Row 0 of the font is the top of the digit, and the top is the highest y in the texture
            int fontRow = rows[DIGIT_HEIGHT - 1 - row];
            for (int column = 0; column < DIGIT_WIDTH; column++)
            {
                if ((fontRow & (4 >> column)) == 0)
                {
                    continue;
                }
                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int x = originX + column * scale + dx;
                        int y = originY + row * scale + dy;
                        if (x < 0 || y < 0 || x >= width || y >= height)
                        {
                            continue;
                        }
                        int index = (y * width + x) * 4;
                        rgba[index] = value;
                        rgba[index + 1] = value;
                        rgba[index + 2] = value;
                        rgba[index + 3] = 255;
                    }
                }
            }
        }
    }

    /// <summary>The size of one font pixel for an icon of this height: about one eighth of the icon for the whole digit.</summary>
    public static int ScaleFor(int height)
    {
        return Math.Max(1, height / (DIGIT_HEIGHT * 5));
    }

    private static byte ToByte(float value)
    {
        return (byte)(value < 0f ? 0 : value > 255f ? 255 : value + 0.5f);
    }
}
