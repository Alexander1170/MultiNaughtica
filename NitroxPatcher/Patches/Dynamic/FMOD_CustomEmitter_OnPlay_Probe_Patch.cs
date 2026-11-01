#if DEBUG
using System.Reflection;
using FMOD.Studio;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
///     Docking sound probe (Debug builds only, logging only, changes nothing; #2549, "docking a Seamoth plays for all players"): logs every docking sound that an emitter plays on this client with
///     where it plays and how far the listener is, so that the harness can tell whether a far player really hears it. <see cref="FMODDockingProbe" /> is shared by the three probe patches.
///     Log line: <c>[SyncHarness] sound: event:/... (emitter|asset|oneshot) at (x, y, z), N m from the listener</c>
/// </summary>
public sealed partial class FMOD_CustomEmitter_OnPlay_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((FMOD_CustomEmitter t) => t.OnPlay());

    public static void Postfix(FMOD_CustomEmitter __instance)
    {
        string extra = "";
        if (__instance.asset && !string.IsNullOrEmpty(__instance.asset.path) && __instance.asset.path.IndexOf("dock", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            EventInstance evt = __instance.GetEventInstance();
            evt.getDescription(out EventDescription description);
            description.is3D(out bool is3D);
            description.getMaximumDistance(out float maximumDistance);
            extra = $", 3D {is3D}, audible up to {maximumDistance:F0} m";
        }
        FMODDockingProbe.Record(__instance.asset ? __instance.asset.path : null, "emitter", __instance.transform.position, extra);
    }
}

internal static class FMODDockingProbe
{
    public static void Record(string path, string kind, Vector3 position, string extra = "")
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf("dock", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }
        if (extra == "" && FMODUWE.GetEventDescription(path, out EventDescription eventDescription) == FMOD.RESULT.OK)
        {
            eventDescription.is3D(out bool is3D);
            eventDescription.getMaximumDistance(out float maximumDistance);
            extra = $", 3D {is3D}, audible up to {maximumDistance:F0} m";
        }
        Transform listener = MainCamera.camera ? MainCamera.camera.transform : (Player.main ? Player.main.transform : null);
        float distance = listener ? Vector3.Distance(listener.position, position) : -1f;
        // Where the sound comes from (the first frames outside this probe), for the docking one-shots the far player hears
        string origin = "";
        if (kind == "oneshot")
        {
            string[] frames = new System.Diagnostics.StackTrace(2, false).ToString().Split('\n');
            for (int i = 0; i < frames.Length && i < 6; i++)
            {
                origin += (i == 0 ? " via " : " < ") + frames[i].Trim().Replace("at ", "").Split('(')[0];
            }
        }
        Log.Info($"[SyncHarness] sound: {path} ({kind}) at {position.ToString("F0")}, {distance:F0} m from the listener{extra}{origin}");
    }
}
#endif
