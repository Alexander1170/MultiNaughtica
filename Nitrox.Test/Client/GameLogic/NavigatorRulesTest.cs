using NitroxClient.GameLogic.Navigator;

namespace NitroxClient.GameLogic;

/// <summary>The pure parts of the navigator console (docs/coop/VEHICLE_UPGRADES.md): the view that the mouse gives the map, the hologram in the build room and its size, and how contacts are written and clamped.</summary>
[TestClass]
public class NavigatorRulesTest
{
    // ---- the map view

    [TestMethod]
    public void TheMapStartsAsTheWorldLiesAndTheWholeRangeInTheDisc()
    {
        NavigatorMapView view = new();

        view.Yaw.Should().Be(NavigatorMapView.DEFAULT_YAW);
        view.Tilt.Should().Be(NavigatorMapView.DEFAULT_TILT);
        view.Zoom.Should().Be(1f);
        view.VisibleRadius(200f).Should().Be(200f);
    }

    [TestMethod]
    public void DraggingAcrossTurnsTheMapAndWrapsAtHalfATurn()
    {
        NavigatorMapView view = new();

        view.Drag(100f, 0f);
        view.Yaw.Should().BeApproximately(NavigatorMapView.DEFAULT_YAW + 100f * NavigatorMapView.YAW_PER_PIXEL, 0.001f);

        view.Drag(-10000f, 0f);
        view.Yaw.Should().BeInRange(-180f, 180f);
        NavigatorMapView.NormalizeYaw(190f).Should().BeApproximately(-170f, 0.001f);
        NavigatorMapView.NormalizeYaw(-190f).Should().BeApproximately(170f, 0.001f);
        NavigatorMapView.NormalizeYaw(540f).Should().BeApproximately(180f, 0.001f);
    }

    [TestMethod]
    public void TheMapSpinsRightRoundAsOftenAsItIsDraggedWithoutAStopOrASnapBack()
    {
        const float DEGREES_A_STEP = 20f;
        const float PIXELS_A_STEP = DEGREES_A_STEP / NavigatorMapView.YAW_PER_PIXEL;
        foreach (float direction in new[] { 1f, -1f })
        {
            NavigatorMapView view = new();
            float start = view.Yaw;
            int wraps = 0;
            // (two whole turns, 36 drags of 20 degrees each: the number goes from one end of the circle to the other once in every turn)
            for (int step = 1; step <= 36; step++)
            {
                float before = view.Yaw;
                view.Drag(direction * PIXELS_A_STEP, 0f);

                NavigatorMapView.NormalizeYaw(view.Yaw - before).Should().BeApproximately(direction * DEGREES_A_STEP, 0.01f, $"drag {step} turns the map by the same amount as the first: nothing stops it or sets it back");
                view.Yaw.Should().BeGreaterThan(-180f).And.BeLessOrEqualTo(180f);
                if (Math.Abs(view.Yaw - before) > 180f)
                {
                    wraps++;
                }
                if (step % 18 == 0)
                {
                    NavigatorMapView.NormalizeYaw(view.Yaw - start).Should().BeApproximately(0f, 0.05f, $"after {step / 18} whole turns the map is where it began");
                }
            }
            wraps.Should().Be(2, "the number went from one end of the circle to the other once in every whole turn, and the map did not jump there");
            view.Tilt.Should().Be(NavigatorMapView.DEFAULT_TILT, "turning it round leaves the tilt alone");
        }
    }

    [TestMethod]
    public void DraggingUpAndDownRollsTheMapOverTheTopAndRightRoundWithoutALimit()
    {
        const float DEGREES_A_STEP = 30f;
        const float PIXELS_A_STEP = DEGREES_A_STEP / NavigatorMapView.TILT_PER_PIXEL;
        NavigatorMapView view = new();

        // dragging down tips the top of the map towards the navigator: past upright (90), on to over the top (180) and under
        List<float> seen = [];
        for (int step = 1; step <= 24; step++)
        {
            float before = view.Tilt;
            view.Drag(0f, -PIXELS_A_STEP);
            seen.Add(view.Tilt);

            NavigatorMapView.NormalizeYaw(view.Tilt - before).Should().BeApproximately(DEGREES_A_STEP, 0.01f, $"drag {step} rolls the map by the same amount as the first: there is no stop at upright, at flat or anywhere");
            view.Tilt.Should().BeGreaterThan(-180f).And.BeLessOrEqualTo(180f);
        }
        seen[0].Should().BeApproximately(88f, 0.01f);
        seen[1].Should().BeApproximately(118f, 0.01f, "it goes on past upright, where it used to stop");
        seen[3].Should().BeApproximately(178f, 0.01f);
        seen[4].Should().BeApproximately(-152f, 0.01f, "over the top and under: the underside is toward the navigator");
        seen[11].Should().BeApproximately(58f, 0.01f, "twelve drags of 30 degrees are a whole roll");
        view.Tilt.Should().BeApproximately(58f, 0.01f, "and twelve more are another");
        view.Yaw.Should().Be(NavigatorMapView.DEFAULT_YAW, "rolling it over leaves the turn alone");

        // the other way, up: down to flat (0) and on to leaning away, which used to stop at 15 degrees from flat
        view.Reset();
        view.Drag(0f, 58f / NavigatorMapView.TILT_PER_PIXEL);
        view.Tilt.Should().BeApproximately(0f, 0.01f, "dragging up lays the map flat, seen from the side");
        view.Drag(0f, 20f / NavigatorMapView.TILT_PER_PIXEL);
        view.Tilt.Should().BeApproximately(-20f, 0.01f, "and on");
        view.Drag(0f, 100000f);
        view.Tilt.Should().BeGreaterThan(-180f).And.BeLessOrEqualTo(180f);
    }

    /// <summary>
    /// Where a point of the map's plane (<paramref name="x" /> across the map to its front, <paramref name="z" /> across to its right, as the map lies before it is tilted) is on the navigator's screen, in the way the console
    /// poses the map: spun about its own vertical axis by the yaw, then tipped towards the navigator about the line across their view by the tilt (<c>Quaternion.Euler(0, 0, -tilt)</c>, with Unity's turn of +x towards -z
    /// as positive about the vertical). The navigator looks along -x, so +z is to their right and +y is up.
    /// </summary>
    private static (float Right, float Up) OnTheScreen(float yawDegrees, float tiltDegrees, float x, float z)
    {
        double yaw = yawDegrees * Math.PI / 180.0;
        double tilt = tiltDegrees * Math.PI / 180.0;
        double spunX = (x * Math.Cos(yaw)) + (z * Math.Sin(yaw));
        double spunZ = (-x * Math.Sin(yaw)) + (z * Math.Cos(yaw));
        return ((float)spunZ, (float)(-spunX * Math.Sin(tilt)));
    }

    [TestMethod]
    public void DraggingRightTurnsTheMapClockwiseOnTheScreenWhicheverSideOfItFacesTheNavigator()
    {
        // the point of the map that is at the top of the screen when the map is at this tilt: the one at the map's far edge (x -1) while the front of the map faces the navigator, the one at its near edge (x +1)
        // once it is rolled over and the map's far edge hangs at the bottom
        foreach (float tilt in new[] { 10f, 58f, 90f, 135f, 170f, -10f, -58f, -90f, -135f, -170f })
        {
            NavigatorMapView view = new();
            view.Set(30f, tilt, 1f);
            float topX = OnTheScreen(view.Yaw, view.Tilt, -1f, 0f).Up > 0f ? -1f : 1f;
            (float rightBefore, float upBefore) = OnTheScreen(view.Yaw, view.Tilt, topX, 0f);
            upBefore.Should().BeGreaterThan(0f, $"tilt {tilt}: the point that was picked is at the top of the screen");

            view.Drag(10f, 0f);
            (float rightAfter, _) = OnTheScreen(view.Yaw, view.Tilt, topX, 0f);

            rightAfter.Should().BeGreaterThan(rightBefore, $"tilt {tilt}: dragging right moves the top of the map to the right, as it does from the first tilt on (the front of the map faces the navigator: {NavigatorMapView.FacesViewer(tilt)})");
        }

        NavigatorMapView.FacesViewer(NavigatorMapView.DEFAULT_TILT).Should().BeTrue();
        NavigatorMapView.FacesViewer(90f).Should().BeTrue();
        NavigatorMapView.FacesViewer(179f).Should().BeTrue();
        NavigatorMapView.FacesViewer(-1f).Should().BeFalse("rolled one degree past flat, away from the navigator, the underside is toward them");
        NavigatorMapView.FacesViewer(-179f).Should().BeFalse();
        NavigatorMapView.FacesViewer(181f).Should().BeFalse("181 is -179");
        new NavigatorMapView().ShowsItsFront.Should().BeTrue();
    }

    [TestMethod]
    public void TheWheelZoomsInSteadilyAndStopsAtTheLimits()
    {
        NavigatorMapView view = new();

        view.Scroll(1f);
        view.Zoom.Should().BeApproximately(NavigatorMapView.ZOOM_PER_TICK, 0.0001f);
        view.Scroll(-1f);
        view.Zoom.Should().BeApproximately(1f, 0.0001f);

        view.Scroll(-20f);
        view.Zoom.Should().Be(NavigatorMapView.MIN_ZOOM, "the whole range is the widest view");
        view.Scroll(100f);
        view.Zoom.Should().Be(NavigatorMapView.MAX_ZOOM);
        view.VisibleRadius(400f).Should().Be(50f);
    }

    [TestMethod]
    public void SettingTheViewTakesTheAnglesRoundTheCircleAndKeepsTheZoomWithinItsLimits()
    {
        NavigatorMapView view = new();

        view.Set(200f, 5f, 99f);
        (view.Yaw, view.Tilt, view.Zoom).Should().Be((-160f, 5f, NavigatorMapView.MAX_ZOOM), "a tilt of 5 degrees is as good as any: the old floor of 15 is gone");
        view.Set(-540f, 200f, 0f);
        (view.Yaw, view.Tilt, view.Zoom).Should().Be((180f, -160f, NavigatorMapView.MIN_ZOOM));
        view.Set(0f, -90f, 1f);
        view.Tilt.Should().Be(-90f, "the map may lean away from the navigator as well");
        view.Set(0f, 720f + 33f, 1f);
        view.Tilt.Should().BeApproximately(33f, 0.001f);
        view.Set(float.NaN, float.NaN, float.NaN);
        (view.Yaw, view.Tilt, view.Zoom).Should().Be((NavigatorMapView.DEFAULT_YAW, NavigatorMapView.DEFAULT_TILT, NavigatorMapView.MIN_ZOOM));
    }

    [TestMethod]
    public void NumbersThatAreNotNumbersChangeNothingAndResetGivesTheStartBack()
    {
        NavigatorMapView view = new();
        view.Drag(30f, 20f);
        view.Scroll(3f);
        (float yaw, float tilt, float zoom) = (view.Yaw, view.Tilt, view.Zoom);

        view.Drag(float.NaN, 1f);
        view.Drag(1f, float.PositiveInfinity);
        view.Scroll(float.NaN);

        (view.Yaw, view.Tilt, view.Zoom).Should().Be((yaw, tilt, zoom));
        view.Reset();
        (view.Yaw, view.Tilt, view.Zoom).Should().Be((NavigatorMapView.DEFAULT_YAW, NavigatorMapView.DEFAULT_TILT, 1f));
    }

    // ---- the view stays where the navigator left it

    private static string RepoFile(params string[] path)
    {
        string directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "Nitrox.slnx")))
            {
                return File.ReadAllText(Path.Combine(new[] { directory }.Concat(path).ToArray()));
            }
            directory = Path.GetDirectoryName(directory);
        }
        Assert.Inconclusive("Nitrox.slnx was not found above the test assembly");
        return "";
    }

    [TestMethod]
    public void TheViewIsOneLineOfTextThatReadsBackAsItWasEvenRolledOverAndZoomed()
    {
        NavigatorMapView view = new();
        view.Set(-123.5f, -152.25f, 3.5f);

        string text = view.ToText();

        text.Should().Be("-123.5,-152.25,3.5");
        NavigatorMapView back = new();
        back.TryLoad(text).Should().BeTrue();
        (back.Yaw, back.Tilt, back.Zoom).Should().Be((-123.5f, -152.25f, 3.5f));

        // a machine that writes its decimals with a comma writes and reads the same text
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            view.ToText().Should().Be(text);
            NavigatorMapView german = new();
            german.TryLoad(text).Should().BeTrue();
            german.Tilt.Should().Be(-152.25f);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void ATextThatIsNotAViewChangesNothing()
    {
        NavigatorMapView view = new();
        view.Set(10f, 20f, 2f);
        int revision = view.Revision;

        foreach (string text in new[] { null, "", "   ", "1,2", "1,2,3,4", "a,b,c", "1,2,NaN", "Infinity,1,1", "1;2;3" })
        {
            view.TryLoad(text).Should().BeFalse($"'{text}' is not a view");
        }

        (view.Yaw, view.Tilt, view.Zoom).Should().Be((10f, 20f, 2f));
        view.Revision.Should().Be(revision);
        // numbers that are numbers but out of range are taken round the circle and into the limits, as Set does it
        view.TryLoad("540,-200,99").Should().BeTrue();
        (view.Yaw, view.Tilt, view.Zoom).Should().Be((180f, 160f, NavigatorMapView.MAX_ZOOM));
    }

    [TestMethod]
    public void TheRevisionCountsWhatChangedTheViewAndNotWhatLeftItAsItWas()
    {
        NavigatorMapView view = new();
        view.Revision.Should().Be(0);

        view.Reset();
        view.Drag(0f, 0f);
        view.Scroll(0f);
        view.Scroll(-5f);
        view.Set(NavigatorMapView.DEFAULT_YAW, NavigatorMapView.DEFAULT_TILT, NavigatorMapView.MIN_ZOOM);
        view.Drag(float.NaN, 1f);
        view.Revision.Should().Be(0, "none of these moved the map");

        view.Drag(10f, 0f);
        view.Revision.Should().Be(1);
        view.Scroll(1f);
        view.Revision.Should().Be(2);
        view.Set(1f, 2f, 3f);
        view.Revision.Should().Be(3);
        view.Reset();
        view.Revision.Should().Be(4);
        view.Reset();
        view.Revision.Should().Be(4, "putting it back twice is one change");
    }

    [TestMethod]
    public void TheMapStaysWhereTheNavigatorLeftItWhenTheConsoleIsLeftAndEnteredAgainAndAfterARestart()
    {
        NavigatorViewKeeper keeper = new();
        keeper.Load(string.Empty).Should().BeFalse("nothing was kept for this Cyclops yet: the map stands as it first does");
        (keeper.View.Yaw, keeper.View.Tilt, keeper.View.Zoom).Should().Be((NavigatorMapView.DEFAULT_YAW, NavigatorMapView.DEFAULT_TILT, NavigatorMapView.MIN_ZOOM));

        // the navigator turns the map, rolls it over the top and zooms in
        keeper.View.Drag(250f, -400f);
        keeper.View.Scroll(4f);
        (float yaw, float tilt, float zoom) = (keeper.View.Yaw, keeper.View.Tilt, keeper.View.Zoom);
        yaw.Should().NotBe(NavigatorMapView.DEFAULT_YAW);
        tilt.Should().BeGreaterThan(90f, "rolled over past upright");
        zoom.Should().BeGreaterThan(1f);

        // they get up: the console writes the view down and does nothing else to it
        string written = keeper.Flush();
        written.Should().NotBeNull();
        (keeper.View.Yaw, keeper.View.Tilt, keeper.View.Zoom).Should().Be((yaw, tilt, zoom), "leaving the console does not put the map back");

        // the next one sits down at it: the map is as it was, and nothing needs writing
        (keeper.View.Yaw, keeper.View.Tilt, keeper.View.Zoom).Should().Be((yaw, tilt, zoom), "entering the console does not put the map back");
        keeper.Flush().Should().BeNull("nothing changed since it was written");
        keeper.TakeDue(1000f).Should().BeNull();

        // the game is closed and started again: the console of the same Cyclops reads what was written
        NavigatorViewKeeper restarted = new();
        restarted.Load(written).Should().BeTrue();
        restarted.View.Yaw.Should().BeApproximately(yaw, 1e-4f);
        restarted.View.Tilt.Should().BeApproximately(tilt, 1e-4f);
        restarted.View.Zoom.Should().BeApproximately(zoom, 1e-4f);
        restarted.Flush().Should().BeNull("what was just read does not need writing again");

        // only the navigator's own Reset puts it back, and that is a change like any other
        restarted.View.Reset();
        restarted.Flush().Should().Be(new NavigatorMapView().ToText());
    }

    [TestMethod]
    public void TheViewIsWrittenAMomentAfterTheLastChangeAndAtOnceWhenTheConsoleIsLeft()
    {
        NavigatorViewKeeper keeper = new();
        keeper.Load(string.Empty);
        keeper.TakeDue(0f).Should().BeNull("nothing has changed");

        keeper.View.Drag(10f, 0f);
        keeper.TakeDue(10f).Should().BeNull("a change starts the wait");
        keeper.View.Drag(10f, 0f);
        keeper.TakeDue(10.5f).Should().BeNull("another change starts the wait again");
        keeper.TakeDue(10f + NavigatorViewKeeper.SAVE_AFTER_SECONDS + 0.1f).Should().BeNull("the first wait is over but the second is not: a drag is written once, after it");
        string written = keeper.TakeDue(10.5f + NavigatorViewKeeper.SAVE_AFTER_SECONDS + 0.1f);
        written.Should().Be(keeper.View.ToText());
        keeper.TakeDue(100f).Should().BeNull("it was written once");

        // leaving the console writes at once what is not written yet
        keeper.View.Scroll(2f);
        keeper.Flush().Should().Be(keeper.View.ToText());
        keeper.Flush().Should().BeNull();
        keeper.TakeDue(200f).Should().BeNull("the leave wrote it already");
    }

    [TestMethod]
    public void TheKeptViewIsReadOnceAndNeverOverAViewThatWasAlreadyTouchedAndEveryCyclopsHasItsOwn()
    {
        NavigatorViewKeeper touched = new();
        touched.View.Drag(100f, 0f);
        float yaw = touched.View.Yaw;
        touched.Loaded.Should().BeFalse();
        touched.Load("-90,40,2").Should().BeFalse("the navigator has turned the map already: theirs stands");
        touched.Loaded.Should().BeTrue();
        touched.View.Yaw.Should().Be(yaw);
        touched.Load("-90,40,2").Should().BeFalse("the kept view is read once");
        touched.Flush().Should().Be(touched.View.ToText(), "the view they made is not lost");

        NavigatorViewKeeper unreadable = new();
        unreadable.Load("not a view").Should().BeFalse();
        unreadable.Loaded.Should().BeTrue();
        unreadable.View.Yaw.Should().Be(NavigatorMapView.DEFAULT_YAW);
        unreadable.Flush().Should().BeNull("a view that could not be read leaves nothing to write");

        NavigatorViewKeeper.PrefKey("cyclops-a").Should().NotBe(NavigatorViewKeeper.PrefKey("cyclops-b"));
        NavigatorViewKeeper.PrefKey("cyclops-a").Should().StartWith(NavigatorViewKeeper.PREF_PREFIX).And.EndWith("cyclops-a");
    }

    [TestMethod]
    public void TheConsoleNeverPutsTheViewBackByItselfAndOnlyTheNavigatorsOwnResetDoes()
    {
        string console = RepoFile("NitroxClient", "MonoBehaviours", "Cyclops", "NavigatorConsole.cs");
        string screen = RepoFile("NitroxClient", "MonoBehaviours", "Cyclops", "NavigatorConsoleScreen.cs");

        console.Should().NotContain("View.Reset(", "sitting down at the console and getting up do nothing to the view (2026-10-30: it keeps the angle you left it at)");
        console.Should().NotContain("ActiveView = null", "the map is drawn with the console's view all the time, not only while the console is open");
        console.Should().Contain("NavigatorViewKeeper");
        screen.Should().Contain("console.View.Reset()", "the R key and the Reset view button are the navigator's own choice");
    }

    // ---- the hologram in the build room

    [TestMethod]
    public void TheHologramFloatsInTheMiddleOfTheBuildRoomAndRisesAsItGrows()
    {
        (float x, float y, float z) = NavigatorConsoleLayout.HologramCenter;

        x.Should().BeApproximately((NavigatorConsoleLayout.ROOM_MIN_X + NavigatorConsoleLayout.ROOM_MAX_X) / 2f, 1e-4f, "the middle of the room across");
        z.Should().BeApproximately((NavigatorConsoleLayout.ROOM_MIN_Z + NavigatorConsoleLayout.ROOM_MAX_Z) / 2f, 1e-4f, "the middle of the room along");
        (y - NavigatorConsoleLayout.ROOM_MIN_Y).Should().BeApproximately(NavigatorConsoleLayout.HOLOGRAM_HEIGHT_ABOVE_FLOOR, 1e-4f, "waist height for a small one");
        NavigatorConsoleLayout.ROOM_MAX_Z.Should().BeLessThan(NavigatorConsoleLayout.ROOM_MIN_Z + 4.5f, "the room is the 4 m between two bulkheads");
        NavigatorConsoleLayout.ROOM_MIN_Z.Should().BeGreaterThan(-15.5f, "behind the helm's bulkhead (its collider ends at z -15.41)");
        NavigatorConsoleLayout.ROOM_MAX_Z.Should().BeLessThan(-11f, "before the vehicle bay's bulkhead (its collider starts at z -11.29)");

        NavigatorConsoleLayout.CenterY(NavigatorConsoleLayout.MIN_RADIUS).Should().BeApproximately(y, 1e-4f, "a small hologram hangs at waist height");
        NavigatorConsoleLayout.CenterY(0.5f).Should().BeApproximately(y, 1e-4f);
        float previous = NavigatorConsoleLayout.CenterY(0.5f);
        for (float radius = 0.6f; radius <= NavigatorConsoleLayout.MAX_RADIUS; radius += 0.05f)
        {
            float centre = NavigatorConsoleLayout.CenterY(radius);
            centre.Should().BeGreaterThanOrEqualTo(previous - 1e-4f, "it only rises as it grows (radius " + radius + ")");
            previous = centre;
        }
        NavigatorConsoleLayout.CenterY(NavigatorConsoleLayout.MAX_RADIUS).Should().BeApproximately((NavigatorConsoleLayout.ROOM_MIN_Y + NavigatorConsoleLayout.ROOM_MAX_Y) / 2f, 1e-4f, "the floor and the ceiling cut an equal share of the biggest");
    }

    [TestMethod]
    public void TheHologramNeverReachesAWallOrABulkheadAndItsSphereFitsBetweenFloorAndCeilingUpToTheFullHeightSize()
    {
        (float x, float _, float z) = NavigatorConsoleLayout.HologramCenter;
        float biggest = NavigatorConsoleLayout.MAX_RADIUS;

        (x - biggest).Should().BeGreaterThan(NavigatorConsoleLayout.ROOM_MIN_X + NavigatorConsoleLayout.ROOM_MARGIN, "the wall on the pilot's right");
        (x + biggest).Should().BeLessThan(NavigatorConsoleLayout.ROOM_MAX_X - NavigatorConsoleLayout.ROOM_MARGIN, "the wall on the pilot's left");
        (z - biggest).Should().BeGreaterThan(NavigatorConsoleLayout.ROOM_MIN_Z + NavigatorConsoleLayout.ROOM_MARGIN, "the helm's bulkhead");
        (z + biggest).Should().BeLessThan(NavigatorConsoleLayout.ROOM_MAX_Z - NavigatorConsoleLayout.ROOM_MARGIN, "the vehicle bay's bulkhead");
        biggest.Should().BeGreaterThan(1.9f, "nearly the half length of the room (2.06 m)");

        // the game fades the terrain by its distance in all three directions: the volume is a sphere. Up to the full-height size the sphere is clear of the floor and the ceiling
        for (float radius = NavigatorConsoleLayout.MIN_RADIUS; radius <= NavigatorConsoleLayout.FullHeightRadius + 1e-4f; radius += 0.05f)
        {
            float centre = NavigatorConsoleLayout.CenterY(radius);

            (centre - radius).Should().BeGreaterThanOrEqualTo(NavigatorConsoleLayout.ROOM_MIN_Y + NavigatorConsoleLayout.ROOM_MARGIN - 1e-3f, "above the floor, radius " + radius);
            (centre + radius).Should().BeLessThanOrEqualTo(NavigatorConsoleLayout.ROOM_MAX_Y - NavigatorConsoleLayout.ROOM_MARGIN + 1e-3f, "below the ceiling, radius " + radius);
        }
        NavigatorConsoleLayout.DEFAULT_RADIUS.Should().BeLessThanOrEqualTo(NavigatorConsoleLayout.FullHeightRadius, "the size it starts with is not cut by the floor or the ceiling");
        NavigatorConsoleLayout.FullHeightRadius.Should().BeApproximately(1.445f, 1e-3f, "half the room's height of 2.99 m, less the margin");
        NavigatorConsoleLayout.MaxRadiusInRoom(NavigatorConsoleLayout.HologramCenter, 0f).Should().BeApproximately(NavigatorConsoleLayout.HOLOGRAM_HEIGHT_ABOVE_FLOOR, 1e-4f, "from the waist-high middle the floor is the nearest face");
        NavigatorConsoleLayout.MIN_RADIUS.Should().BeLessThan(NavigatorConsoleLayout.DEFAULT_RADIUS);
        NavigatorConsoleLayout.DEFAULT_RADIUS.Should().BeLessThan(NavigatorConsoleLayout.MAX_RADIUS);
    }

    [TestMethod]
    public void TheSizeIsKeptBetweenTheLimitsAndNotANumberGivesTheDefault()
    {
        NavigatorConsoleLayout.ClampRadius(0.01f).Should().Be(NavigatorConsoleLayout.MIN_RADIUS);
        NavigatorConsoleLayout.ClampRadius(7f).Should().Be(NavigatorConsoleLayout.MAX_RADIUS);
        NavigatorConsoleLayout.ClampRadius(0.4f).Should().Be(0.4f);
        NavigatorConsoleLayout.ClampRadius(float.NaN).Should().Be(NavigatorConsoleLayout.DEFAULT_RADIUS);
        NavigatorConsoleLayout.ClampRadius(float.PositiveInfinity).Should().Be(NavigatorConsoleLayout.DEFAULT_RADIUS);
    }

    [TestMethod]
    public void TheSizeControlStepsTenPercentANotchAndStopsAtTheLimits()
    {
        NavigatorConsoleLayout.StepRadius(0.5f, 1f).Should().BeApproximately(0.55f, 1e-4f);
        NavigatorConsoleLayout.StepRadius(0.5f, -1f).Should().BeApproximately(0.5f / 1.1f, 1e-4f);
        NavigatorConsoleLayout.StepRadius(0.5f, 100f).Should().Be(NavigatorConsoleLayout.MAX_RADIUS);
        NavigatorConsoleLayout.StepRadius(0.5f, -100f).Should().Be(NavigatorConsoleLayout.MIN_RADIUS);
        NavigatorConsoleLayout.StepRadius(0.5f, float.NaN).Should().Be(0.5f, "not a number of notches changes nothing");
        NavigatorConsoleLayout.StepRadius(float.NaN, 1f).Should().BeApproximately(NavigatorConsoleLayout.DEFAULT_RADIUS * 1.1f, 1e-4f);

        float radius = 0.8f;
        for (int i = 0; i < 4; i++)
        {
            radius = NavigatorConsoleLayout.StepRadius(radius, 1f);
        }
        radius.Should().BeGreaterThan(0.8f);
        for (int i = 0; i < 4; i++)
        {
            radius = NavigatorConsoleLayout.StepRadius(radius, -1f);
        }
        radius.Should().BeApproximately(0.8f, 1e-4f, "as many notches back as forth is where it started");
    }

    [TestMethod]
    public void TheHandTriggerIsASlabOnTheDiscNotTooSmallToAimAtAndTheConsoleStaysOpenNearIt()
    {
        NavigatorConsoleLayout.TargetSize(NavigatorConsoleLayout.MIN_RADIUS).Should().Be((NavigatorConsoleLayout.MIN_TARGET_DIAMETER, NavigatorConsoleLayout.TARGET_THICKNESS), "a very small hologram can still be aimed at");
        NavigatorConsoleLayout.TargetSize(1f).Should().Be((2f, NavigatorConsoleLayout.TARGET_THICKNESS), "as wide as the disc");
        NavigatorConsoleLayout.TargetSize(NavigatorConsoleLayout.MAX_RADIUS).Width.Should().BeApproximately(2f * NavigatorConsoleLayout.MAX_RADIUS, 1e-4f);
        NavigatorConsoleLayout.TargetSize(float.NaN).Width.Should().BeApproximately(2f * NavigatorConsoleLayout.DEFAULT_RADIUS, 1e-4f);
        NavigatorConsoleLayout.TARGET_THICKNESS.Should().BeLessThan(1f, "a slab, not a volume: a ray from eyes inside a big hologram still finds it from above");
        NavigatorConsoleLayout.MaxUseDistance(0.5f).Should().BeApproximately(NavigatorConsoleLayout.MAX_USE_DISTANCE + 0.5f, 1e-4f);
        NavigatorConsoleLayout.MaxUseDistance(NavigatorConsoleLayout.MAX_RADIUS).Should().BeGreaterThan(NavigatorConsoleLayout.MaxUseDistance(NavigatorConsoleLayout.MIN_RADIUS));
    }

    [TestMethod]
    public void TheFrameTiltsTowardsTheViewerWhereverTheyStand()
    {
        (float X, float Y, float Z) anchor = (-0.15f, 0.21f, -13.35f);

        NavigatorConsoleLayout.FrameYawToward((1.85f, 0.86f, -13.35f), anchor, 77f).Should().BeApproximately(0f, 1e-3f, "a viewer on the +x side: the frame is tilted towards +x as it is built");
        NavigatorConsoleLayout.FrameYawToward((-0.15f, 0.86f, -15.35f), anchor, 77f).Should().BeApproximately(90f, 1e-3f, "a viewer towards the bow (-z)");
        Math.Abs(NavigatorConsoleLayout.FrameYawToward((-2.15f, 0.86f, -13.35f), anchor, 77f)).Should().BeApproximately(180f, 1e-3f, "a viewer on the -x side (half a turn either way)");
        NavigatorConsoleLayout.FrameYawToward((-0.15f, 0.86f, -11.35f), anchor, 77f).Should().BeApproximately(-90f, 1e-3f, "a viewer towards the stern (+z)");

        // the frame's +x, turned about the vertical by the answer, points at the viewer (Unity turns +x to (cos, 0, -sin))
        foreach ((float x, float z) in new[] { (1.1f, -12.4f), (-1.9f, -14.8f), (0.5f, -15.2f), (-2.3f, -11.6f) })
        {
            float yaw = NavigatorConsoleLayout.FrameYawToward((x, 0.86f, z), anchor, 0f);
            double radians = yaw * Math.PI / 180.0;
            double length = Math.Sqrt((x - anchor.X) * (x - anchor.X) + (z - anchor.Z) * (z - anchor.Z));
            Math.Cos(radians).Should().BeApproximately((x - anchor.X) / length, 1e-4);
            (-Math.Sin(radians)).Should().BeApproximately((z - anchor.Z) / length, 1e-4);
        }

        NavigatorConsoleLayout.FrameYawToward((-0.15f, 3f, -13.35f), anchor, 77f).Should().Be(77f, "straight above: there is no side to tilt towards, the turn is kept");
        NavigatorConsoleLayout.FrameYawToward((float.NaN, 0f, 0f), anchor, 77f).Should().Be(77f);
    }

    [TestMethod]
    public void TheCameraIsTurnedToTheHologramFromTheSideOfTheRoom()
    {
        // eyes at the +x side of the room, the hologram straight ahead (towards -x) and a little below the eyes
        (float yaw, float pitch) = NavigatorConsoleLayout.AimAngles((1.0f, 0.86f, -13.35f), NavigatorConsoleLayout.HologramCenter);

        yaw.Should().BeApproximately(-90f, 0.1f, "looking towards -x");
        pitch.Should().BeApproximately(-29.6f, 0.2f, "down at the hologram, which is at waist height");
    }

    [TestMethod]
    public void AimAnglesCountTheWayUnityDoes()
    {
        NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (0f, 0f, 5f)).Should().Be((0f, 0f));
        NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (5f, 0f, 0f)).Yaw.Should().BeApproximately(90f, 0.001f);
        NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (0f, 0f, -5f)).Yaw.Should().BeApproximately(180f, 0.001f);
        NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (0f, 5f, 0f)).Pitch.Should().BeApproximately(90f, 0.001f);
        NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (0f, -5f, 5f)).Pitch.Should().BeApproximately(-45f, 0.001f);
        NavigatorConsoleLayout.AimAngles((1f, 1f, 1f), (1f, 1f, 1f)).Should().Be((0f, 0f), "no direction");
        NavigatorConsoleLayout.AimAngles((float.NaN, 0f, 0f), (1f, 0f, 0f)).Should().Be((0f, 0f));
    }

    [TestMethod]
    public void TheHologramIsShownNearAndKeptUntilFarWithoutFlickering()
    {
        NavigatorConsoleLayout.ShouldShowMap(4f, false).Should().BeTrue("in the build room");
        NavigatorConsoleLayout.ShouldShowMap(NavigatorConsoleLayout.MAP_SHOW_DISTANCE + 0.5f, false).Should().BeFalse("not yet");
        NavigatorConsoleLayout.ShouldShowMap(NavigatorConsoleLayout.MAP_SHOW_DISTANCE + 0.5f, true).Should().BeTrue("once shown it stays until the player is farther than the hide distance");
        NavigatorConsoleLayout.ShouldShowMap(NavigatorConsoleLayout.MAP_HIDE_DISTANCE + 0.5f, true).Should().BeFalse();
        NavigatorConsoleLayout.ShouldShowMap(float.NaN, true).Should().BeFalse();
        NavigatorConsoleLayout.MAP_HIDE_DISTANCE.Should().BeGreaterThan(NavigatorConsoleLayout.MAP_SHOW_DISTANCE);
    }

    [TestMethod]
    public void TheMapStartsAsTheWorldLiesAndKeepsTheDirectionsWhateverTheFrameTurn()
    {
        NavigatorMapView.DEFAULT_YAW.Should().Be(0f, "the map is registered to the world: only its tilt follows the viewer");
        NavigatorMapView.NormalizeYaw(NavigatorMapView.DEFAULT_YAW - 123f).Should().BeApproximately(-123f, 1e-4f);
    }

    // ---- contacts

    [TestMethod]
    public void DistancesAreWrittenInMetresThenKilometres()
    {
        ContactRules.Distance(0f).Should().Be("0 m");
        ContactRules.Distance(4.4f).Should().Be("4 m");
        ContactRules.Distance(999f).Should().Be("999 m");
        ContactRules.Distance(999.4f).Should().Be("999 m");
        ContactRules.Distance(999.6f).Should().Be("1.0 km", "a distance that rounds to a kilometre is not written as 1000 m");
        ContactRules.Distance(1250f).Should().Be("1.3 km");
        ContactRules.Distance(-1f).Should().Be("-");
        ContactRules.Distance(float.NaN).Should().Be("-");
    }

    [TestMethod]
    public void AContactBeyondTheRimIsDrawnOnTheRimInTheSameDirectionAndSmaller()
    {
        ContactRules.RimClamp(50f, 100f).Should().Be((1f, false));
        ContactRules.RimClamp(100f, 100f).Should().Be((1f, false));
        (float scale, bool off) = ContactRules.RimClamp(250f, 100f);
        scale.Should().BeApproximately(0.4f, 0.0001f);
        off.Should().BeTrue();
        ContactRules.IconSize(true).Should().BeLessThan(ContactRules.IconSize(false));
        ContactRules.RimClamp(10f, 0f).OffMap.Should().BeTrue("a map without a size draws nothing in place");
        ContactRules.RimClamp(float.NaN, 100f).OffMap.Should().BeTrue();
        ContactRules.RimClamp(0f, 100f).Should().Be((1f, false), "the hull's own icon sits at the middle");
    }

    [TestMethod]
    public void ARangeKeepsWhatIsInsideItOnly()
    {
        ContactRules.WithinRange(199f, 200f).Should().BeTrue();
        ContactRules.WithinRange(201f, 200f).Should().BeFalse();
        ContactRules.WithinRange(float.NaN, 200f).Should().BeFalse();
        ContactRules.WithinRange(-3f, 200f).Should().BeFalse();
    }

    [TestMethod]
    public void AMarkOutranksTheCrewAndTheirVehiclesButNotADangerAndIsKeptOnTheRim()
    {
        ContactRules.Priority(ContactKind.Mark, CreatureDanger.None).Should().BeGreaterThan(ContactRules.Priority(ContactKind.Teammate, CreatureDanger.None), "the crew's own marks are not the ones left out first");
        ContactRules.Priority(ContactKind.Mark, CreatureDanger.None).Should().BeLessThan(ContactRules.Priority(ContactKind.Creature, CreatureDanger.Dangerous), "a danger is never dropped for a mark");
        ContactRules.Priority(ContactKind.Mark, CreatureDanger.None).Should().BeLessThan(ContactRules.Priority(ContactKind.Cyclops, CreatureDanger.None));
        ContactRules.ClampsToRim(ContactKind.Mark).Should().BeTrue("a mark beyond the range is a direction to go in");
    }

    [TestMethod]
    public void OnlyTheThingsThatMoveAreKeptOnTheRimAndTheHeightIsWrittenForTheLine()
    {
        ContactRules.ClampsToRim(ContactKind.Creature).Should().BeTrue();
        ContactRules.ClampsToRim(ContactKind.Teammate).Should().BeTrue();
        ContactRules.ClampsToRim(ContactKind.Vehicle).Should().BeTrue();
        ContactRules.ClampsToRim(ContactKind.Beacon).Should().BeFalse();
        ContactRules.ClampsToRim(ContactKind.Base).Should().BeFalse();

        ContactRules.Height(35f).Should().Be("35 m above");
        ContactRules.Height(-1250f).Should().Be("1.3 km below");
        ContactRules.Height(1.5f).Should().Be("level");
        ContactRules.Height(float.NaN).Should().Be("level");
    }

    [TestMethod]
    public void TheKeyNameComesOutOfTheGamesTextMeshProMarkup()
    {
        NavigatorConsoleText.PlainKeyName("<sprite name=\"KeyboardE\" color=#ADF8FFFF>", "Esc").Should().Be("E");
        NavigatorConsoleText.PlainKeyName("<sprite name=\"KeyboardEscape\">", "x").Should().Be("Escape");
        NavigatorConsoleText.PlainKeyName("<sprite name=\"Mouse0\">", "x").Should().Be("Mouse0");
        NavigatorConsoleText.PlainKeyName("<color=#fff>X</color>", "Esc").Should().Be("X", "other markup is dropped");
        NavigatorConsoleText.PlainKeyName("Space", "Esc").Should().Be("Space");
        NavigatorConsoleText.PlainKeyName("", "Esc").Should().Be("Esc");
        NavigatorConsoleText.PlainKeyName(null, "Esc").Should().Be("Esc");
    }

    [TestMethod]
    public void TheLinesOfTheMapNameTheContactAndTheRange()
    {
        ContactRules.Describe("Reaper Leviathan", 120f).Should().Be("Reaper Leviathan  120 m");
        ContactRules.Describe("  ", 5f).Should().Be("Contact  5 m");
        ContactRules.RangeLine(133.33f, 1.5f).Should().Be("Range 133 m (zoom 1.5x)");
        ContactRules.RangeLine(2000f, 1f).Should().Be("Range 2.0 km (zoom 1.0x)");
    }
}
