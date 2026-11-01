using System;
using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Says in the log when the game's handling of a quick slot key press throws (a Unity exception goes to the player log, which nobody reads): the key of an active module in the fifth or sixth slot did nothing
/// in the play-test of coop.12 and nothing in the Nitrox log said why. The exception is passed on unchanged. Rare, so at most a few lines per session.
/// </summary>
public sealed partial class Vehicle_SlotKeyDown_Diagnostics_Patch : NitroxPatch, IDynamicPatch
{
    private const int MAX_LOGS = 5;

    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.SlotKeyDown(default));

    private static int logged;

    public static Exception Finalizer(Exception __exception, Vehicle __instance, int slotID)
    {
        if (__exception != null && logged < MAX_LOGS)
        {
            logged++;
            Log.Error(__exception, $"[VehicleUpgrades] the key press of quick slot {slotID} in {(__instance ? __instance.name : "a vehicle")} threw");
        }
        return __exception;
    }
}
