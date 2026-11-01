using System.Reflection;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Every Cyclops gets the components that drive the Cyclops Solar Charger and the Cyclops Power Share (the game's own update of a Cyclops that is far from the camera does not run), and its lighting panel
/// the buttons of the Power Share switch and the tow hook, and the display at its helm the terrain hologram of the Cyclops Terrain Sonar (<see cref="CyclopsTerrainSonar" />, with the navigator console of
/// the later milestones). The copy of a Cyclops that Nitrox walks the player around in (<see cref="VirtualCyclops" />) gets none of the buttons and the hologram (it would build them on a Cyclops nobody sees).
/// </summary>
public sealed partial class SubRoot_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.Start());

    public static void Postfix(SubRoot __instance)
    {
        if (__instance.isBase)
        {
            NavigatorContacts.RegisterBase(__instance);
        }
        if (__instance.isCyclops)
        {
            __instance.gameObject.EnsureComponent<CyclopsSolarCharger>();
            __instance.gameObject.EnsureComponent<CyclopsPowerShare>();
            if (!__instance.GetComponent<VirtualCyclops>())
            {
                __instance.gameObject.EnsureComponent<CyclopsTerrainSonar>();
                __instance.gameObject.EnsureComponent<NavigatorConsole>();
                __instance.gameObject.EnsureComponent<HelmMarkLayer>();
                __instance.gameObject.EnsureComponent<CyclopsHelmMap>();
                __instance.gameObject.EnsureComponent<CyclopsHelmHud>();
                __instance.gameObject.EnsureComponent<RouteGuide>();
                __instance.gameObject.EnsureComponent<CyclopsClearance>();
                CyclopsLightingPanel lighting = __instance.GetComponentInChildren<CyclopsLightingPanel>(true);
                if (lighting)
                {
                    lighting.gameObject.EnsureComponent<CyclopsPanelButtons>();
                }
            }
        }
    }
}
