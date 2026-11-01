using System.Reflection;
using NitroxClient.GameLogic.PlayerLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     <c>uGUI_SceneIntro</c> checks the hold of the skip button twice per frame, once in <c>HandleInput</c> (patched by <see cref="uGUI_SceneIntro_HandleInput_Patch" />, which is where Nitrox ends
///     the intro for the player) and once in <c>OnUpdate</c>, which calls <c>Stop(true)</c> itself. Which of the two runs first in a frame is up to Unity: when <c>OnUpdate</c> does, the skip
///     bypasses the Nitrox one (the intro stops, but the player is never marked as past it, the partner stays drawn as a copy and the lifepod keeps the fire of the intro). Both run the same code now.
/// </summary>
public sealed partial class uGUI_SceneIntro_OnUpdate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_SceneIntro t) => t.OnUpdate());

    public static bool Prefix(uGUI_SceneIntro __instance)
    {
        // The test of the original: the hint is armed (skipHintStartTime >= 0) and the button was held for the time the game asks for
        if (IntroSequenceRules.IsSkipRequested(__instance.skipHintStartTime, GameInput.GetButtonHeldTime(uGUI_SceneIntro.skipButton), uGUI_SceneIntro.skipTimeout))
        {
            uGUI_SceneIntro_HandleInput_Patch.HandleButtonHeld(__instance);
            return false;
        }

        return true;
    }
}
