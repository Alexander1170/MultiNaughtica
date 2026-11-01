using System.Collections;
using System.Reflection;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Handles cleanup of temporary babies created for animation on non-simulating players.
/// Only the real networked baby (from simulating player) should call SwimToMother().
/// Temporary babies are destroyed after the animation completes, or after
/// <see cref="NitroxClient.GameLogic.SeaEmperorBabyRules.TEMPORARY_BABY_LIFETIME_SECONDS" /> if the animation never ends: the end of the animation is an animation event, and the
/// animator of an egg that nobody looks at does not advance, so in the first live run 2 of 5 temporary babies were still there next to the real ones after 6 minutes
/// (<see cref="EndTemporaryBabyLater" />, started by <see cref="IncubatorEgg_HatchNow_Patch" />).
/// </summary>
public sealed partial class IncubatorEggAnimation_OnHatchAnimationEnd_Patch : NitroxPatch, IDynamicPatch
{
    internal const string TEMPORARY_BABY_MARKER = "_NitroxTemporary";

    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((IncubatorEggAnimation t) => t.OnHatchAnimationEnd());

    public static bool Prefix(IncubatorEggAnimation __instance)
    {
        // Safety check - ensure we have a baby reference
        if (!__instance.baby)
        {
            return true; // Let original method handle this case
        }

        // Check if this is a temporary baby (created for animation only)
        if (__instance.baby.name.Contains(TEMPORARY_BABY_MARKER))
        {
            // For temporary babies, we only want to end the cinematic mode and cleanup
            // Don't call SwimToMother() as that should only happen for the real networked baby
            EndTemporaryBaby(__instance);
            return false; // Skip the original method
        }

        // For real networked babies, let the original method run (which calls SwimToMother)
        return true;
    }

    /// <summary>
    /// Ends the animation of the temporary baby of <paramref name="animation" /> and destroys the baby. The real networked one will be spawned via server broadcast.
    /// </summary>
    internal static void EndTemporaryBaby(IncubatorEggAnimation animation)
    {
        if (animation.baby.cinematicController)
        {
            animation.baby.cinematicController.SetCinematicMode(false);
        }

        // Set animationActive to false (field is publicized via BepInEx.AssemblyPublicizer)
        animation.animationActive = false;

        Object.Destroy(animation.baby.gameObject);
    }

    /// <summary>
    /// The safety net of the temporary baby <paramref name="baby" />: when <see cref="SeaEmperorBabyRules.TemporaryBabyExpired" /> says so it is gone, whether or not the egg animation ended (checked once a
    /// second; the coroutine ends with the baby when the animation does end in time). A baby that the egg's animation did not take (<c>IncubatorEggAnimation.StartHatchAnimation</c> returns at once while an
    /// earlier animation of the egg is active) has nothing that would ever end it and is destroyed.
    /// </summary>
    internal static IEnumerator EndTemporaryBabyLater(IncubatorEggAnimation animation, GameObject baby)
    {
        float age = 0f;
        while (baby && !SeaEmperorBabyRules.TemporaryBabyExpired(age))
        {
            yield return new WaitForSeconds(1f);
            age += 1f;
        }

        if (!baby)
        {
            yield break; // the animation ended in time
        }

        if (animation && animation.baby && animation.baby.gameObject == baby)
        {
            EndTemporaryBaby(animation);
        }
        else
        {
            Object.Destroy(baby);
        }
    }
}
