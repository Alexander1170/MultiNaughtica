using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Vehicle_OnPilotModeEnd_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Vehicle t) => t.OnPilotModeEnd());

    public static void Prefix(Vehicle __instance)
    {
        Resolve<Vehicles>().BroadcastOnPilotModeChanged(__instance.gameObject, false);
        // Fixes instances of vehicles stuck on nothing by forcing the workaround (let another player enter and leave the vehicle)
        if (__instance.TryGetComponent(out MultiplayerVehicleControl mvc))
        {
            mvc.Exit();
        }

        // A vehicle that is destroyed with its pilot inside calls this from OnKill: the id is gone with it and nobody will answer a lock request for it (V0, VD5)
        bool isDying = __instance.liveMixin && !__instance.liveMixin.IsAlive();
        if (!isDying && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<SimulationOwnership>().RequestSimulationLock(id, SimulationLockType.TRANSIENT);
        }
    }

    /// <summary>A Seamoth whose pilot got out ends its afterburner burn (the cooldown goes on).</summary>
    public static void Postfix(Vehicle __instance)
    {
        VehicleAfterburners.End(__instance, AfterburnerState.EndReason.Exited);
    }
}
