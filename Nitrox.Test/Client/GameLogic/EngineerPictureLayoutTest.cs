using NitroxClient.GameLogic.Engineer;

namespace NitroxClient.GameLogic;

/// <summary>Where the bubbles, the heat bar and the fires and breaches are on the engineer's PDA screen.</summary>
[TestClass]
public class EngineerPictureLayoutTest
{
    // the boxes round the fire spawn points of the seven rooms in the Cyclops scene, in the hull's frame: room number, z from, z to, y from, y to
    private static readonly (int Room, float Z0, float Z1, float Y0, float Y1)[] SceneRooms =
    [
        (0, -20.6f, -16.2f, -1.0f, 0.9f),
        (1, -13.5f, -11.1f, -0.5f, 0.9f),
        (2, -9.4f, 0.8f, -0.7f, 0.9f),
        (3, 3.3f, 10.8f, -0.8f, 0.9f),
        (4, 0.9f, 10.8f, -4.8f, -3.3f),
        (5, -8.0f, -4.2f, -6.2f, -3.9f),
        (6, -17.1f, -10.8f, -6.1f, -3.9f)
    ];

    [TestMethod]
    public void TheBubbleSpotsAreTheOnesTheUserConfirmed()
    {
        EngineerPictureLayout.Bubbles.Should().HaveCount(7);
        (EngineerBubble bubble, string name, float x, float y) = EngineerPictureLayout.Bubbles[0];
        bubble.Should().Be(EngineerBubble.Shields);
        name.Should().Be("SHIELDS");
        x.Should().Be(0.118f);
        y.Should().Be(0.383f);
        EngineerPictureLayout.Bubbles.Single(spot => spot.Bubble == EngineerBubble.Systems).X.Should().Be(0.473f, "spot E, as chosen");
        EngineerPictureLayout.Bubbles.Single(spot => spot.Bubble == EngineerBubble.Vent).Y.Should().Be(0.589f);
        EngineerPictureLayout.Bubbles.Select(spot => spot.Bubble).Distinct().Should().HaveCount(7);
    }

    [TestMethod]
    public void EverySpotIsInsideThePictureAndNoTwoBubblesOverlapAtTheNominalSize()
    {
        float radius = EngineerPictureLayout.BUBBLE_DIAMETER / 2f;
        foreach ((EngineerBubble _, string name, float x, float y) in EngineerPictureLayout.Bubbles)
        {
            // in units of the picture's height, both ways
            (x * EngineerPictureLayout.PICTURE_ASPECT - radius).Should().BeGreaterThan(0f, name);
            (x * EngineerPictureLayout.PICTURE_ASPECT + radius).Should().BeLessThan(EngineerPictureLayout.PICTURE_ASPECT, name);
            (y - radius).Should().BeGreaterThan(0f, name);
            (y + radius + EngineerPictureLayout.LABEL_BELOW).Should().BeLessThan(1f, $"{name} and its label");
        }
        for (int i = 0; i < EngineerPictureLayout.Bubbles.Length; i++)
        {
            for (int j = i + 1; j < EngineerPictureLayout.Bubbles.Length; j++)
            {
                (EngineerBubble _, string nameA, float xa, float ya) = EngineerPictureLayout.Bubbles[i];
                (EngineerBubble _, string nameB, float xb, float yb) = EngineerPictureLayout.Bubbles[j];
                float dx = (xa - xb) * EngineerPictureLayout.PICTURE_ASPECT;
                float dy = ya - yb;
                Math.Sqrt(dx * dx + dy * dy).Should().BeGreaterThan(EngineerPictureLayout.BUBBLE_DIAMETER, $"{nameA} and {nameB}");
            }
        }
    }

    [TestMethod]
    public void ASpotOfThePictureIsPlacedFromTheMiddleOfItsRectangle()
    {
        EngineerPictureLayout.PanelPoint(0.5f, 0.5f, 1000f, 375f).Should().Be((0f, 0f));
        EngineerPictureLayout.PanelPoint(0f, 0f, 1000f, 375f).Should().Be((-500f, 187.5f), "the top left corner");
        EngineerPictureLayout.PanelPoint(1f, 1f, 1000f, 375f).Should().Be((500f, -187.5f), "the bottom right corner, y up");
        (float x, float y) = EngineerPictureLayout.PanelPoint(0.685f, 0.387f, 2046f, 768f);
        x.Should().BeApproximately(378.5f, 0.1f);
        y.Should().BeApproximately(86.8f, 0.1f);
    }

    [TestMethod]
    public void ThePictureFitsAnAreaWithItsOwnProportions()
    {
        (float width, float height) = EngineerPictureLayout.FitPicture(1000f, 1000f);
        width.Should().Be(1000f);
        height.Should().BeApproximately(1000f / EngineerPictureLayout.PICTURE_ASPECT, 0.01f);

        (width, height) = EngineerPictureLayout.FitPicture(1000f, 300f);
        height.Should().Be(300f, "the area is lower than the width allows");
        width.Should().BeApproximately(300f * EngineerPictureLayout.PICTURE_ASPECT, 0.01f);

        EngineerPictureLayout.FitPicture(-5f, 100f).Should().Be((0f, 0f));
    }

    [TestMethod]
    public void TheSevenRoomsOfTheSceneLandInsideTheirRectanglesOnThePicture()
    {
        const float TOLERANCE = 0.03f;
        EngineerPictureLayout.RoomCount.Should().Be(7);
        foreach ((int room, float z0, float z1, float y0, float y1) in SceneRooms)
        {
            (float x0, float x1, float top, float bottom) = EngineerPictureLayout.RoomRect(room);
            foreach (float z in new[] { z0, (z0 + z1) / 2f, z1 })
            {
                foreach (float y in new[] { y0, (y0 + y1) / 2f, y1 })
                {
                    (float px, float py) = EngineerPictureLayout.Project(z, y);
                    px.Should().BeInRange(x0 - TOLERANCE, x1 + TOLERANCE, $"room {room} at z {z}");
                    py.Should().BeInRange(top - TOLERANCE, bottom + TOLERANCE, $"room {room} at y {y}");
                }
            }
            EngineerPictureLayout.RoomOf((z0 + z1) / 2f, (y0 + y1) / 2f).Should().Be(room, "the middle of a room's fire spawn points belongs to that room");
        }
    }

    [TestMethod]
    public void ABreachOnTheSkinBelongsToTheNearestRoom()
    {
        EngineerPictureLayout.RoomOf(-18f, 5f).Should().Be(0, "above the helm");
        EngineerPictureLayout.RoomOf(7f, -7f).Should().Be(4, "under the lower engine room");
        EngineerPictureLayout.RoomOf(-5f, -7f).Should().Be(5, "under the lower vehicle bay");
        EngineerPictureLayout.RoomOf(6f, 3f).Should().Be(3, "above the engine room");
        EngineerPictureLayout.RoomRect(-1).Should().Be((0f, 1f, 0f, 1f), "not a room: the whole picture");
    }

    [TestMethod]
    public void TheHeatBarFillsFromZeroToOne()
    {
        EngineerPictureLayout.HeatFraction(0, 10).Should().Be(0f);
        EngineerPictureLayout.HeatFraction(5, 10).Should().Be(0.5f);
        EngineerPictureLayout.HeatFraction(12, 10).Should().Be(1f);
        EngineerPictureLayout.HeatFraction(-2, 10).Should().Be(0f);
        EngineerPictureLayout.HeatFraction(3, 0).Should().Be(0f);
        EngineerPictureLayout.HeatBar.X0.Should().BeLessThan(EngineerPictureLayout.HeatBar.X1);
        EngineerPictureLayout.HeatBar.Y0.Should().BeLessThan(EngineerPictureLayout.HeatBar.Y1);
    }
}
