using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxClient.GameLogic;

/// <summary>
///     The pixel work on the icons of the new items: the tint of a tier and the number in a corner.
/// </summary>
[TestClass]
public class IconPixelsTest
{
    private const int SIZE = 40;

    private static byte[] Opaque(byte r = 100, byte g = 100, byte b = 100, byte a = 255)
    {
        byte[] rgba = new byte[SIZE * SIZE * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = a;
        }
        return rgba;
    }

    [TestMethod]
    public void TheVanillaTiersKeepTheirColours()
    {
        IconPixels.TintFor(1).Should().Be((1f, 1f, 1f));
        IconPixels.TintFor(3).Should().Be((1f, 1f, 1f));
    }

    [TestMethod]
    public void EveryNewTierHasItsOwnTint()
    {
        IconPixels.TintFor(4).Should().NotBe(IconPixels.TintFor(5));
        IconPixels.TintFor(4).Should().NotBe(IconPixels.TintFor(3));
        IconPixels.TintFor(5).Should().NotBe(IconPixels.TintFor(3));
    }

    [TestMethod]
    public void TheTintMultipliesTheColourAndKeepsTheAlpha()
    {
        byte[] rgba = Opaque(200, 100, 50, 128);

        IconPixels.Tint(rgba, (0.5f, 1f, 2f));

        rgba[0].Should().Be(100);
        rgba[1].Should().Be(100);
        rgba[2].Should().Be(100);
        rgba[3].Should().Be(128);
    }

    [TestMethod]
    public void TheTintNeverLeavesTheByteRange()
    {
        byte[] rgba = Opaque(250, 250, 250);

        IconPixels.Tint(rgba, (2f, 2f, 2f));

        rgba[0].Should().Be(255);
    }

    [TestMethod]
    public void TransparentPixelsAreNotTinted()
    {
        byte[] rgba = Opaque(200, 200, 200, 0);

        IconPixels.Tint(rgba, (0.1f, 0.1f, 0.1f));

        rgba[0].Should().Be(200);
    }

    [TestMethod]
    public void ADigitIsDrawnIntoTheLowerRightCornerAsWhiteWithABlackShadow()
    {
        byte[] rgba = Opaque();

        IconPixels.DrawDigit(rgba, SIZE, SIZE, 4, 2).Should().BeTrue();

        // Digit 4 is 3 x 5 font pixels of 2 x 2 pixels, one font pixel of margin to the corner: x from 32 to 37, y from 2 to 11 (the texture's y grows upwards)
        int WhiteCount() => Enumerable.Range(0, SIZE * SIZE).Count(i => rgba[i * 4] == 255 && rgba[i * 4 + 1] == 255 && rgba[i * 4 + 2] == 255);
        int BlackCount() => Enumerable.Range(0, SIZE * SIZE).Count(i => rgba[i * 4] == 0 && rgba[i * 4 + 1] == 0 && rgba[i * 4 + 2] == 0);
        // The 4 has 9 lit font pixels (5,5,7,1,1 -> 2+2+3+1+1), 4 pixels each
        WhiteCount().Should().Be(9 * 4);
        BlackCount().Should().BeGreaterThan(0).And.BeLessThan(WhiteCount() + 40);
        // Nothing outside the corner changed
        rgba[(20 * SIZE + 5) * 4].Should().Be(100);
        rgba[(38 * SIZE + 38) * 4].Should().Be(100, "the top right corner is not touched");
    }

    [TestMethod]
    public void TheTopOfTheDigitIsAtTheHighestRowOfTheTexture()
    {
        byte[] rgba = Opaque();

        IconPixels.DrawDigit(rgba, SIZE, SIZE, 7, 2);

        // A 7 is a full bar on top: its highest row (y = 2 + 4 * 2 .. 11) is white over the whole width, its lowest row only in the right column
        int originX = SIZE - 3 * 2 - 2;
        for (int x = 0; x < 6; x++)
        {
            rgba[((2 + 4 * 2) * SIZE + originX + x) * 4].Should().Be(255, $"x {x} of the top bar");
        }
        rgba[(2 * SIZE + originX) * 4].Should().NotBe(255, "the left pixel of the bottom row of a 7 is not lit");
        rgba[(2 * SIZE + originX + 5) * 4].Should().Be(255);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(10)]
    public void ADigitOutOfRangeChangesNothing(int digit)
    {
        byte[] rgba = Opaque();
        byte[] before = (byte[])rgba.Clone();

        IconPixels.DrawDigit(rgba, SIZE, SIZE, digit, 2).Should().BeFalse();

        rgba.Should().Equal(before);
    }

    [TestMethod]
    public void ADigitThatDoesNotFitChangesNothing()
    {
        byte[] rgba = Opaque();
        byte[] before = (byte[])rgba.Clone();

        IconPixels.DrawDigit(rgba, SIZE, SIZE, 5, 20).Should().BeFalse();

        rgba.Should().Equal(before);
    }

    [TestMethod]
    [DataRow(0, 12)]
    [DataRow(1, 8)]
    [DataRow(2, 11)]
    [DataRow(3, 11)]
    [DataRow(4, 9)]
    [DataRow(5, 11)]
    [DataRow(6, 12)]
    [DataRow(7, 7)]
    [DataRow(8, 13)]
    [DataRow(9, 12)]
    public void EveryDigitDrawsItsLitFontPixelsInWhite(int digit, int litFontPixels)
    {
        byte[] rgba = Opaque();

        IconPixels.DrawDigit(rgba, SIZE, SIZE, digit, 2).Should().BeTrue();

        // A font pixel is 2 x 2 pixels at scale 2; the background is grey (100), so only the digit itself is white in all three channels
        int white = Enumerable.Range(0, SIZE * SIZE).Count(i => rgba[i * 4] == 255 && rgba[i * 4 + 1] == 255 && rgba[i * 4 + 2] == 255);
        white.Should().Be(litFontPixels * 4, $"digit {digit} has {litFontPixels} lit pixels in the 3 x 5 font");
    }

    [TestMethod]
    public void TheScaleGrowsWithTheIconAndIsAtLeastOne()
    {
        IconPixels.ScaleFor(10).Should().Be(1);
        IconPixels.ScaleFor(128).Should().Be(5);
        IconPixels.ScaleFor(256).Should().BeGreaterThan(IconPixels.ScaleFor(128));
    }
}
