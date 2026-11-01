using System.Reflection;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Every real Cyclops gets the gunner console (<see cref="GunnerConsole" />, the right desk of the cockpit with the Cyclops Gunner Station module). A second patch on <c>SubRoot.Start</c> next to
/// <see cref="SubRoot_Start_Patch" /> so that the navigator's own patch stays as it was reviewed. The copy of a Cyclops that Nitrox walks the player around in (<see cref="VirtualCyclops" />) gets none: it
/// would put a hand target on a Cyclops nobody sees.
/// </summary>
public sealed partial class SubRoot_Start_Gunner_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.Start());

    public static void Postfix(SubRoot __instance)
    {
        if (__instance.isCyclops && !__instance.GetComponent<VirtualCyclops>())
        {
            __instance.gameObject.EnsureComponent<GunnerConsole>();
        }
    }
}
