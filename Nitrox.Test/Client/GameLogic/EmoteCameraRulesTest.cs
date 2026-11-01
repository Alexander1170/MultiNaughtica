using NitroxClient.GameLogic.PlayerModels;

namespace NitroxClient.GameLogic;

/// <summary>The numbers of the third-person camera that shows a player his own emote (docs/coop/PLAYER_MODELS.md, "The emote camera").</summary>
[TestClass]
public class EmoteCameraRulesTest
{
    [TestMethod]
    public void AnglesAreKeptInRange()
    {
        EmoteCameraRules.WrapDegrees(190f).Should().BeApproximately(-170f, 0.001f);
        EmoteCameraRules.WrapDegrees(-190f).Should().BeApproximately(170f, 0.001f);
        EmoteCameraRules.WrapDegrees(720f + 30f).Should().BeApproximately(30f, 0.001f);
        EmoteCameraRules.WrapDegrees(float.NaN).Should().Be(0f);
        EmoteCameraRules.ClampPitch(-200f).Should().Be(EmoteCameraRules.MIN_PITCH);
        EmoteCameraRules.ClampPitch(200f).Should().Be(EmoteCameraRules.MAX_PITCH);
        EmoteCameraRules.ClampPitch(10f).Should().Be(10f);
        EmoteCameraRules.ClampDistance(0.1f).Should().Be(EmoteCameraRules.MIN_DISTANCE);
        EmoteCameraRules.ClampDistance(50f).Should().Be(EmoteCameraRules.MAX_DISTANCE);
        EmoteCameraRules.DEFAULT_DISTANCE.Should().BeInRange(EmoteCameraRules.MIN_DISTANCE, EmoteCameraRules.MAX_DISTANCE);
    }

    [TestMethod]
    public void YawZeroLooksAlongZAndTurnsTowardsX()
    {
        EmoteCameraRules.YawOf(0f, 1f).Should().BeApproximately(0f, 0.001f);
        EmoteCameraRules.YawOf(1f, 0f).Should().BeApproximately(90f, 0.001f);
        EmoteCameraRules.YawOf(0f, -1f).Should().BeApproximately(180f, 0.001f);
        EmoteCameraRules.YawOf(-1f, 0f).Should().BeApproximately(-90f, 0.001f);
        EmoteCameraRules.YawOf(0f, 0f).Should().Be(0f, "no direction is no turn");

        (float x, float y, float z) = EmoteCameraRules.Direction(0f, 0f);
        (x, y, z).Should().Be((0f, 0f, 1f));
        (x, y, z) = EmoteCameraRules.Direction(90f, 0f);
        x.Should().BeApproximately(1f, 0.001f);
        z.Should().BeApproximately(0f, 0.001f);
        (_, y, _) = EmoteCameraRules.Direction(0f, 30f);
        y.Should().BeApproximately(0.5f, 0.001f, "looking up 30 degrees");
        (x, y, z) = EmoteCameraRules.Direction(123f, -40f);
        Math.Sqrt(x * x + y * y + z * z).Should().BeApproximately(1.0, 0.0001, "a direction is a unit vector");
    }

    [TestMethod]
    public void TheCameraStartsInFrontOfTheCharacterLookingBackAtHim()
    {
        // the character faces +Z (yaw 0): the camera is in front of him, on +Z, and looks back along -Z, turned a little to one side
        float yaw = EmoteCameraRules.StartYaw(0f);
        yaw.Should().BeApproximately(180f - EmoteCameraRules.START_SIDE_DEGREES, 0.001f);
        (float x, float y, float z) = EmoteCameraRules.Direction(yaw, EmoteCameraRules.START_PITCH);
        z.Should().BeLessThan(0f, "the camera looks towards the character, back along the way he faces");
        y.Should().BeLessThan(0f, "and down a little");
        // the camera stands at pivot - direction * distance: in front of the character (+Z) and a little above him
        (-z).Should().BeGreaterThan(0.7f);
        (-y).Should().BeGreaterThan(0f);

        // whatever way he faces, the camera looks (within the side turn) the way that is opposite
        foreach (float facing in new[] { 0f, 45f, 90f, -135f, 179f })
        {
            float start = EmoteCameraRules.StartYaw(facing);
            Math.Abs(EmoteCameraRules.WrapDegrees(start - facing - 180f)).Should().BeApproximately(EmoteCameraRules.START_SIDE_DEGREES, 0.01f, $"facing {facing}");
        }
    }

    [TestMethod]
    public void AFriendDecidesWhichSideTheCameraStartsOn()
    {
        // the friend is east (+X) of the character: the camera stands east of him and looks west, along -X (yaw -90)
        EmoteCameraRules.TryYawFromFriend(0f, 0f, 10f, 0f, out float yaw).Should().BeTrue();
        yaw.Should().BeApproximately(-90f, 0.001f);
        (float x, _, float z) = EmoteCameraRules.Direction(yaw, 0f);
        x.Should().BeApproximately(-1f, 0.001f);
        z.Should().BeApproximately(0f, 0.001f);

        EmoteCameraRules.TryYawFromFriend(0f, 0f, 0f, -4f, out yaw).Should().BeTrue();
        yaw.Should().BeApproximately(0f, 0.001f, "the friend is south (-Z): the camera is south and looks north");
        EmoteCameraRules.TryYawFromFriend(3f, 3f, 3.2f, 3f, out _).Should().BeFalse("a friend on top of him says nothing");
    }

    [TestMethod]
    public void AWallPullsTheCameraInAndItDriftsBackOut()
    {
        EmoteCameraRules.AllowedDistance(2.8f, false, 0f).Should().Be(2.8f);
        EmoteCameraRules.AllowedDistance(2.8f, true, 5f).Should().BeApproximately(2.8f, 0.0001f, "a wall further away than the camera does not matter");
        EmoteCameraRules.AllowedDistance(2.8f, true, 1.5f).Should().BeApproximately(1.5f - EmoteCameraRules.WALL_SKIN, 0.0001f);
        EmoteCameraRules.AllowedDistance(2.8f, true, 0.2f).Should().BeApproximately(0.2f - EmoteCameraRules.WALL_SKIN, 0.0001f, "a wall 0.2 m away keeps the camera in front of it, nearer than any minimum that would put it beyond the wall");
        EmoteCameraRules.AllowedDistance(2.8f, true, 0.07f).Should().BeApproximately(EmoteCameraRules.FLOOR_DISTANCE, 0.0001f, "a wall right at his chest puts the camera in his chest, the floor");
        EmoteCameraRules.AllowedDistance(0.1f, true, 0.05f).Should().BeGreaterThanOrEqualTo(EmoteCameraRules.FLOOR_DISTANCE, "the camera is never on the character's chest");
        EmoteCameraRules.AllowedDistance(9f, false, 0f).Should().Be(EmoteCameraRules.MAX_DISTANCE, "the wanted distance is clamped first");

        EmoteCameraRules.EasedDistance(2.8f, 1.2f, 0.016f).Should().Be(1.2f, "in at once");
        float out1 = EmoteCameraRules.EasedDistance(1.2f, 2.8f, 0.1f);
        out1.Should().BeApproximately(1.2f + EmoteCameraRules.EASE_OUT_PER_SECOND * 0.1f, 0.0001f);
        EmoteCameraRules.EasedDistance(2.7f, 2.8f, 1f).Should().Be(2.8f, "it does not overshoot");
        EmoteCameraRules.EasedDistance(1f, 3f, -1f).Should().Be(1f, "a negative frame time does not move it");
    }

    [TestMethod]
    public void TheCameraStartsOnASideThatHasRoom()
    {
        // open water: the wanted side is taken as it is
        EmoteCameraRules.PickStartYaw(152f, _ => 2.8f, 2.8f, out float room).Should().BeApproximately(152f, 0.001f);
        room.Should().Be(2.8f);

        // he stands at a console with his face to the wall: the wanted side and the 50 degrees beside it have 0.5 m of room, up to 100 degrees from it there is 1.4 m, the rest is free: the first turn with room is taken
        float Room(float yaw)
        {
            float apart = Math.Abs(EmoteCameraRules.WrapDegrees(yaw - 152f));
            return apart < 55f ? 0.5f : apart < 100f ? 1.4f : 3f;
        }
        float picked = EmoteCameraRules.PickStartYaw(152f, Room, 2.8f, out room);
        picked.Should().BeApproximately(EmoteCameraRules.WrapDegrees(152f + 120f), 0.001f, "the nearest turn with enough room");
        room.Should().Be(3f);

        // the nearest side with room is taken, not the opposite one: the wanted side is blocked, 30 degrees to one side has room
        EmoteCameraRules.PickStartYaw(0f, yaw => Math.Abs(yaw - 30f) < 0.01f ? 2.5f : 0.4f, 2.8f, out room).Should().BeApproximately(30f, 0.001f);
        room.Should().Be(2.5f);

        // a tight room with no side that has enough room: the side with the most
        EmoteCameraRules.PickStartYaw(0f, yaw => Math.Abs(yaw + 90f) < 0.01f ? 1.2f : 0.6f, 2.8f, out room).Should().BeApproximately(-90f, 0.001f);
        room.Should().Be(1.2f);

        // a wanted distance that is shorter than the comfort is the measure
        EmoteCameraRules.PickStartYaw(0f, _ => 0.9f, 0.9f, out _).Should().Be(0f, "0.9 m of room is all that he asked for");
        EmoteCameraRules.START_OFFSETS.Should().StartWith(0f).And.OnlyHaveUniqueItems();
        EmoteCameraRules.START_OFFSETS.Should().Contain(180f);
    }

    [TestMethod]
    public void InACorridorTheCameraLooksAlongItInsteadOfFromInsideTheWall()
    {
        // his shoulder is to the wall of a corridor: along yaw 0 (within 20 degrees) there are 3 m of room, in every other direction 0.5 m
        float Room(float yaw) => Math.Abs(EmoteCameraRules.WrapDegrees(yaw)) < 20f ? 3f : 0.5f;

        // the mouse points along the corridor: nothing changes
        EmoteCameraRules.PickViewYaw(10f, Room, 2.8f, out float room).Should().BeApproximately(10f, 0.001f);
        room.Should().Be(3f);

        // the mouse points at the wall (0.5 m of room, less than the tight distance): the camera looks along the corridor
        float view = EmoteCameraRules.PickViewYaw(180f, Room, 2.8f, out room);
        Math.Abs(EmoteCameraRules.WrapDegrees(view)).Should().BeLessThan(20f);
        room.Should().Be(3f);

        // a side with 1.5 m of room is not tight: the camera stays where the mouse points, pulled in by the wall as before
        EmoteCameraRules.PickViewYaw(90f, _ => 1.5f, 2.8f, out room).Should().BeApproximately(90f, 0.001f);
        room.Should().Be(1.5f);
        // under the tight distance but a wanted distance that is shorter than it: the wanted distance is the measure
        EmoteCameraRules.PickViewYaw(90f, _ => 0.7f, 0.7f, out _).Should().BeApproximately(90f, 0.001f);
        // a closet: nothing has room, the side with the most
        EmoteCameraRules.PickViewYaw(0f, yaw => Math.Abs(yaw - 60f) < 0.01f ? 0.9f : 0.3f, 2.8f, out room).Should().BeApproximately(60f, 0.001f);
        room.Should().Be(0.9f);
    }

    [TestMethod]
    public void TheCameraSlidesToASideWithRoomAtALimitedSpeedAndTheShortestWay()
    {
        // from 0 towards 90: a tenth of a second is 27 degrees
        EmoteCameraRules.SlideToward(0f, 90f, 0.1f).Should().BeApproximately(EmoteCameraRules.SLIDE_DEGREES_PER_SECOND * 0.1f, 0.001f);
        // it arrives and does not overshoot
        EmoteCameraRules.SlideToward(80f, 90f, 1f).Should().BeApproximately(90f, 0.001f);
        EmoteCameraRules.SlideToward(0f, 90f, 10f).Should().BeApproximately(90f, 0.001f);
        // the shortest way: from 170 to -170 is 20 degrees up through 180, not 340 down
        EmoteCameraRules.SlideToward(170f, -170f, 0.01f).Should().BeApproximately(170f + EmoteCameraRules.SLIDE_DEGREES_PER_SECOND * 0.01f, 0.001f);
        EmoteCameraRules.SlideToward(170f, -170f, 1f).Should().BeApproximately(-170f, 0.001f);
        EmoteCameraRules.SlideToward(-170f, 170f, 0.01f).Should().BeApproximately(-170f - EmoteCameraRules.SLIDE_DEGREES_PER_SECOND * 0.01f, 0.001f);
        // no time, no slide; a negative time does not slide backwards
        EmoteCameraRules.SlideToward(10f, 90f, 0f).Should().Be(10f);
        EmoteCameraRules.SlideToward(10f, 90f, -1f).Should().Be(10f);
        // a slide that is already there stays
        EmoteCameraRules.SlideToward(45f, 45f, 0.5f).Should().BeApproximately(45f, 0.001f);
        EmoteCameraRules.SLIDE_DEGREES_PER_SECOND.Should().BeGreaterThan(90f, "a half turn takes about a second at most");
    }

    [TestMethod]
    public void TheNameAndStatsOfARemotePlayerFaceTheCameraThatDrawsThem()
    {
        (float X, float Y, float Z) head = (0f, 0f, 1f);
        (float X, float Y, float Z) emote = (0f, 0f, -1f);

        // the head's camera looks along +Z and the emote camera the other way (the live check of 2026-10-30: the tag was mirrored): while the emote camera is out the tag turns to it
        EmoteCameraRules.BillboardForward(true, head, emote).Should().Be(emote);
        // without the emote camera the head's camera is the one that draws
        EmoteCameraRules.BillboardForward(false, head, emote).Should().Be(head);
        // the emote camera is out but has not drawn a frame yet (no direction): the head's camera
        EmoteCameraRules.BillboardForward(true, head, (0f, 0f, 0f)).Should().Be(head);
        EmoteCameraRules.BillboardForward(true, head, (float.NaN, 0f, 1f)).Should().Be(head);
        EmoteCameraRules.BillboardForward(true, head, (float.PositiveInfinity, 0f, 0f)).Should().Be(head);
        // a direction that is not a unit vector is still a direction
        EmoteCameraRules.BillboardForward(true, head, (0.2f, 0f, 0f)).Should().Be((0.2f, 0f, 0f));
    }

    [TestMethod]
    public void TheCameraStaysOnTheSideOfTheWaterThatTheCharacterIsOn()
    {
        // a swimmer 3 m under the surface: the camera may not rise above it
        EmoteCameraRules.KeepOnWaterSide(1f, -3f, 0f, true).Should().Be(-EmoteCameraRules.WATER_MARGIN);
        EmoteCameraRules.KeepOnWaterSide(-2f, -3f, 0f, true).Should().Be(-2f, "already below it");
        // somebody standing on the surface or on a beach: the camera may not dip under it
        EmoteCameraRules.KeepOnWaterSide(-1f, 1.2f, 0f, true).Should().Be(EmoteCameraRules.WATER_MARGIN);
        EmoteCameraRules.KeepOnWaterSide(2f, 1.2f, 0f, true).Should().Be(2f);
        // inside a base or a Cyclops the surface is not a thing
        EmoteCameraRules.KeepOnWaterSide(-1f, 1.2f, 0f, false).Should().Be(-1f);
        // a different ocean level (the game can change it)
        EmoteCameraRules.KeepOnWaterSide(-8f, -10f, -5f, true).Should().Be(-8f);
        EmoteCameraRules.KeepOnWaterSide(0f, -10f, -5f, true).Should().Be(-5f - EmoteCameraRules.WATER_MARGIN);
    }
}
