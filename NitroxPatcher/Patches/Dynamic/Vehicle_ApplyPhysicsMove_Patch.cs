using System;
using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Seamoth's engine turns the player's input into forces here, and only on the client of the player who pilots it. While the afterburner burns the four forces are scaled by the server's
/// factor for this one call and put back afterwards (also when the game's method throws), so nothing else that reads them sees a changed value. The other players see the faster Seamoth
/// through the movement the game already syncs.
/// </summary>
public sealed partial class Vehicle_ApplyPhysicsMove_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.ApplyPhysicsMove());

    public static void Prefix(Vehicle __instance, out VehicleAfterburners.ForceSnapshot __state)
    {
        PatchProbe.Fired("Vehicle.ApplyPhysicsMove");
        VehicleAfterburners.ScaleForces(__instance, out __state);
    }

    public static Exception Finalizer(Vehicle __instance, VehicleAfterburners.ForceSnapshot __state, Exception __exception)
    {
        VehicleAfterburners.RestoreForces(__instance, in __state);
        return __exception;
    }
}
