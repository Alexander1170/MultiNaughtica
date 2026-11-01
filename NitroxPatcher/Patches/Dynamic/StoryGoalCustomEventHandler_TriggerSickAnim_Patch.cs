using System.Reflection;
using Nitrox.Model.Subnautica.Helper;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The goal of the infection scan (SelfScan4) reveals the infection 14 s after it completes: <c>TriggerSickAnim</c> sets the infection of the local player to 1 and starts
/// the reveal. Every client runs it for a goal that any player completes. A player who is cured (or not infected) has nothing to reveal, and would be made sick again with
/// no way to be cured (the cure ball is switched off by the shared cure goal): see <see cref="InfectionStage.CanReveal" />.
/// </summary>
public sealed partial class StoryGoalCustomEventHandler_TriggerSickAnim_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StoryGoalCustomEventHandler t) => t.TriggerSickAnim());

    public static bool Prefix()
    {
        return Player.main && Player.main.infectedMixin && InfectionStage.CanReveal(Player.main.infectedMixin.GetInfectedAmount());
    }
}
