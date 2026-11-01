using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The camera of a player who holds on to a Seamoth's rider bar looks around freely (<see cref="RiderBars.ApplyFreeLook" />): the game's locked mode of a vehicle turns the mouse look off, because a
/// pilot steers with it. Runs after the game's own camera update, which would reset it.
/// </summary>
public sealed partial class MainCameraControl_OnUpdate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MainCameraControl t) => t.OnUpdate());

    public static void Postfix(MainCameraControl __instance)
    {
        RiderBars.ApplyFreeLook(__instance);
    }
}
