using System;
using System.Reflection;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The two scales of the Cyclops' engine (the game sets both to 1 and changes them nowhere) are multiplied for the one call of the game's physics step and put back afterwards, also when the game's method throws:
/// a Cyclops that tows a vehicle is slower (the server's <c>TowSpeedFactor</c>, both scales), and a Cyclops whose engineer sends the power to the engines is faster (<see cref="EngineerState.SpeedFactor" />, the
/// acceleration only: the climb and the dive are accelerated too, the turn is not). One patch for both, because two patches that save and restore the same field would depend on the order of their finalizers.
/// The force is only applied by the client that simulates the Cyclops, so that is where it counts.
/// </summary>
public sealed partial class SubControl_FixedUpdate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubControl t) => t.FixedUpdate());

    public readonly struct Scales
    {
        public Scales(bool scaled, float acceleration, float turning)
        {
            Scaled = scaled;
            Acceleration = acceleration;
            Turning = turning;
        }

        public bool Scaled { get; }
        public float Acceleration { get; }
        public float Turning { get; }
    }

    public static void Prefix(SubControl __instance, out Scales __state)
    {
        __state = default;
        if (!__instance.sub)
        {
            return;
        }
        float tow = Math.Min(1f, VehicleTows.SpeedFactor(__instance.sub));
        bool flank = __instance.cyclopsMotorMode && __instance.cyclopsMotorMode.cyclopsMotorMode == CyclopsMotorMode.CyclopsMotorModes.Flank;
        float engineer = EngineerState.SpeedFactor(__instance.sub, flank);
#if DEBUG
        EngineerState.DebugAppliedAcceleration = __instance.accelScale * tow * engineer;
#endif
        if (tow >= 1f && engineer == 1f)
        {
            return;
        }
        PatchProbe.Fired("SubControl.FixedUpdate");
        __state = new Scales(true, __instance.accelScale, __instance.turnScale);
        __instance.accelScale *= tow * engineer;
        __instance.turnScale *= tow;
    }

    public static Exception Finalizer(SubControl __instance, Scales __state, Exception __exception)
    {
        if (__state.Scaled)
        {
            __instance.accelScale = __state.Acceleration;
            __instance.turnScale = __state.Turning;
        }
        return __exception;
    }
}
