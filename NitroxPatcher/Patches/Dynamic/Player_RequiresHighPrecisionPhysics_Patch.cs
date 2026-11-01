using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Subnautica enables high precision physics any time the local player is in a moving cyclops and is not piloting it.
/// We force a higher fixed timestep as long as the local player is inside of a Cyclops to avoid it stuttering: the walking of the local player there runs in FixedUpdate
/// (<c>CyclopsMotor</c>), at the default 10 Hz it would visibly stutter. While the player pilots the Cyclops (<see cref="Player.Mode.Piloting" />) the motor does not walk
/// them, so the vanilla rule (no high precision while piloting) applies: the whole world's physics runs at 10 Hz again instead of 50 Hz for as long as someone drives (PL10).
/// </summary>
public sealed partial class Player_RequiresHighPrecisionPhysics_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.RequiresHighPrecisionPhysics());

    public static bool Prefix(Player __instance, ref bool __result)
    {
        if (__instance.currentSub && __instance.currentSub.isCyclops && __instance.mode != Player.Mode.Piloting)
        {
            __result = true;
            return false;
        }

        return true;
    }
}
