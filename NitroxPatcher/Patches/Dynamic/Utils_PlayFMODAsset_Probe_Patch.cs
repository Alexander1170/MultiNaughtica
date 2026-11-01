#if DEBUG
using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>Docking sound probe, see <see cref="FMOD_CustomEmitter_OnPlay_Probe_Patch" />: assets played at the local player (no position of their own).</summary>
public sealed partial class Utils_PlayFMODAsset_Probe_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => Utils.PlayFMODAsset(default(FMODAsset)));

    public static void Postfix(FMODAsset asset)
    {
        FMODDockingProbe.Record(asset ? asset.path : null, "asset", Player.main ? Player.main.transform.position : UnityEngine.Vector3.zero);
    }
}
#endif
