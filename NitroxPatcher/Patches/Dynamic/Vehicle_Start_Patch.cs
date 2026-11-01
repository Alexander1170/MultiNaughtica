using System.Reflection;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Vehicles;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>Every Seamoth and Prawn Suit gets the component that asks a Cyclops with the power share module for energy (the vehicle's simulator is the one that asks), and every Seamoth the one that shows its rider bar.</summary>
public sealed partial class Vehicle_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.Start());

    public static void Postfix(Vehicle __instance)
    {
        if (__instance is SeaMoth || __instance is Exosuit)
        {
            __instance.gameObject.EnsureComponent<VehiclePowerShare>();
        }
        if (__instance is SeaMoth)
        {
            __instance.gameObject.EnsureComponent<SeamothRiderBar>();
        }
    }
}
