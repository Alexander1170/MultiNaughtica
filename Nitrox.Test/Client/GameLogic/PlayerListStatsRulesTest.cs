using System.Globalization;
using LitJson;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Helper;
using static NitroxClient.GameLogic.PlayerListStatsRules;

namespace NitroxClient.GameLogic;

[TestClass]
public class PlayerListStatsRulesTest
{
    private static readonly StatsLabels L = StatsLabels.English;

    private static Point3 At(float x, float y, float z)
    {
        return new Point3(x, y, z);
    }

    private static StatsSnapshot Snapshot(float health = 80f, float food = 54.2f, float water = 71f, float age = 1f, Point3? remote = null, Point3? local = null, float ocean = 0f, RemoteVehicleKind vehicle = RemoteVehicleKind.None, VitalsParts parts = VitalsParts.All)
    {
        // a sample that came `age` seconds before `now`
        const float NOW = 1000f;
        return StatsSnapshot.Create(new VitalsSample(health, food, water, NOW - age), NOW, remote ?? At(3f, -45f, 4f), local ?? At(0f, -45f, 0f), ocean, vehicle, parts);
    }

    // ---- a vital

    [TestMethod]
    public void FreshVitalsArePlainRoundedNumbers()
    {
        VitalValue(80f, stale: false).Should().Be("80");
        VitalValue(1f, stale: false).Should().Be("1");
        VitalValue(100f, stale: false).Should().Be("100");
    }

    [TestMethod]
    public void AHalfRoundsUpLikeTheBarsOfTheGameDo()
    {
        // the bars of the HUD use CeilToInt: the number is the one on the player's own screen
        VitalValue(79.5f, stale: false).Should().Be("80");
        VitalValue(79.0f, stale: false).Should().Be("79");
        VitalValue(79.01f, stale: false).Should().Be("80");
    }

    [TestMethod]
    public void JustUnderOneHundredReadsOneHundred()
    {
        VitalValue(99.99f, stale: false).Should().Be("100");
    }

    [TestMethod]
    public void ALivingPlayerNeverReadsZero()
    {
        VitalValue(0.4f, stale: false).Should().Be("1");
        VitalValue(0.0001f, stale: false).Should().Be("1");
    }

    [TestMethod]
    public void ZeroIsZeroAndNegativeZeroToo()
    {
        VitalValue(0f, stale: false).Should().Be("0");
        VitalValue(-0f, stale: false).Should().Be("0");
    }

    [TestMethod]
    public void NotANumberInfinityAndNegativeAreUnknown()
    {
        VitalValue(float.NaN, stale: false).Should().Be("?");
        VitalValue(float.PositiveInfinity, stale: false).Should().Be("?");
        VitalValue(float.NegativeInfinity, stale: false).Should().Be("?");
        VitalValue(-1f, stale: false).Should().Be("?");
    }

    [TestMethod]
    public void FoodAboveOneHundredIsNotCut()
    {
        VitalValue(150f, stale: false).Should().Be("150");
        VitalValue(200f, stale: false).Should().Be("200");
    }

    [TestMethod]
    public void AbsurdValuesAreCapped()
    {
        VitalValue(998.2f, stale: false).Should().Be("999");
        VitalValue(999.1f, stale: false).Should().Be("999+");
        VitalValue(1000f, stale: false).Should().Be("999+");
        VitalValue(float.MaxValue, stale: false).Should().Be("999+");
    }

    [TestMethod]
    public void StaleVitalsGetATildeAndUnknownOnesNever()
    {
        VitalValue(80f, stale: true).Should().Be("~80");
        VitalValue(float.NaN, stale: true).Should().Be("?");
    }

    // ---- how old is too old

    [TestMethod]
    public void VitalsAreStaleFromTenSecondsOn()
    {
        IsStale(0f).Should().BeFalse();
        IsStale(9.99f).Should().BeFalse();
        IsStale(10f).Should().BeTrue();
        IsStale(300f).Should().BeTrue();
    }

    [TestMethod]
    public void VitalsFromTheFutureAreStaleAndNoAgeIsNotStale()
    {
        // the clock went back: the age cannot be trusted; "never came" is unknown, which is shown as ?, not as old
        IsStale(-0.5f).Should().BeTrue();
        IsStale(float.NaN).Should().BeFalse();
    }

    [TestMethod]
    public void TheAgeOfASampleFollowsTheClock()
    {
        VitalsSample sample = new(80f, 50f, 70f, receivedAt: 100f);

        sample.AgeAt(104.5f).Should().Be(4.5f);
        sample.AgeAt(99f).Should().Be(-1f);
    }

    [TestMethod]
    public void NoSampleHasNoAgeAndTheDefaultIsNoSample()
    {
        float.IsNaN(VitalsSample.None.AgeAt(10f)).Should().BeTrue();
        float.IsNaN(default(VitalsSample).AgeAt(10f)).Should().BeTrue();
        default(VitalsSample).HasValue.Should().BeFalse();
    }

    // ---- how far

    [TestMethod]
    public void UnderThreeMetresIsHere()
    {
        DistanceText(At(0f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("here");
        DistanceText(At(2.99f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("here");
    }

    [TestMethod]
    public void ThreeMetresIsNotHere()
    {
        DistanceText(At(3f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("3 m away");
    }

    [TestMethod]
    public void TheDistanceIsTheNearestMetre()
    {
        DistanceText(At(3.4f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("3 m away");
        DistanceText(At(142.5f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("143 m away");
        DistanceText(At(142.49f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("142 m away");
    }

    [TestMethod]
    public void AThousandMetresIsNeverWrittenAsMetres()
    {
        DistanceText(At(999.4f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("999 m away");
        DistanceText(At(999.5f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("1.0 km away");
        DistanceText(At(1000f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("1.0 km away");
    }

    [TestMethod]
    public void KilometresHaveOneDecimal()
    {
        DistanceText(At(1049f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("1.0 km away");
        DistanceText(At(1050f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("1.1 km away");
        DistanceText(At(5678f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("5.7 km away");
        DistanceText(At(12340f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("12.3 km away");
    }

    [TestMethod]
    public void FarAwayIsCappedAtOneHundredKilometres()
    {
        DistanceText(At(99949f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("99.9 km away");
        DistanceText(At(99950f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("100+ km away");
        DistanceText(At(1e9f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("100+ km away");
    }

    [TestMethod]
    public void OppositeHugeCoordinatesAreFarAndNotUnknown()
    {
        // the difference of two floats that big is a number, just a huge one: "100+ km", not "?" (the double math in Distance and the infinity branch both end here, so this pins the text only)
        DistanceText(At(3e38f, 0f, 0f), At(-3e38f, 0f, 0f), L).Should().Be("100+ km away");
    }

    [TestMethod]
    public void TheDistanceIsThreeDimensional()
    {
        // 3-4-12 makes 13 m
        DistanceText(At(3f, 4f, 12f), At(0f, 0f, 0f), L).Should().Be("13 m away");
    }

    [TestMethod]
    public void TheDistanceIsTheSameFromEitherEnd()
    {
        Point3 a = At(10f, -30f, 5f);
        Point3 b = At(-4f, 12f, 99f);

        Distance(a, b).Should().Be(Distance(b, a));
        DistanceText(a, b, L).Should().Be(DistanceText(b, a, L));
    }

    [TestMethod]
    public void AnUnknownPositionMakesTheDistanceUnknown()
    {
        DistanceText(Point3.Unknown, At(0f, 0f, 0f), L).Should().Be("? away");
        DistanceText(At(0f, 0f, 0f), Point3.Unknown, L).Should().Be("? away");
        DistanceText(At(float.PositiveInfinity, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("? away");
    }

    [TestMethod]
    public void TheDistanceIsWrittenTheSameInEveryCulture()
    {
        // only integers are written (their text does not depend on the culture); this pins that no culture-dependent number format creeps in
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            DistanceText(At(1234f, 0f, 0f), At(0f, 0f, 0f), L).Should().Be("1.2 km away");
            VitalValue(79.5f, stale: false).Should().Be("80");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // ---- how deep

    [TestMethod]
    public void TheDepthIsFlooredLikeTheCompassOfTheHud()
    {
        DepthText(At(0f, -143.7f, 0f), 0f, L).Should().Be("Depth 143 m");
        DepthText(At(0f, -1f, 0f), 0f, L).Should().Be("Depth 1 m");
    }

    [TestMethod]
    public void WithinOneMetreOfTheSeaIsTheSurface()
    {
        DepthText(At(0f, -0.99f, 0f), 0f, L).Should().Be("Surface");
        DepthText(At(0f, 0f, 0f), 0f, L).Should().Be("Surface");
        DepthText(At(0f, 0.99f, 0f), 0f, L).Should().Be("Surface");
    }

    [TestMethod]
    public void HigherThanOneMetreIsAboveWater()
    {
        DepthText(At(0f, 1f, 0f), 0f, L).Should().Be("Above water");
        DepthText(At(0f, 25f, 0f), 0f, L).Should().Be("Above water");
    }

    [TestMethod]
    public void TheOceanLevelShiftsTheDepth()
    {
        DepthText(At(0f, -5f, 0f), 10f, L).Should().Be("Depth 15 m");
    }

    [TestMethod]
    public void AMissingOceanLevelIsZero()
    {
        DepthText(At(0f, -20f, 0f), float.NaN, L).Should().Be("Depth 20 m");
    }

    [TestMethod]
    public void AnUnknownPositionHasAnUnknownDepth()
    {
        DepthText(Point3.Unknown, 0f, L).Should().Be("Depth ?");
    }

    [TestMethod]
    public void AnAbsurdDepthIsCapped()
    {
        DepthText(At(0f, -1e9f, 0f), 0f, L).Should().Be("Depth 9999 m");
    }

    // ---- where

    [TestMethod]
    public void AVehicleIsRecognisedFromTheFlagsOfTheRemotePlayer()
    {
        ClassifyVehicle(hasVehicle: true, isSeamoth: true, isPrawn: false, inCyclops: false, inBase: false, inLifepod: false).Should().Be(RemoteVehicleKind.Seamoth);
        ClassifyVehicle(hasVehicle: true, isSeamoth: false, isPrawn: true, inCyclops: false, inBase: false, inLifepod: false).Should().Be(RemoteVehicleKind.Prawn);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: true, inBase: false, inLifepod: false).Should().Be(RemoteVehicleKind.Cyclops);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: false, inBase: true, inLifepod: false).Should().Be(RemoteVehicleKind.Base);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: false, inBase: false, inLifepod: true).Should().Be(RemoteVehicleKind.Lifepod);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: false, inBase: false, inLifepod: false).Should().Be(RemoteVehicleKind.None);
    }

    [TestMethod]
    public void TheNearestContainerWins()
    {
        // a Seamoth docked in a base is a Seamoth; a player in a Cyclops that is in a base is in the Cyclops; a base that holds a lifepod is a base
        ClassifyVehicle(hasVehicle: true, isSeamoth: true, isPrawn: false, inCyclops: true, inBase: true, inLifepod: false).Should().Be(RemoteVehicleKind.Seamoth);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: true, inBase: true, inLifepod: false).Should().Be(RemoteVehicleKind.Cyclops);
        ClassifyVehicle(hasVehicle: false, isSeamoth: false, isPrawn: false, inCyclops: false, inBase: true, inLifepod: true).Should().Be(RemoteVehicleKind.Base);
    }

    [TestMethod]
    public void AVehicleOfAnotherKindIsUnknownAndNotNone()
    {
        ClassifyVehicle(hasVehicle: true, isSeamoth: false, isPrawn: false, inCyclops: false, inBase: false, inLifepod: false).Should().Be(RemoteVehicleKind.Unknown);
    }

    [TestMethod]
    public void TheZeroValueOfTheKindIsUnknown()
    {
        default(RemoteVehicleKind).Should().Be(RemoteVehicleKind.Unknown);
    }

    [TestMethod]
    public void EveryPlaceHasItsOwnWordAndNoVehicleHasNone()
    {
        VehicleText(RemoteVehicleKind.Seamoth, L).Should().Be("Seamoth");
        VehicleText(RemoteVehicleKind.Prawn, L).Should().Be("Prawn suit");
        VehicleText(RemoteVehicleKind.Cyclops, L).Should().Be("Cyclops");
        VehicleText(RemoteVehicleKind.Base, L).Should().Be("In a base");
        VehicleText(RemoteVehicleKind.Lifepod, L).Should().Be("Lifepod");
        // a line that says "no vehicle" is a line that can be wrong: it says nothing
        VehicleText(RemoteVehicleKind.None, L).Should().BeEmpty();
        VehicleText(RemoteVehicleKind.Unknown, L).Should().BeEmpty();
    }

    // ---- which vitals

    [TestMethod]
    public void SurvivalAndHardcoreShowEverything()
    {
        PartsFor(SubnauticaGameMode.SURVIVAL).Should().Be(VitalsParts.All);
        PartsFor(SubnauticaGameMode.HARDCORE).Should().Be(VitalsParts.All);
    }

    [TestMethod]
    public void FreedomShowsOnlyTheHealthAndCreativeNothing()
    {
        // food and water stand still where the game does not require survival
        PartsFor(SubnauticaGameMode.FREEDOM).Should().Be(VitalsParts.Health);
        PartsFor(SubnauticaGameMode.CREATIVE).Should().Be(VitalsParts.None);
    }

    [TestMethod]
    public void AnUndefinedModeShowsEverything()
    {
        PartsFor((SubnauticaGameMode)12345).Should().Be(VitalsParts.All);
    }

    // ---- the lines

    [TestMethod]
    public void TheLinesHaveTheAgreedShape()
    {
        StatsLine line = Format(Snapshot(remote: At(3f, -45f, 4f), local: At(0f, -45f, 0f), vehicle: RemoteVehicleKind.Seamoth), L);

        line.Vitals.Should().Be("HP 80 | Food 55 | Water 71");
        line.Place.Should().Be("5 m away | Depth 45 m");
        line.Vehicle.Should().Be("Seamoth");
        line.Text.Should().Be("HP 80 | Food 55 | Water 71\n5 m away | Depth 45 m\nSeamoth");
    }

    [TestMethod]
    public void NoVehicleLeavesNoVehicleLineAndNoDanglingBreak()
    {
        StatsLine line = Format(Snapshot(vehicle: RemoteVehicleKind.None), L);

        line.Place.Should().Be("5 m away | Depth 45 m");
        line.Vehicle.Should().BeEmpty();
        line.Text.Should().Be("HP 80 | Food 55 | Water 71\n5 m away | Depth 45 m");
        line.Text.Should().NotEndWith("\n");
    }

    [TestMethod]
    public void TheDefaultLineIsTheNoStatsLineAndEveryReaderOfItWorks()
    {
        // the rows are given default for a name only, and the Debug probe reads the parts of what a row was given: null parts there threw a NullReferenceException in the second live run
        StatsLine line = default;

        line.Vitals.Should().BeEmpty();
        line.Place.Should().BeEmpty();
        line.Vehicle.Should().BeEmpty();
        line.Text.Should().BeEmpty();
        new StatsLine(null, null, null).Text.Should().BeEmpty();
    }

    [TestMethod]
    public void TheVehicleHasALineOfItsOwnSoThatNoLineHasMoreThanTwoParts()
    {
        // the first live run: three parts on one line ("here | Above water | Lifepod") reached the Nitrox buttons next to the row
        StatsLine line = Format(Snapshot(vehicle: RemoteVehicleKind.Prawn), L);

        foreach (string row in line.Text.Split('\n'))
        {
            row.Split(new[] { SEPARATOR }, StringSplitOptions.None).Length.Should().BeLessThan(4);
        }

        line.Place.Should().NotContain("Prawn");
        line.Vehicle.Should().Be("Prawn suit");
    }

    [TestMethod]
    public void AnUnknownSnapshotSaysQuestionMarksEverywhere()
    {
        StatsLine line = Format(StatsSnapshot.Unknown, L);

        line.Vitals.Should().Be("HP ? | Food ? | Water ?");
        line.Place.Should().Be("? away | Depth ?");
    }

    [TestMethod]
    public void NoVitalsYetIsUnknownAndNotStale()
    {
        StatsLine line = Format(StatsSnapshot.Create(VitalsSample.None, 1000f, At(0f, -10f, 0f), At(0f, -10f, 0f), 0f, RemoteVehicleKind.None, VitalsParts.All), L);

        line.Vitals.Should().Be("HP ? | Food ? | Water ?");
        line.Vitals.Should().NotContain("~");
    }

    [TestMethod]
    public void StaleVitalsAreMarkedAndTheRestOfTheLineIsNot()
    {
        StatsLine line = Format(Snapshot(age: 12f), L);

        line.Vitals.Should().Be("HP ~80 | Food ~55 | Water ~71");
        line.Place.Should().NotContain("~");
    }

    [TestMethod]
    public void VitalsExactlyAtTheLimitAreStale()
    {
        Format(Snapshot(age: 9.99f), L).Vitals.Should().NotContain("~");
        Format(Snapshot(age: 10f), L).Vitals.Should().Contain("~");
    }

    [TestMethod]
    public void ACreativePlayerHasNoVitalsLineAndTheTextIsThePlaceOnly()
    {
        StatsLine line = Format(Snapshot(remote: At(0f, 0f, 0f), local: At(0f, 0f, 0f), parts: VitalsParts.None), L);

        line.Vitals.Should().BeEmpty();
        line.Text.Should().Be("here | Surface");
        line.Text.Should().NotStartWith("\n");
    }

    [TestMethod]
    public void AFreedomPlayerShowsOnlyTheHealth()
    {
        Format(Snapshot(parts: VitalsParts.Health), L).Vitals.Should().Be("HP 80");
    }

    [TestMethod]
    public void OneUnknownVitalKeepsTheOthers()
    {
        Format(Snapshot(food: float.NaN), L).Vitals.Should().Be("HP 80 | Food ? | Water 71");
    }

    [TestMethod]
    public void TheSameInputGivesTheSameStringEveryTime()
    {
        string first = Format(Snapshot(), L).Text;
        string second = Format(Snapshot(), L).Text;

        string.Equals(first, second, StringComparison.Ordinal).Should().BeTrue();
    }

    [TestMethod]
    public void ATenthOfAPointOfFoodDoesNotChangeTheText()
    {
        // 54.2 and 54.9 are both 55 on the bar: the label is not rebuilt for a change nobody can read
        Format(Snapshot(food: 54.2f), L).Text.Should().Be(Format(Snapshot(food: 54.9f), L).Text);
    }

    [TestMethod]
    public void TheTextIsPlainAsciiWithoutMarkup()
    {
        string text = Format(Snapshot(vehicle: RemoteVehicleKind.Prawn), L).Text;

        text.Should().NotContain("<");
        text.All(character => character < 128).Should().BeTrue();
    }

    [TestMethod]
    public void TheLinesCanBeBuiltFromAnotherLanguage()
    {
        StatsLabels german = StatsLabels.Resolve((key, fallback) => key switch
        {
            Keys.HEALTH => "LP {0}",
            Keys.DISTANCE => "{0} entfernt",
            Keys.SEAMOTH => "Seemotte",
            _ => fallback
        });

        StatsLine line = Format(Snapshot(vehicle: RemoteVehicleKind.Seamoth), german);

        line.Vitals.Should().StartWith("LP 80");
        line.Place.Should().Be("5 m entfernt | Depth 45 m");
        line.Vehicle.Should().Be("Seemotte");
    }

    // ---- the words

    [TestMethod]
    public void WithoutALookupTheWordsAreEnglish()
    {
        StatsLabels.Resolve(null).Should().BeEquivalentTo(StatsLabels.English);
    }

    [TestMethod]
    public void ALookupThatAnswersWithTheFallbackGivesEnglish()
    {
        StatsLabels.Resolve((_, fallback) => fallback).Should().BeEquivalentTo(StatsLabels.English);
    }

    [TestMethod]
    public void EveryKeyIsAskedExactlyOnce()
    {
        List<string> asked = [];

        StatsLabels.Resolve((key, fallback) =>
        {
            asked.Add(key);
            return fallback;
        });

        asked.Should().HaveCount(15);
        asked.Should().OnlyHaveUniqueItems();
        asked.Should().BeEquivalentTo(typeof(PlayerListStatsRules.Keys).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (string)field.GetRawConstantValue()));
    }

    [TestMethod]
    public void ATranslationIsUsed()
    {
        StatsLabels.Resolve((key, fallback) => key == Keys.HEALTH ? "Vie {0}" : fallback).Health.Should().Be("Vie {0}");
    }

    [TestMethod]
    public void AnEmptyOrMissingAnswerFallsBackToEnglish()
    {
        StatsLabels.Resolve((_, _) => null).Should().BeEquivalentTo(StatsLabels.English);
        StatsLabels.Resolve((_, _) => string.Empty).Should().BeEquivalentTo(StatsLabels.English);
        StatsLabels.Resolve((_, _) => "   ").Should().BeEquivalentTo(StatsLabels.English);
    }

    [TestMethod]
    public void ABrokenFormatFallsBackToEnglish()
    {
        StatsLabels.Resolve((key, fallback) => key == Keys.HEALTH ? "HP {0" : fallback).Health.Should().Be("HP {0}");
        StatsLabels.Resolve((key, fallback) => key == Keys.HEALTH ? "HP" : fallback).Health.Should().Be("HP {0}");
        StatsLabels.Resolve((key, fallback) => key == Keys.HEALTH ? "HP {1}" : fallback).Health.Should().Be("HP {0}");
    }

    [TestMethod]
    public void AWordIsTrimmed()
    {
        StatsLabels.Resolve((key, fallback) => key == Keys.SEAMOTH ? "  Seemotte " : fallback).Seamoth.Should().Be("Seemotte");
    }

    [TestMethod]
    public void EveryNitroxKeyIsInTheEnglishLanguageFileAndSaysWhatTheFallbackSays()
    {
        string path = Path.Combine(NitroxUser.AssetsPath, "LanguageFiles", "en.json");
        File.Exists(path).Should().BeTrue();
        using StreamReader reader = new(path);
        JsonData english = JsonMapper.ToObject(reader);
        List<string> missing = [];
        foreach (FieldInfo field in typeof(PlayerListStatsRules.Keys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string key = (string)field.GetRawConstantValue();
            if (key.StartsWith("Nitrox_", StringComparison.Ordinal) && !english.Keys.Contains(key))
            {
                missing.Add(key);
            }
        }

        missing.Should().BeEmpty();
        // the words in the file and the fallbacks in the code are the same, so that a missing file behaves like the English one
        StatsLabels.Resolve((key, fallback) => english.Keys.Contains(key) ? (string)english[key] : fallback).Should().BeEquivalentTo(StatsLabels.English);
    }

    [TestMethod]
    public void TheDepthIsTheRemotePlayersAndTheOceanLevelIsTheSnapshots()
    {
        // remote 80 down, local 10 down, the sea at +5: 70 m apart, the remote player is 85 m below the sea (a mix-up of remote and local, or a lost ocean level, changes the text)
        Format(Snapshot(remote: At(0f, -80f, 0f), local: At(0f, -10f, 0f), ocean: 5f), L).Place.Should().Be("70 m away | Depth 85 m");
    }

    [TestMethod]
    public void ATranslationWithASecondPlaceholderFallsBackToEnglish()
    {
        // "{0}" is in it, so the substring test passes, but string.Format throws: the throw is what the fallback catches
        StatsLabels.Resolve((key, fallback) => key == Keys.HEALTH ? "HP {0} {1}" : fallback).Health.Should().Be("HP {0}");
    }

    [TestMethod]
    public void ThePrawnSuitAndTheCyclopsKeepTheirOwnWords()
    {
        StatsLabels labels = StatsLabels.Resolve((key, fallback) => key == Keys.PRAWN ? "P" : key == Keys.CYCLOPS ? "C" : fallback);

        labels.Prawn.Should().Be("P");
        labels.Cyclops.Should().Be("C");
        labels.Seamoth.Should().Be("Seamoth");
    }

    [TestMethod]
    public void ADefaultPositionIsNotAPositionAndADefaultSnapshotShowsNothingItDoesNotKnow()
    {
        default(Point3).IsKnown.Should().BeFalse();

        StatsLine line = Format(default, L);

        line.Vitals.Should().BeEmpty();
        line.Place.Should().Be("? away | Depth ?");
    }

    // ---- when to rebuild

    [TestMethod]
    public void NothingIsRebuiltWhileTheTabIsNotShown()
    {
        ShouldRefresh(now: 100f, lastRefresh: 0f, interval: 1f, tabIsShown: false).Should().BeFalse();
        ShouldRefresh(now: 100f, lastRefresh: float.NegativeInfinity, interval: 1f, tabIsShown: false).Should().BeFalse();
    }

    [TestMethod]
    public void TheIntervalIsRespectedToTheEdge()
    {
        ShouldRefresh(now: 10.999f, lastRefresh: 10f, interval: 1f, tabIsShown: true).Should().BeFalse();
        ShouldRefresh(now: 11f, lastRefresh: 10f, interval: 1f, tabIsShown: true).Should().BeTrue();
        ShouldRefresh(now: 10f, lastRefresh: 10f, interval: 1f, tabIsShown: true).Should().BeFalse();
    }

    [TestMethod]
    public void TheFirstShowRefreshesAtOnce()
    {
        ShouldRefresh(now: 3f, lastRefresh: float.NegativeInfinity, interval: 1f, tabIsShown: true).Should().BeTrue();
        ShouldRefresh(now: 3f, lastRefresh: float.NaN, interval: 1f, tabIsShown: true).Should().BeTrue();
    }

    [TestMethod]
    public void AClockThatWentBackRefreshesOnceAndThenIsAnchoredAgain()
    {
        ShouldRefresh(now: 5f, lastRefresh: 50f, interval: 1f, tabIsShown: true).Should().BeTrue();
        // the caller sets last = now: from there it counts again
        ShouldRefresh(now: 5.5f, lastRefresh: 5f, interval: 1f, tabIsShown: true).Should().BeFalse();
        ShouldRefresh(now: 6f, lastRefresh: 5f, interval: 1f, tabIsShown: true).Should().BeTrue();
    }

    [TestMethod]
    public void AClockThatIsNotANumberNeverRefreshes()
    {
        ShouldRefresh(now: float.NaN, lastRefresh: 0f, interval: 1f, tabIsShown: true).Should().BeFalse();
        ShouldRefresh(now: float.PositiveInfinity, lastRefresh: 0f, interval: 1f, tabIsShown: true).Should().BeFalse();
    }

    [TestMethod]
    public void ABrokenIntervalIsOneSecond()
    {
        ShouldRefresh(now: 10.5f, lastRefresh: 10f, interval: 0f, tabIsShown: true).Should().BeFalse();
        ShouldRefresh(now: 11f, lastRefresh: 10f, interval: -5f, tabIsShown: true).Should().BeTrue();
        ShouldRefresh(now: 10.5f, lastRefresh: 10f, interval: float.NaN, tabIsShown: true).Should().BeFalse();
        ShouldRefresh(now: 11f, lastRefresh: 10f, interval: float.NaN, tabIsShown: true).Should().BeTrue();
    }

    [TestMethod]
    public void SixtySecondsAt60FramesAreSixtyRefreshesAndTheHiddenTimeCostsNothing()
    {
        // what uGUI_PlayerListTab.OnLateUpdate does: hidden resets the last refresh, shown refreshes when due
        float last = float.NegativeInfinity;
        int refreshes = 0;
        int refreshesWhileHidden = 0;
        for (int frame = 0; frame < 60 * 60; frame++)
        {
            float now = frame / 60f;
            bool shown = frame < 20 * 60 || frame >= 40 * 60;
            if (!shown)
            {
                last = float.NegativeInfinity;
            }

            if (ShouldRefresh(now, last, REFRESH_INTERVAL, shown))
            {
                last = now;
                refreshes++;
                if (!shown)
                {
                    refreshesWhileHidden++;
                }
            }
        }

        refreshesWhileHidden.Should().Be(0);
        // 20 s + 20 s shown, and the first frame after the gap refreshes at once
        refreshes.Should().BeInRange(40, 42);
    }

    [TestMethod]
    public void SlowFramesRefreshOncePerFrameAndNeverInABurst()
    {
        float last = float.NegativeInfinity;
        int refreshes = 0;
        for (float now = 0f; now < 25f; now += 5f)
        {
            if (ShouldRefresh(now, last, REFRESH_INTERVAL, true))
            {
                last = now;
                refreshes++;
            }
        }

        refreshes.Should().Be(5);
    }

    // ---- when to assign the text

    [TestMethod]
    public void TheSameTextIsNotAssignedAgain()
    {
        TextGate gate = new();

        gate.Changed("HP 80").Should().BeTrue();
        gate.Changed(new string("HP 80".ToCharArray())).Should().BeFalse();
    }

    [TestMethod]
    public void ADifferentTextIsAssigned()
    {
        TextGate gate = new();
        gate.Changed("HP 80");

        gate.Changed("HP 79").Should().BeTrue();
        gate.Changed("HP 79").Should().BeFalse();
    }

    [TestMethod]
    public void AResetAssignsTheNextTextEvenIfItIsTheSame()
    {
        TextGate gate = new();
        gate.Changed("HP 80");
        gate.Reset();

        gate.Changed("HP 80").Should().BeTrue();
    }

    [TestMethod]
    public void NullIsTheEmptyTextAndTheFirstTextIsAlwaysAssigned()
    {
        TextGate gate = new();

        gate.Changed(null).Should().BeTrue();
        gate.Changed(string.Empty).Should().BeFalse();
    }

    [TestMethod]
    public void TheGateIsCaseSensitive()
    {
        TextGate gate = new();
        gate.Changed("here");

        gate.Changed("Here").Should().BeTrue();
    }

    // ---- the height of a row

    [TestMethod]
    public void ARowIsAsTallAsItsTextPlusTheMargin()
    {
        RowHeight(oneLine: 40f, textHeight: 90f, margin: 5f).Should().Be(100f);
    }

    [TestMethod]
    public void ARowIsNeverLowerThanAOneLineRow()
    {
        RowHeight(oneLine: 40f, textHeight: 10f, margin: 5f).Should().Be(40f);
    }

    [TestMethod]
    public void ARowKeepsItsHeightWhenTheTextCannotBeMeasured()
    {
        RowHeight(oneLine: 40f, textHeight: float.NaN, margin: 5f).Should().Be(40f);
        RowHeight(oneLine: 40f, textHeight: 90f, margin: float.NaN).Should().Be(40f);
    }

    // ---- a point

    [TestMethod]
    public void OnlyThreeNumbersAreAPosition()
    {
        At(1f, 2f, 3f).IsKnown.Should().BeTrue();
        At(1f, float.NaN, 3f).IsKnown.Should().BeFalse();
        At(1f, 2f, float.PositiveInfinity).IsKnown.Should().BeFalse();
        Point3.Unknown.IsKnown.Should().BeFalse();
    }
}
