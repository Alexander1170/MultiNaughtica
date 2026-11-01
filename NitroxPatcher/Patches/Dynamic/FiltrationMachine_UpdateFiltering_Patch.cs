using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Water filtration machines run only for the player who simulates the base. Without this every client ran its own copy
/// (consuming power, counting down the timers and spawning FilteredWater and salt), and each copy was registered on the server
/// as a new item, so the water and salt were multiplied by the number of players. The items the simulating player produces reach
/// the others through the normal container sync, like any item put in the machine by hand.
/// </summary>
public sealed partial class FiltrationMachine_UpdateFiltering_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((FiltrationMachine t) => t.UpdateFiltering());

    public static bool Prefix(FiltrationMachine __instance)
    {
        Base parentBase = __instance.GetComponentInParent<Base>();
        if (!parentBase || !parentBase.TryGetNitroxId(out NitroxId baseId))
        {
            // Not part of a synced base: nothing to arbitrate, keep the vanilla behaviour
            return true;
        }

        return Resolve<SimulationOwnership>().HasAnyLockType(baseId);
    }
}
