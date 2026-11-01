using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The lights of a vehicle use power in every frame. The copy of a vehicle on the other client (the one that does not hold its lock) has its lights on too (the metadata applies them)
/// and drained the same battery on its own, which cancelled the drain of the client that holds the vehicle. Lights of a vehicle that this client does not hold are free here; the holder pays
/// (V0, battery charge). Lights of tools and everything that is not part of a vehicle keep the vanilla behaviour.
/// </summary>
public sealed partial class ToggleLights_UpdateLightEnergy_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((ToggleLights t) => t.UpdateLightEnergy());

    public static bool Prefix(ToggleLights __instance)
    {
        Vehicle vehicle = __instance.GetComponentInParent<Vehicle>();
        return !vehicle || !vehicle.TryGetNitroxId(out NitroxId id) || Resolve<SimulationOwnership>().HasAnyLockType(id);
    }
}
