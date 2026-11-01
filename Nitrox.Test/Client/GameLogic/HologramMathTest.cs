using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.GameLogic;

/// <summary>The mapping between the real world and the hologram, with depth, the ray tests that pick a place on its terrain, and the gate that keeps it from starting during a join.</summary>
[TestClass]
public class HologramMathTest
{
    private static readonly (float X, float Y, float Z) Pivot = (-121f, -14f, 505f);
    private static readonly (float X, float Y, float Z) Anchor = (-0.15f, 0.7f, -13.35f);

    /// <summary>The turn of the navigator's hologram as the sonar sets it: the frame is turned towards the viewer by <paramref name="frameYaw" /> and tilted, and the map's own turn takes the frame's out again.</summary>
    private static Rot3 TurnOf(float frameYaw, float tilt, float userYaw)
    {
        return Rot3.AboutY(frameYaw).After(Rot3.AboutZ(-tilt)).After(Rot3.AboutY(userYaw - frameYaw));
    }

    [TestMethod]
    public void APlaceInTheWorldGoesToTheHologramAndBackWhateverTheTurnAndTheSize()
    {
        foreach ((float yaw, float tilt, float scale) in new[] { (0f, 58f, 0.007f), (90f, 30f, 0.0125f), (-90f, 15f, 0.0046f), (137f, 90f, 0.02f) })
        {
            Rot3 turn = TurnOf(yaw, tilt, 0f);
            foreach ((float X, float Y, float Z) world in new[] { (-121f, -14f, 505f), (-61f, -14f, 560f), (-90f, -163f, 470f), (-150f, 12f, 420f) })
            {
                (float X, float Y, float Z) map = HologramMath.ToMap(world, Pivot, Anchor, turn, scale);
                (float X, float Y, float Z) back = HologramMath.ToWorld(map, Pivot, Anchor, turn, scale);

                back.X.Should().BeApproximately(world.X, 0.01f);
                back.Y.Should().BeApproximately(world.Y, 0.01f);
                back.Z.Should().BeApproximately(world.Z, 0.01f);
            }
        }
    }

    [TestMethod]
    public void TheHullIsAtTheMiddleOfTheHologramAndTheDeeperAPlaceTheFartherBelowItsPlane()
    {
        const float SCALE = 0.007f;
        Rot3 turn = TurnOf(-90f, 58f, 0f);
        (float X, float Y, float Z) up = turn.Apply((0f, 1f, 0f));

        HologramMath.ToMap(Pivot, Pivot, Anchor, turn, SCALE).Should().Be(Anchor, "the Cyclops is at the middle of its own hologram");
        foreach (float depthBelowHull in new[] { 20f, 100f, 163f })
        {
            (float X, float Y, float Z) deeper = HologramMath.ToMap((Pivot.X, Pivot.Y - depthBelowHull, Pivot.Z), Pivot, Anchor, turn, SCALE);
            float along = (deeper.X - Anchor.X) * up.X + (deeper.Y - Anchor.Y) * up.Y + (deeper.Z - Anchor.Z) * up.Z;

            along.Should().BeApproximately(-depthBelowHull * SCALE, 1e-4f, "below the hologram's flat reference plane by the depth difference, scaled");
        }
        (float X, float Y, float Z) shallower = HologramMath.ToMap((Pivot.X, Pivot.Y + 10f, Pivot.Z), Pivot, Anchor, turn, SCALE);
        ((shallower.X - Anchor.X) * up.X + (shallower.Y - Anchor.Y) * up.Y + (shallower.Z - Anchor.Z) * up.Z).Should().BeApproximately(10f * SCALE, 1e-4f, "above the plane");
    }

    [TestMethod]
    public void DepthGrowsDownwardsFromTheSeaSurface()
    {
        HologramMath.DepthOf(0f).Should().Be(0f);
        HologramMath.DepthOf(-163f).Should().Be(163f);
        HologramMath.DepthOf(12f).Should().Be(-12f);
    }

    [TestMethod]
    public void AClickOnTheTerrainOfTheHologramMakesAPlaceAtTheDepthOfTheTerrainAndNotAtTheCyclopsDepth()
    {
        const float SCALE = 0.007f;
        const float SEA_FLOOR = -163f;
        Rot3 turn = TurnOf(-90f, 40f, 0f);
        (float X, float Y, float Z) target = (-61f, SEA_FLOOR, 540f);

        // the terrain is a flat triangle pair 163 m down, as the hologram draws it (its vertices go through the same mapping)
        (float X, float Y, float Z) Corner(float x, float z) => HologramMath.ToMap((x, SEA_FLOOR, z), Pivot, Anchor, turn, SCALE);
        (float X, float Y, float Z) a = Corner(-300f, 300f);
        (float X, float Y, float Z) b = Corner(300f, 300f);
        (float X, float Y, float Z) c = Corner(300f, 800f);
        (float X, float Y, float Z) d = Corner(-300f, 800f);

        // the player's eye is above and in front of the hologram and looks at the picture of the target
        (float X, float Y, float Z) eye = (Anchor.X + 1.2f, Anchor.Y + 0.9f, Anchor.Z);
        (float X, float Y, float Z) picture = HologramMath.ToMap(target, Pivot, Anchor, turn, SCALE);
        (float X, float Y, float Z) direction = (picture.X - eye.X, picture.Y - eye.Y, picture.Z - eye.Z);

        bool hit = HologramMath.RayTriangle(eye, direction, a, b, c, out float t) || HologramMath.RayTriangle(eye, direction, a, c, d, out t);
        hit.Should().BeTrue("the ray from the eye to the picture of the target crosses the picture of the sea floor");
        (float X, float Y, float Z) onRelief = (eye.X + direction.X * t, eye.Y + direction.Y * t, eye.Z + direction.Z * t);
        (float X, float Y, float Z) world = HologramMath.ToWorld(onRelief, Pivot, Anchor, turn, SCALE);

        world.Y.Should().BeApproximately(SEA_FLOOR, 0.05f, "the place is at the depth of the terrain that was clicked");
        HologramMath.DepthOf(world.Y).Should().BeApproximately(163f, 0.05f);
        world.X.Should().BeApproximately(target.X, 0.05f);
        world.Z.Should().BeApproximately(target.Z, 0.05f);
        world.Y.Should().NotBeApproximately(Pivot.Y, 100f, "not the Cyclops' own depth, which is what the flat plane gives");
    }

    [TestMethod]
    public void ARayMeetsATriangleFromEitherSideAndNotBehindOrBesideIt()
    {
        (float, float, float) a = (-1f, -1f, 5f);
        (float, float, float) b = (1f, -1f, 5f);
        (float, float, float) c = (0f, 1f, 5f);

        HologramMath.RayTriangle((0f, 0f, 0f), (0f, 0f, 1f), a, b, c, out float t).Should().BeTrue();
        t.Should().BeApproximately(5f, 1e-4f);
        HologramMath.RayTriangle((0f, 0f, 10f), (0f, 0f, -1f), a, b, c, out t).Should().BeTrue("the back of it");
        t.Should().BeApproximately(5f, 1e-4f);
        HologramMath.RayTriangle((0f, 0f, 0f), (0f, 0f, 2f), a, b, c, out t).Should().BeTrue();
        t.Should().BeApproximately(2.5f, 1e-4f, "in units of the direction's length");
        HologramMath.RayTriangle((0f, 0f, 10f), (0f, 0f, 1f), a, b, c, out _).Should().BeFalse("it is behind the origin");
        HologramMath.RayTriangle((3f, 0f, 0f), (0f, 0f, 1f), a, b, c, out _).Should().BeFalse("beside it");
        HologramMath.RayTriangle((0f, 0f, 0f), (1f, 0f, 0f), a, b, c, out _).Should().BeFalse("parallel to it");
        HologramMath.RayTriangle((0f, 0f, 0f), (0f, 0f, 1f), a, a, a, out _).Should().BeFalse("a degenerate triangle");
    }

    [TestMethod]
    public void ARayMeetsABoxWhereItEntersItAndZeroFromInside()
    {
        (float, float, float) min = (-1f, -1f, 2f);
        (float, float, float) max = (1f, 1f, 4f);

        HologramMath.RayBox((0f, 0f, 0f), (0f, 0f, 1f), min, max, out float t).Should().BeTrue();
        t.Should().BeApproximately(2f, 1e-4f);
        HologramMath.RayBox((0f, 0f, 3f), (0f, 0f, 1f), min, max, out t).Should().BeTrue("from inside");
        t.Should().Be(0f);
        HologramMath.RayBox((5f, 0f, 0f), (0f, 0f, 1f), min, max, out _).Should().BeFalse("beside it");
        HologramMath.RayBox((0f, 0f, 9f), (0f, 0f, 1f), min, max, out _).Should().BeFalse("behind the origin");
        HologramMath.RayBox((0f, 0f, 0f), (1f, 0f, 1f), min, max, out _).Should().BeFalse("passes beside it");
    }

    [TestMethod]
    public void ARayMeetsASphereWhereItIsInsideItAndNotBehindTheOrigin()
    {
        // (the hologram shows terrain inside a sphere only: a pick takes the triangles that lie in this part of the ray)
        HologramMath.RaySphere((0f, 0f, -5f), (0f, 0f, 1f), (0f, 0f, 0f), 2f, out float entry, out float exit).Should().BeTrue();
        entry.Should().BeApproximately(3f, 1e-4f);
        exit.Should().BeApproximately(7f, 1e-4f);
        HologramMath.RaySphere((0f, 0f, 0f), (0f, 0f, 2f), (0f, 0f, 0f), 2f, out entry, out exit).Should().BeTrue("from inside");
        entry.Should().Be(0f);
        exit.Should().BeApproximately(1f, 1e-4f, "in units of the direction's length");
        HologramMath.RaySphere((0f, 3f, -5f), (0f, 0f, 1f), (0f, 0f, 0f), 2f, out _, out _).Should().BeFalse("passes beside it");
        HologramMath.RaySphere((0f, 0f, 5f), (0f, 0f, 1f), (0f, 0f, 0f), 2f, out _, out _).Should().BeFalse("behind the origin");
        HologramMath.RaySphere((0f, 0f, -5f), (0f, 0f, 0f), (0f, 0f, 0f), 2f, out _, out _).Should().BeFalse("no direction");
    }

    [TestMethod]
    public void AChunkMeshIsNamedAfterTheCubeOfTheWorldItCovers()
    {
        (float X, float Y, float Z) land = (-2048f, -3040f, -2048f);

        // two picks of the play-test of Build 3c, near enough to the middle of the map to have been taken back to the world rightly: the cube is the one in the mesh's name
        HologramMath.ChunkOf((-38.1f, -66.8f, 672.9f), land, out float edge).Should().Be((15, 23, 21));
        edge.Should().BeGreaterThan(3f);
        HologramMath.ChunkOf((-92.6f, -154.5f, 665.3f), land, out _).Should().Be((15, 22, 21));
        HologramMath.ChunkOf((-2048f, -3040f, -2048f), land, out float corner).Should().Be((0, 0, 0));
        corner.Should().Be(0f);
        HologramMath.ChunkOf((-1921f, -2913f, -1921f), land, out float inner).Should().Be((0, 0, 0), "127 m in");
        inner.Should().BeApproximately(1f, 1e-3f, "1 m from the faces it is nearest to");
    }

    [TestMethod]
    public void TheCubeIsReadFromAChunkMeshsName()
    {
        HologramMath.TryParseChunkName("Chunk-14-23-20", out (int X, int Y, int Z) chunk).Should().BeTrue();
        chunk.Should().Be((14, 23, 20));
        HologramMath.TryParseChunkName("Chunk-14-23", out _).Should().BeFalse();
        HologramMath.TryParseChunkName("Mesh-1-2-3", out _).Should().BeFalse();
        HologramMath.TryParseChunkName("Chunk-a-2-3", out _).Should().BeFalse();
        HologramMath.TryParseChunkName(null, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TheHologramWaitsForTheJoinTheLoadingScreenAndTheWorldToSettle()
    {
        HologramStartRules.WaitingFor(true, true, false, false, true).Should().BeNull("everything is done");
        HologramStartRules.WaitingFor(false, true, false, false, true).Should().Contain("no session");
        HologramStartRules.WaitingFor(true, false, false, false, true).Should().Contain("join");
        HologramStartRules.WaitingFor(true, true, true, false, true).Should().Contain("intro");
        HologramStartRules.WaitingFor(true, true, false, true, true).Should().Contain("loading screen");
        HologramStartRules.WaitingFor(true, true, false, false, false).Should().Contain("settle");
        HologramStartRules.WaitingFor(true, false, true, true, false).Should().Contain("join", "the first thing that is missing is the one that is named");
    }

    /// <summary>The turn of a hull as its heading, pitch and roll (the order the game's Euler angles use is not needed here: any turn will do).</summary>
    private static Rot3 HullTurn(float yaw, float pitch, float roll)
    {
        return Rot3.AboutY(yaw).After(Rot3.AboutX(pitch)).After(Rot3.AboutZ(roll));
    }

    /// <summary>The turn that the aligner builds, <c>anchor.rotation * View * Inverse(pivot.rotation)</c>: the frame is a child of the hull, so its world turn is the hull's and then its own yaw and tilt.</summary>
    private static Rot3 AlignerTurn(Rot3 hull, float frameYaw, float tilt, float userYaw)
    {
        Rot3 frame = hull.After(Rot3.AboutY(frameYaw)).After(Rot3.AboutZ(-tilt));
        return frame.After(Rot3.AboutY(userYaw - frameYaw)).After(hull.Transposed);
    }

    [TestMethod]
    public void APickOnSlopingTerrainComesBackAsTheSamePlaceWhateverTheHullsTurnTheTiltAndTheSizeOfTheHologram()
    {
        foreach ((float yaw, float pitch, float roll) in new[] { (0f, 0f, 0f), (73f, 8f, -5f), (-140f, -12f, 9f), (200f, 0f, 20f) })
        {
            foreach ((float frameYaw, float tilt, float userYaw, float scale) in new[] { (-90f, 58f, 0f, 0.007f), (35f, 20f, 70f, 0.0125f), (170f, 85f, -40f, 0.0046f) })
            {
                Rot3 turn = AlignerTurn(HullTurn(yaw, pitch, roll), frameYaw, tilt, userYaw);

                // a sea floor that slopes both ways, as two triangles whose corners went through the same mapping as the hologram's vertices
                static (float X, float Y, float Z) Floor(float x, float z) => (x, -150f + 0.2f * (x - Pivot.X) - 0.1f * (z - Pivot.Z), z);
                (float X, float Y, float Z) Corner(float x, float z) => HologramMath.ToMap(Floor(x, z), Pivot, Anchor, turn, scale);
                (float X, float Y, float Z) a = Corner(-400f, 300f);
                (float X, float Y, float Z) b = Corner(200f, 300f);
                (float X, float Y, float Z) c = Corner(200f, 800f);
                (float X, float Y, float Z) d = Corner(-400f, 800f);

                foreach ((float x, float z) in new[] { (-121f, 505f), (-61f, 560f), (-180f, 470f), (-10f, 700f) })
                {
                    (float X, float Y, float Z) target = Floor(x, z);
                    (float X, float Y, float Z) picture = HologramMath.ToMap(target, Pivot, Anchor, turn, scale);
                    // an eye a little way off, looking at the picture of the target: the ray goes through it and on
                    (float X, float Y, float Z) eye = (picture.X + 0.7f, picture.Y + 0.9f, picture.Z - 0.4f);
                    (float X, float Y, float Z) direction = (picture.X - eye.X, picture.Y - eye.Y, picture.Z - eye.Z);

                    bool hit = HologramMath.RayTriangle(eye, direction, a, b, c, out float t) || HologramMath.RayTriangle(eye, direction, a, c, d, out t);
                    hit.Should().BeTrue($"the ray meets the picture of the floor (hull {yaw}/{pitch}/{roll}, frame {frameYaw}/{tilt}/{userYaw}, scale {scale})");
                    (float X, float Y, float Z) onRelief = (eye.X + direction.X * t, eye.Y + direction.Y * t, eye.Z + direction.Z * t);
                    (float X, float Y, float Z) world = HologramMath.ToWorld(onRelief, Pivot, Anchor, turn, scale);

                    world.X.Should().BeApproximately(target.X, 0.1f);
                    world.Z.Should().BeApproximately(target.Z, 0.1f);
                    world.Y.Should().BeApproximately(target.Y, 0.1f, "the depth of the floor at that place, not the Cyclops' own");
                }
            }
        }
    }

    [TestMethod]
    public void TheMapKeepsTheHullsDirectionsWhateverTheHeadingPitchAndRollOfTheHull()
    {
        const float SCALE = 0.007f;
        // a place 100 m ahead of the hull, 30 m to the pilot's right (x is negative there) and 12 m below it, in the hull's own frame
        (float X, float Y, float Z) local = (-30f, -12f, 100f);
        (float X, float Y, float Z)? reference = null;
        foreach ((float yaw, float pitch, float roll) in new[] { (0f, 0f, 0f), (73f, 8f, -5f), (-140f, -12f, 9f), (200f, 0f, 20f) })
        {
            Rot3 hull = HullTurn(yaw, pitch, roll);
            Rot3 turn = AlignerTurn(hull, 25f, 40f, 25f);
            (float X, float Y, float Z) offset = hull.Apply(local);
            (float X, float Y, float Z) world = (Pivot.X + offset.X, Pivot.Y + offset.Y, Pivot.Z + offset.Z);

            HologramMath.ToMap(Pivot, Pivot, Anchor, turn, SCALE).Should().Be(Anchor, "the hull is at the middle of the hologram at any heading");
            (float X, float Y, float Z) map = HologramMath.ToMap(world, Pivot, Anchor, turn, SCALE);
            // the hologram hangs on the hull and turns with it: its offsets are the same in the hull's frame
            (float X, float Y, float Z) inHull = hull.ApplyInverse((map.X - Anchor.X, map.Y - Anchor.Y, map.Z - Anchor.Z));
            reference ??= inHull;

            inHull.X.Should().BeApproximately(reference.Value.X, 1e-3f);
            inHull.Y.Should().BeApproximately(reference.Value.Y, 1e-3f);
            inHull.Z.Should().BeApproximately(reference.Value.Z, 1e-3f);
        }
    }

    [TestMethod]
    public void TheHelmAimsAtMostTwentyMetresAboveARoutePointAndNeverAtTheSurfaceJustBecauseTheWaterIsShallow()
    {
        RouteClearance.CLEARANCE_METRES.Should().Be(20f);
        RouteClearance.AimClearance(143f).Should().Be(20f);
        RouteClearance.AimClearance(40f).Should().Be(20f);
        RouteClearance.AimClearance(12f).Should().Be(6f);
        RouteClearance.AimClearance(0.5f).Should().Be(1f);

        RouteClearance.AimY(-143f).Should().Be(-123f, "20 m above a floor 143 m deep");
        RouteClearance.AimY(-12f).Should().Be(-6f, "half the depth where the water is shallow, not the surface");
        RouteClearance.AimY(-2f).Should().Be(-1f);
        RouteClearance.AimY(-0.5f).Should().Be(0f, "less than a metre of water is the surface");
        RouteClearance.AimY(12f).Should().Be(0f, "a floor above the surface (an island): the surface");
        RouteClearance.AimY(float.NaN).Should().Be(float.NaN);

        RouteClearance.Describe(-163f).Should().Be("floor at 163 m, the helm aims 20 m above it at 143 m");
        RouteClearance.Describe(-0.5f).Should().Contain("surface");
    }

    [TestMethod]
    public void ARoutePointAtTheCyclopsOwnDepthIsStoredSoThatTheHelmAimsAtThatDepth()
    {
        foreach (float aim in new[] { 0f, -0.4f, -1f, -1.5f, -7f, -20f, -35f, -100f, -250f })
        {
            float stored = RouteClearance.PinYFor(aim);

            RouteClearance.AimY(stored).Should().BeApproximately(aim, 1e-3f, "the helm goes to the depth that was clicked");
            stored.Should().BeLessThanOrEqualTo(aim, "the pin is the lower one");
        }
        RouteClearance.PinYFor(5f).Should().Be(RouteClearance.PinYFor(0f), "above the surface is the surface");
        RouteClearance.PinYFor(float.NaN).Should().Be(float.NaN);
    }
}
