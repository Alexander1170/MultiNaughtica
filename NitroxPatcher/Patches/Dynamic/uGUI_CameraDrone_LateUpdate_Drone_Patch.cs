using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;
using Mathf = UnityEngine.Mathf;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The static and the black fade of the picture of a scout drone follow the server's range (<see cref="ScoutDroneRules.Noise" /> and <see cref="ScoutDroneRules.Fader" />) instead of the game's fixed 250 and
/// 520 metres. Done after the game's own update of the HUD, with the game's fade of the connection kept (the picture still fades in when the control starts): the effect on the screen reads the HUD's <c>noise</c>
/// in its pre-render.
/// </summary>
public sealed partial class uGUI_CameraDrone_LateUpdate_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CameraDrone t) => t.LateUpdate());

    public static void Postfix(uGUI_CameraDrone __instance)
    {
        MapRoomCamera camera = __instance.activeCamera;
        if (!camera || !ScoutDrone.Of(camera, out _) || !__instance.fader)
        {
            return;
        }
        float distance = camera.GetScreenDistance(null);
        float range = VehicleUpgradeSettings.DroneRange;
        // the game's connection fade, as its own code computes it
        var sequence = __instance.faderSequence;
        float noiseConnect = sequence.active ? 0.5f * (1f - Mathf.Cos(Mathf.PI * sequence.t)) : 0f;
        float faderConnect = sequence.active ? noiseConnect : (sequence.target ? 1f : 0f);
        __instance._noise = Mathf.Max(noiseConnect, ScoutDroneRules.Noise(distance, range, VehicleUpgradeSettings.DroneStaticStart));
        Color color = __instance.fader.color;
        color.a = Mathf.Max(faderConnect, ScoutDroneRules.Fader(distance, range));
        __instance.fader.color = color;
    }
}
