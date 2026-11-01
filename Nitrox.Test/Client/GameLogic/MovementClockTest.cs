namespace NitroxClient.GameLogic;

/// <summary>
///     The test setting of the movement clock (<see cref="MovementClock" />, Debug builds only): <c>NITROX_TIME_TEST=offset=30000,float=1</c> makes a young harness world read as an old one and
///     brings the float timestamps of before bug 7 back, so that one build can show the difference (a harness script that is not part of this repository).
/// </summary>
[TestClass]
public class MovementClockTest
{
    [TestMethod]
    public void NothingSetIsTheRealClock()
    {
        MovementClock.Parse(null).Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("  ").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Apply(MovementClock.Parse(null), 28800.123456789).Should().Be(28800.123456789);
    }

    [TestMethod]
    public void TheOffsetAndTheFloatAreRead()
    {
        MovementClock.Parse("offset=30000,float=1").Should().Be(new MovementClock.Setting(30000, true));
        MovementClock.Parse("float=1,offset=30000").Should().Be(new MovementClock.Setting(30000, true));
        MovementClock.Parse(" Offset = 131400 , FLOAT = true ").Should().Be(new MovementClock.Setting(131400, true));
        MovementClock.Parse("offset=1e4").Should().Be(new MovementClock.Setting(10000, false));
        MovementClock.Parse("offset=-5.5").Should().Be(new MovementClock.Setting(-5.5, false));
        MovementClock.Parse("float=0").Should().Be(new MovementClock.Setting(0, false));
    }

    [TestMethod]
    public void WhatIsNotUnderstoodIsIgnored()
    {
        MovementClock.Parse("offset=abc,float=1").Should().Be(new MovementClock.Setting(0, true));
        MovementClock.Parse("offset=NaN").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("offset=Infinity").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("speed=2,offset=10").Should().Be(new MovementClock.Setting(10, false));
        MovementClock.Parse("garbage").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("offset").Should().Be(new MovementClock.Setting(0, false));
        MovementClock.Parse("offset=1=2").Should().Be(new MovementClock.Setting(0, false));
    }

    [TestMethod]
    public void TheOffsetMakesAYoungWorldOld()
    {
        MovementClock.Setting setting = MovementClock.Parse("offset=30000");

        MovementClock.Apply(setting, 600.25).Should().Be(30600.25);
    }

    [TestMethod]
    public void TheFloatSettingRoundsToAFloatLikeTheOldCode()
    {
        MovementClock.Setting setting = MovementClock.Parse("offset=33000,float=1");
        const double REAL = 100.987654321;

        double read = MovementClock.Apply(setting, REAL);

        read.Should().Be((double)(float)(REAL + 33000)); // the old (float)RealTimeElapsed
        Math.Abs(read - (REAL + 33000)).Should().BeGreaterThan(0.0001); // and it is not the exact time: that is the point of the setting
        Math.Abs(read - (REAL + 33000)).Should().BeLessThanOrEqualTo(0.00195313); // by at most half a step of 3.9 ms
    }

    [TestMethod]
    public void TheDoubleSettingKeepsTheExactTimeAtTheSameAge()
    {
        MovementClock.Setting setting = MovementClock.Parse("offset=33000");
        const double REAL = 100.987654321;

        MovementClock.Apply(setting, REAL).Should().Be(REAL + 33000);
    }

    [TestMethod]
    public void TheSenderThresholdIsAFloatSumUnderTheFloatSetting()
    {
        // before bug 7 the threshold "last broadcast plus one period" was a sum of floats, rounded to the float grid (1.95 ms at 8 h): 17 steps are 33.20 ms, the exact period is 33.33 ms
        MovementClock.Setting oldBehaviour = MovementClock.Parse("float=1");
        double last = (float)30000.1234;

        double rounded = MovementClock.Plus(oldBehaviour, last, 1f / 30f);
        double exact = MovementClock.Plus(MovementClock.Parse(null), last, 1f / 30f);

        rounded.Should().Be((double)(float)(last + 1f / 30f));
        exact.Should().Be(last + 1f / 30f);
        Math.Abs(rounded - exact).Should().BeLessThanOrEqualTo(0.001);
    }

    [TestMethod]
    public void TheSettingIsDescribedForTheLog()
    {
        MovementClock.Describe(MovementClock.Parse(null)).Should().Be("real");
        MovementClock.Describe(MovementClock.Parse("offset=30000")).Should().Be("offset=30000");
        MovementClock.Describe(MovementClock.Parse("offset=30000,float=1")).Should().Be("offset=30000+float");
        MovementClock.Describe(MovementClock.Parse("float=1")).Should().Be("offset=0+float");
    }
}
