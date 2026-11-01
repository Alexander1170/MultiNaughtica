using System.Reflection;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Every real Cyclops gets the engineer console (<see cref="EngineerConsole" />, the console in the engine room with the Cyclops Engineer Console module). A third patch on <c>SubRoot.Start</c> next to
/// <see cref="SubRoot_Start_Patch" /> and <see cref="SubRoot_Start_Gunner_Patch" /> so that the merged stations stay as they were reviewed. The copy of a Cyclops that Nitrox walks the player around in
/// (<see cref="VirtualCyclops" />) gets none: it would put a hand target on a Cyclops nobody sees.
/// </summary>
public sealed partial class SubRoot_Start_Engineer_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.Start());

    public static void Postfix(SubRoot __instance)
    {
        if (__instance.isCyclops && !__instance.GetComponent<VirtualCyclops>())
        {
            __instance.gameObject.EnsureComponent<EngineerConsole>();
        }
    }
}
