using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours.Vehicles;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Enables vehicle's position sync when they're released from reapers, and tells the other clients that the Reaper let go (K16, see <see cref="ReaperLeviathan_GrabVehicle_Patch"/>).
/// </summary>
public sealed partial class ReaperLeviathan_ReleaseVehicle_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((ReaperLeviathan t) => t.ReleaseVehicle());

    public static void Prefix(ReaperLeviathan __instance)
    {
        if (__instance.holdingVehicle && __instance.holdingVehicle.TryGetComponent(out VehicleMovementReplicator vehicleMovementReplicator))
        {
            vehicleMovementReplicator.enabled = true;
        }

        // Only when something is held: vanilla also calls this every frame while the type is set and the vehicle is gone (destroyed), and the others learn of that from the destroy.
        // And only while the Reaper is active: vanilla's OnDisable releases the vehicle too, which is what a copy that is destroyed or unloaded on this client does, not a release
        // that the other clients should follow.
        if (__instance.isActiveAndEnabled && __instance.holdingVehicle && __instance.TryGetNitroxId(out NitroxId reaperId))
        {
            Resolve<IPacketSender>().Send(new ReaperReleaseVehicle(reaperId));
        }

        // A timer left from an earlier grab (the fallback of a grab that another client announced) must not let go of the next one
        __instance.CancelInvoke(nameof(ReaperLeviathan.ReleaseVehicle));
    }
}
