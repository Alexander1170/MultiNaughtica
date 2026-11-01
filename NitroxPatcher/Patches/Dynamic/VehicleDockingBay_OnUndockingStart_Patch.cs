using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The undocking of the local player starts here, after the game accepted the click (<c>CinematicModeTriggerBase.StartCinematicMode</c> checks that no other cinematic runs and that the
/// player's tool can be put away). It used to be announced when the server granted the lock, which happens before the game decides: a refused click left the other players with a bay
/// that opens for a vehicle that never leaves (V0, PO2). The other players replay the start through <c>VehicleUndockingProcessor</c> and never call this method.
/// </summary>
public sealed partial class VehicleDockingBay_OnUndockingStart_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((VehicleDockingBay t) => t.OnUndockingStart());

    public static void Prefix(VehicleDockingBay __instance)
    {
        Vehicle vehicle = __instance.GetDockedVehicle();
        if (!vehicle || !__instance.TryGetIdOrWarn(out NitroxId dockId) || !vehicle.TryGetIdOrWarn(out NitroxId vehicleId))
        {
            return;
        }

        Vehicles.EngagePlayerMovementSuppressor(vehicle);
        Resolve<IPacketSender>().Send(new VehicleUndocking(vehicleId, dockId, Resolve<IMultiplayerSession>().Reservation.SessionId, true));
    }
}
