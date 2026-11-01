using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours.Vehicles;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Temporarily disables vehicle's position sync while they're grabbed by reapers, and tells the other clients that the Reaper grabbed it (K16): the copy of the Reaper on each
/// client touches the vehicle on its own, so before this only the client where it happened held the vehicle while the others saw the Reaper swim off and the pilot drive free.
/// The clients that are told grab it too (<see cref="NitroxClient.Communication.Packets.Processors.ReaperGrabVehicleProcessor"/>), with the packet suppressed.
/// </summary>
public sealed partial class ReaperLeviathan_GrabVehicle_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((ReaperLeviathan t) => t.GrabVehicle(default, default));

    public static void Prefix(Vehicle vehicle)
    {
        if (vehicle.TryGetComponent(out VehicleMovementReplicator vehicleMovementReplicator))
        {
            vehicleMovementReplicator.enabled = false;
        }
    }

    public static void Postfix(ReaperLeviathan __instance, Vehicle vehicle)
    {
        if (__instance.TryGetNitroxId(out NitroxId reaperId) && vehicle.TryGetNitroxId(out NitroxId vehicleId))
        {
            Resolve<IPacketSender>().Send(new ReaperGrabVehicle(reaperId, vehicleId));
        }
    }
}
