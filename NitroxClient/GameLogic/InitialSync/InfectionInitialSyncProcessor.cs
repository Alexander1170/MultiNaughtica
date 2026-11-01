using Nitrox.Model.Subnautica.Helper;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic.InitialSync.Abstract;
using Story;

namespace NitroxClient.GameLogic.InitialSync;

/// <summary>
/// Brings the infection of the joining player in line with the story goals the team has completed (see <see cref="InfectionStage"/>): vanilla only changes it
/// when a goal completes, so a player who was offline for the cure, or is new, would keep an infection that nothing can cure any more.
/// Runs after both the stats and the goals are applied and reads the live goal set, so a goal that reached the client while it was joining counts too.
/// </summary>
public sealed class InfectionInitialSyncProcessor : InitialSyncProcessor
{
    public InfectionInitialSyncProcessor()
    {
        AddDependency<PlayerInitialSyncProcessor>();
        AddDependency<StoryGoalInitialSyncProcessor>();

        AddStep(CatchUpInfection);
    }

    private static void CatchUpInfection(InitialPlayerSync packet)
    {
        StoryGoalManager goalManager = StoryGoalManager.main;
        if (!goalManager || !Player.main || !Player.main.infectedMixin)
        {
            return;
        }

        InfectedMixin infectedMixin = Player.main.infectedMixin;
        float current = infectedMixin.GetInfectedAmount();
        float caughtUp = InfectionStage.CatchUp(current, goalManager.completedGoals);
        if (UnityEngine.Mathf.Approximately(current, caughtUp))
        {
            return;
        }

        Log.Info($"Infection of the joining player brought in line with the story: {current:0.00} -> {caughtUp:0.00}");
        infectedMixin.SetInfectedAmount(caughtUp);
        if (caughtUp >= 1f && InfectionStage.IsRevealed(goal => goalManager.completedGoals.Contains(goal)))
        {
            Player.main.infectionRevealed = true;
        }
    }
}
