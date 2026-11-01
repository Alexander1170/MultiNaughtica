using static NitroxClient.GameLogic.InitialSync.PlayerPositionInitialSyncProcessor;

namespace NitroxClient.GameLogic.InitialSync;

[TestClass]
public class EscapePodChoiceTest
{
    [TestMethod]
    public void AssignedPodRegistered_IsUsed()
    {
        ChooseEscapePod(assignedIsRegistered: true, sceneHasEscapePod: true).Should().Be(EscapePodSource.Assigned);
    }

    [TestMethod]
    public void AssignedPodMissing_FallsBackToThePodOfTheScene()
    {
        // #2898: the player used to be left frozen outside the pod because the lookup of the pod threw
        ChooseEscapePod(assignedIsRegistered: false, sceneHasEscapePod: true).Should().Be(EscapePodSource.SceneFallback);
    }

    [TestMethod]
    public void NoPodInTheScene_HasNothingToAttachTo()
    {
        ChooseEscapePod(assignedIsRegistered: false, sceneHasEscapePod: false).Should().Be(EscapePodSource.None);
        // The spawn point is the one of the scene's pod, a registered pod does not help without it
        ChooseEscapePod(assignedIsRegistered: true, sceneHasEscapePod: false).Should().Be(EscapePodSource.None);
    }
}
