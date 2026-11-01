using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class GotoConsoleCommand_GotoPosition_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((GotoConsoleCommand t) => t.GotoPosition(default, default));

    public static bool Prefix(Vector3 position)
    {
        // A rider of a Seamoth's bar lets go first (the locked mode would pull the player back)
        RiderBars.ReleaseLocal(RiderReleaseReason.Teleported, true, false);
        Vehicle currentMountedVehicle = Player.main.currentMountedVehicle;
        if (currentMountedVehicle)
        {
            // Handle GoTo with a vehicle teleport, which takes player too
            currentMountedVehicle.TeleportVehicle(position, currentMountedVehicle.transform.rotation);
            return false;
        }

        // Normal GoTo behaviour if not in vehicle
        return true;
    }
}
