using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Bases;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A nuclear reactor uses up its rods only for the player who simulates the base (see <see cref="BaseBioReactor_ProducePower_Patch"/>). The rods it uses up are taken out of
/// its equipment by the vanilla method, which <see cref="Equipment_RemoveItem_Patch"/> already announces to the server; the depleted rod the reactor then adds already gets its
/// id and is announced by the existing item patches (Pickupable.Pickup, <see cref="Equipment_AddItem_Patch"/>). The other clients skip the whole method, so they neither use up
/// their own copy of the rods nor add a depleted rod of their own.
/// </summary>
public sealed partial class BaseNuclearReactor_ProducePower_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseNuclearReactor t) => t.ProducePower(default));

    public static bool Prefix(BaseNuclearReactor __instance, float requested, ref float __result, out int __state)
    {
        if (BaseSimulation.DisabledByHarness || BaseSimulation.RunsHere(__instance, Resolve<SimulationOwnership>()))
        {
            __state = __instance.CountActiveRods();
            return true;
        }

        // Not our base to burn: same answer as the vanilla method gives while a rod is active, nothing consumed
        __state = -1;
        __result = requested > 0f && __instance.CountActiveRods() > 0 ? requested : 0f;
        return false;
    }

    public static void Postfix(BaseNuclearReactor __instance, int __state)
    {
        // A rod was used up in this call: tell the others how far the next one is burned now, without waiting for the throttle
        if (!BaseSimulation.DisabledByHarness && __state >= 0 && __instance.CountActiveRods() < __state && __instance.TryGetComponent(out PowerSource powerSource))
        {
            PowerSource_ModifyPower_Patch.BroadcastNow(powerSource);
        }
    }
}
