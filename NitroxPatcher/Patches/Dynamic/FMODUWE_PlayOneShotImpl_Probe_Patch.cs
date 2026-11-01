#if DEBUG
using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>Docking sound probe, see <see cref="FMOD_CustomEmitter_OnPlay_Probe_Patch" />: one-shots at a position.</summary>
public sealed partial class FMODUWE_PlayOneShotImpl_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => FMODUWE.PlayOneShotImpl(default(string), default(Vector3), default(float)));

    public static void Postfix(string eventPath, Vector3 position)
    {
        FMODDockingProbe.Record(eventPath, "oneshot", position);
    }
}
#endif
