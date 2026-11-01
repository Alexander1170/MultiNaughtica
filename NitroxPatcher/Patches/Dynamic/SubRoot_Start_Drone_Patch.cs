using System.Reflection;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Every real Cyclops gets the drone bay (<see cref="DroneBay" />, the console in the engine room and the dock on the hull with the Cyclops Drone Bay module). A fourth patch on <c>SubRoot.Start</c> next to
/// <see cref="SubRoot_Start_Patch" />, <see cref="SubRoot_Start_Gunner_Patch" /> and <see cref="SubRoot_Start_Engineer_Patch" />, so that the merged stations stay as they were reviewed. The copy of a Cyclops that
/// Nitrox walks the player around in (<see cref="VirtualCyclops" />) gets none.
/// </summary>
public sealed partial class SubRoot_Start_Drone_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.Start());

    public static void Postfix(SubRoot __instance)
    {
        if (__instance.isCyclops && !__instance.GetComponent<VirtualCyclops>())
        {
            __instance.gameObject.EnsureComponent<DroneBay>();
        }
    }
}
