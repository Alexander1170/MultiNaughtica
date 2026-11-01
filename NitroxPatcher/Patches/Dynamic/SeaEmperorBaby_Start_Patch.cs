using System.Collections;
using System.Linq;
using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// When a Sea Emperor baby is spawned on a remote client (via server broadcast),
/// it needs to swim to mother. The simulating player handles this via the egg animation
/// callback, but remote players receive the baby as a standalone entity.
///
/// This patch ensures the baby swims to mother if (<see cref="SeaEmperorBabyRules.DecideStart" />):
/// 1. The baby is a real baby of the server: not an animation baby (marker in the name) and not the baby of the hatch this player runs
///    (<see cref="HatchedHereBaby" />, or a child of an <c>IncubatorEgg</c>). The test used to be "the baby has no parent", which never holds for a baby of the server: the entity
///    spawner parents every one to the cell root before Start runs, so the baby was never registered with the Emperor and her mother interaction never ran on the second player's screen.
/// 2. The baby doesn't already have a swim target (not already swimming)
/// 3. SeaEmperor.main exists (we're in the prison aquarium); when it is not loaded yet the baby waits for it (<see cref="SeaEmperorBabyRules.EMPEROR_WAIT_SECONDS" />)
/// </summary>
public sealed partial class SeaEmperorBaby_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Start());

    public static void Postfix(SeaEmperorBaby __instance)
    {
        SeaEmperorBabyRules.BabyOrigin origin = SeaEmperorBabyRules.Classify(
            __instance.name.Contains(IncubatorEggAnimation_OnHatchAnimationEnd_Patch.TEMPORARY_BABY_MARKER),
            __instance.GetComponent<HatchedHereBaby>() != null,
            __instance.GetComponentInParent<IncubatorEgg>() != null);

        switch (SeaEmperorBabyRules.DecideStart(origin, SeaEmperor.main != null, HasSwimTarget(__instance)))
        {
            case SeaEmperorBabyRules.StartAction.SwimToMother:
                SwimToMotherNow(__instance);
                break;
            case SeaEmperorBabyRules.StartAction.WaitForEmperor:
                UWE.CoroutineHost.StartCoroutine(WaitForEmperorThenSwim(__instance));
                break;
        }
    }

    private static bool HasSwimTarget(SeaEmperorBaby baby)
    {
        return baby.swimToTarget != null && baby.swimToTarget.target != null;
    }

    private static void SwimToMotherNow(SeaEmperorBaby baby)
    {
        // This is a remotely spawned baby - make it swim to mother
        // Assign a baby ID based on how many babies are already registered
        SeaEmperor emperor = SeaEmperor.main;
        int attachPoints = emperor.babyAttachPoints != null ? emperor.babyAttachPoints.Length : 0;
        int babyId = SeaEmperorBabyRules.NextBabyId(emperor.GetBabies().Count(), attachPoints);
        Log.Info($"A Sea Emperor baby came from the server: it swims to the mother as baby {babyId} of {attachPoints}");
        baby.SetId(babyId);
        baby.SwimToMother();
    }

    private static IEnumerator WaitForEmperorThenSwim(SeaEmperorBaby baby)
    {
        float waited = 0f;
        while (baby && !SeaEmperor.main)
        {
            if (SeaEmperorBabyRules.EmperorWaitOver(waited))
            {
                yield break;
            }

            yield return new WaitForSeconds(1f);
            waited += 1f;
        }

        if (baby && SeaEmperor.main && SeaEmperorBabyRules.DecideStart(SeaEmperorBabyRules.BabyOrigin.SpawnedByServer, true, HasSwimTarget(baby)) == SeaEmperorBabyRules.StartAction.SwimToMother)
        {
            SwimToMotherNow(baby);
        }
    }
}
