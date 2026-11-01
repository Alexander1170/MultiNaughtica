using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     What the server and the clients decide about the two-player intro and about where a player stands who left before it was over (bug1-intro-join: the second player of a new world sat at a
///     black screen, and after rejoining she walked on the sea floor "inside a base").
/// </summary>
[TestClass]
public class IntroCinematicRulesTest
{
    private static readonly NitroxVector3 podPosition = new(-60f, 0f, 100f);

    // The mode a player gets in the initial sync, in each state of the others

    [TestMethod]
    public void FirstPlayerAlone_WaitsForAPartner()
    {
        IntroCinematicRules.ChooseJoinMode(true, []).Should().Be(IntroCinematicMode.LOADING);
    }

    [TestMethod]
    public void SecondPlayerBeforeTheIntro_IsPairedWithTheOneWhoWaits()
    {
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.WAITING]).Should().Be(IntroCinematicMode.LOADING);
    }

    [TestMethod]
    public void SecondPlayerWhileTheFirstHasNotPressedAKeyYet_StartsLikeAFirstPlayer()
    {
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.LOADING]).Should().Be(IntroCinematicMode.LOADING);
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.NONE]).Should().Be(IntroCinematicMode.LOADING);
    }

    [TestMethod]
    [DataRow(IntroCinematicMode.START)]
    [DataRow(IntroCinematicMode.PLAYING)]
    [DataRow(IntroCinematicMode.SINGLEPLAYER)]
    public void SecondPlayerDuringTheIntro_HasItSkippedBecauseNobodyCanBeHisPartner(IntroCinematicMode otherMode)
    {
        IntroCinematicRules.ChooseJoinMode(true, [otherMode]).Should().Be(IntroCinematicMode.COMPLETED);
    }

    [TestMethod]
    public void SecondPlayerAfterTheIntro_HasItSkipped()
    {
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.COMPLETED]).Should().Be(IntroCinematicMode.COMPLETED);
    }

    [TestMethod]
    public void NewPlayerWithAWaitingPlayerAmongOthers_IsStillPaired()
    {
        // The host is past the intro, but another new player waits: those two can play it together
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.COMPLETED, IntroCinematicMode.PLAYING, IntroCinematicMode.WAITING]).Should().Be(IntroCinematicMode.LOADING);
    }

    [TestMethod]
    public void APlayerTheServerKnows_IsPastTheIntroWhateverTheOthersDo()
    {
        foreach (IntroCinematicMode otherMode in Enum.GetValues<IntroCinematicMode>())
        {
            IntroCinematicRules.ChooseJoinMode(false, [otherMode]).Should().Be(IntroCinematicMode.COMPLETED, $"the other player is {otherMode}");
        }
        IntroCinematicRules.ChooseJoinMode(false, []).Should().Be(IntroCinematicMode.COMPLETED);
    }

    [TestMethod]
    public void CreativeWorld_NobodyEverLeavesLoading_SoNewPlayersKeepLoading()
    {
        // A game that never plays the intro (creative) never reports anything: every player stays LOADING for the server
        IntroCinematicRules.ChooseJoinMode(true, [IntroCinematicMode.LOADING, IntroCinematicMode.LOADING]).Should().Be(IntroCinematicMode.LOADING);
    }

    [TestMethod]
    public void NobodyToPairWith_NeedsSomeoneWhoIsPastAndNoOneWhoWaits()
    {
        IntroCinematicRules.HasNobodyToPairWith([]).Should().BeFalse();
        IntroCinematicRules.HasNobodyToPairWith([IntroCinematicMode.LOADING]).Should().BeFalse();
        IntroCinematicRules.HasNobodyToPairWith([IntroCinematicMode.WAITING]).Should().BeFalse();
        IntroCinematicRules.HasNobodyToPairWith([IntroCinematicMode.PLAYING]).Should().BeTrue();
        IntroCinematicRules.HasNobodyToPairWith([IntroCinematicMode.COMPLETED, IntroCinematicMode.LOADING]).Should().BeTrue();
        IntroCinematicRules.HasNobodyToPairWith([IntroCinematicMode.COMPLETED, IntroCinematicMode.WAITING]).Should().BeFalse();
    }

    // Which players have a pose of the cinematic when they leave

    [TestMethod]
    [DataRow(IntroCinematicMode.WAITING, true)]
    [DataRow(IntroCinematicMode.START, true)]
    [DataRow(IntroCinematicMode.PLAYING, true)]
    [DataRow(IntroCinematicMode.SINGLEPLAYER, true)]
    [DataRow(IntroCinematicMode.COMPLETED, false)]
    [DataRow(IntroCinematicMode.LOADING, false)]
    [DataRow(IntroCinematicMode.NONE, false)]
    public void OnlyAPlayerWhoReportedThatHeIsInTheIntroHasLeftDuringIt(IntroCinematicMode lastMode, bool expected)
    {
        // LOADING is also the mode of every player of a creative game, where the intro never runs: those must not be sent back to their lifepod at each join
        IntroCinematicRules.IsMidIntro(lastMode).Should().Be(expected);
    }

    // Where a player who left stands when he joins again

    [TestMethod]
    public void APlayerWhoLeftDuringTheIntro_IsPutAtTheLifepodWhateverTheServerHasForHim()
    {
        // The pose the server saved is one of the cinematic: right at the lifepod, far from it, or deep under it
        NitroxVector3 atTheSeat = podPosition + new NitroxVector3(0.5f, 1f, 0f);
        NitroxVector3 onTheSeaFloor = podPosition + new NitroxVector3(3f, -40f, 2f);

        IntroCinematicRules.ShouldPlaceAtEscapePod(true, true, atTheSeat, podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldPlaceAtEscapePod(true, true, onTheSeaFloor, podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldPlaceAtEscapePod(true, true, NitroxVector3.Zero, podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldPlaceAtEscapePod(true, false, atTheSeat, podPosition).Should().BeTrue();
    }

    [TestMethod]
    public void APlayerWhoLeftOutsideHisLifepodAfterTheIntro_KeepsHisPosition()
    {
        // Far away on the sea floor, not in the pod any more: where he swam to is where he comes back
        NitroxVector3 swimming = podPosition + new NitroxVector3(300f, -80f, -200f);

        IntroCinematicRules.ShouldPlaceAtEscapePod(false, false, swimming, podPosition).Should().BeFalse();
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, false, NitroxVector3.Zero, podPosition).Should().BeFalse();
    }

    [TestMethod]
    public void APlayerSavedInHisLifepodNearIt_KeepsHisPosition()
    {
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, IntroCinematicRules.GetEscapePodSpawn(podPosition), podPosition).Should().BeFalse();
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, podPosition + new NitroxVector3(4f, 1f, -3f), podPosition).Should().BeFalse();
    }

    [TestMethod]
    public void APlayerSavedInHisLifepodButNotAtIt_IsPutAtTheLifepod()
    {
        // What is left of the case when the server forgot that the player left during the intro (a restart): "in the pod" and 40 m below it is the sea floor; far to the side is nowhere
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, podPosition + new NitroxVector3(1f, -40f, 1f), podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, podPosition + new NitroxVector3(200f, 0f, 0f), podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, podPosition + new NitroxVector3(0f, 80f, 0f), podPosition).Should().BeTrue();
    }

    [TestMethod]
    public void APlayerSavedInHisLifepodWithoutAPosition_GetsTheSpawnOfTheLifepod()
    {
        IntroCinematicRules.ShouldPlaceAtEscapePod(false, true, NitroxVector3.Zero, podPosition).Should().BeTrue();
    }

    // The safety net a moment after the initial sync

    [TestMethod]
    public void PlayerWhoJoinedInHisLifepodAndStandsInIt_IsLeftAlone()
    {
        IntroCinematicRules.ShouldReplaceAfterSync(true, false, IntroCinematicRules.GetEscapePodSpawn(podPosition), podPosition).Should().BeFalse();
        IntroCinematicRules.ShouldReplaceAfterSync(true, false, podPosition + new NitroxVector3(4f, 1f, -3f), podPosition).Should().BeFalse();
    }

    [TestMethod]
    public void PlayerWhoJoinedInHisLifepodButIsInTheOpenWater_IsPutBack()
    {
        // The reported case: in the pod's mode, in open water, he falls to the sea floor and stands "inside a base"
        IntroCinematicRules.ShouldReplaceAfterSync(true, false, podPosition + new NitroxVector3(40f, -2f, 25f), podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldReplaceAfterSync(true, false, podPosition + new NitroxVector3(1f, -60f, 1f), podPosition).Should().BeTrue();
        IntroCinematicRules.ShouldReplaceAfterSync(true, false, NitroxVector3.Zero, podPosition).Should().BeTrue();
    }

    [TestMethod]
    public void PlayerWhoDidNotJoinInHisLifepod_IsNeverMoved()
    {
        // A base, a Cyclops, the open water he swam to before leaving: wherever he is, it is where he belongs
        IntroCinematicRules.ShouldReplaceAfterSync(false, false, podPosition + new NitroxVector3(400f, -80f, 0f), podPosition).Should().BeFalse();
        IntroCinematicRules.ShouldReplaceAfterSync(false, false, NitroxVector3.Zero, podPosition).Should().BeFalse();
    }

    [TestMethod]
    public void PlayerInACinematic_IsNeverMoved()
    {
        // The cinematic of the intro puts the player on the animated pod and elsewhere: not for a placement to judge
        IntroCinematicRules.ShouldReplaceAfterSync(true, true, podPosition + new NitroxVector3(300f, 120f, -200f), podPosition).Should().BeFalse();
    }

    [TestMethod]
    public void TheEdgeOfTheLifepod_IsInclusive()
    {
        IntroCinematicRules.IsPoseInEscapePod(podPosition + new NitroxVector3(IntroCinematicRules.ESCAPE_POD_MAX_HORIZONTAL_DISTANCE, 0f, 0f), podPosition).Should().BeTrue();
        IntroCinematicRules.IsPoseInEscapePod(podPosition + new NitroxVector3(IntroCinematicRules.ESCAPE_POD_MAX_HORIZONTAL_DISTANCE + 0.5f, 0f, 0f), podPosition).Should().BeFalse();
        IntroCinematicRules.IsPoseInEscapePod(podPosition + new NitroxVector3(0f, -IntroCinematicRules.ESCAPE_POD_MAX_BELOW, 0f), podPosition).Should().BeTrue();
        IntroCinematicRules.IsPoseInEscapePod(podPosition + new NitroxVector3(0f, -IntroCinematicRules.ESCAPE_POD_MAX_BELOW - 0.5f, 0f), podPosition).Should().BeFalse();
        IntroCinematicRules.IsPoseInEscapePod(podPosition + new NitroxVector3(0f, IntroCinematicRules.ESCAPE_POD_MAX_ABOVE, 0f), podPosition).Should().BeTrue();
    }

    [TestMethod]
    public void TheSpawnOfTheLifepod_IsInTheLifepod()
    {
        NitroxVector3 spawn = IntroCinematicRules.GetEscapePodSpawn(podPosition);

        spawn.Should().Be(podPosition + new NitroxVector3(0.9f, 2.1f, 0f));
        IntroCinematicRules.IsPoseInEscapePod(spawn, podPosition).Should().BeTrue();
        IntroCinematicRules.IsUnknownPosition(spawn).Should().BeFalse();
    }

    [TestMethod]
    public void OnlyTheOriginIsAnUnknownPosition()
    {
        IntroCinematicRules.IsUnknownPosition(NitroxVector3.Zero).Should().BeTrue();
        IntroCinematicRules.IsUnknownPosition(new NitroxVector3(0.0001f, -0.0001f, 0f)).Should().BeTrue();
        IntroCinematicRules.IsUnknownPosition(new NitroxVector3(0f, -1f, 0f)).Should().BeFalse();
    }
}
