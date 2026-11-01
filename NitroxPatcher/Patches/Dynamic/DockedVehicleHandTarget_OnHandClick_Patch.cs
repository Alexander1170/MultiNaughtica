using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.MonoBehaviours.Gui.HUD;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class DockedVehicleHandTarget_OnHandClick_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo targetMethod = Reflect.Method((DockedVehicleHandTarget t) => t.OnHandClick(default(GUIHand)));

    private static bool skipPrefix;

    public static bool Prefix(DockedVehicleHandTarget __instance, GUIHand hand)
    {
        Vehicle vehicle = __instance.dockingBay.GetDockedVehicle();

        if (skipPrefix || !vehicle.TryGetIdOrWarn(out NitroxId vehicleId))
        {
            return true;
        }

        // The game refuses the click itself when the bay is blocked (the hover text says "insufficient undocking clearance"), and it does so without starting anything. Nothing is asked of
        // the server then: the lock that was requested anyway stayed with this player for a vehicle that nobody drives, and the other players were told that it was undocking (V0, PO2)
        if (!__instance.dockingBay.HasUndockingClearance() || !__instance.isValidHandTarget)
        {
            return true;
        }

        if (Resolve<SimulationOwnership>().HasExclusiveLock(vehicleId))
        {
            Log.Debug($"Already have an exclusive lock on this vehicle: {vehicleId}");
            return true;
        }

        HandInteraction<DockedVehicleHandTarget> context = new(__instance, hand);
        LockRequest<HandInteraction<DockedVehicleHandTarget>> lockRequest = new(vehicleId, SimulationLockType.EXCLUSIVE, ReceivedSimulationLockResponse, context);
        Resolve<SimulationOwnership>().RequestSimulationLock(lockRequest);

        return false;
    }

    private static void ReceivedSimulationLockResponse(NitroxId vehicleId, bool lockAcquired, HandInteraction<DockedVehicleHandTarget> context)
    {
        if (lockAcquired)
        {
            VehicleDockingBay dockingBay = context.Target.dockingBay;
            Vehicle vehicle = dockingBay.GetDockedVehicle();

            if (!dockingBay.TryGetIdOrWarn(out NitroxId dockId))
            {
                return;
            }

            // The undocking is announced when the game really starts it (VehicleDockingBay_OnUndockingStart_Patch), not when the lock was granted: the click can still be refused
            skipPrefix = true;
            context.Target.OnHandClick(context.GuiHand);
            skipPrefix = false;
        }
        else
        {
            //TODO: Check if this should be Hand
            HandReticle.main.SetText(HandReticle.TextType.Hand, "Another player is using this vehicle!", false, GameInput.Button.None);
            HandReticle.main.SetText(HandReticle.TextType.HandSubscript, string.Empty, false, GameInput.Button.None);
            HandReticle.main.SetIcon(HandReticle.IconType.HandDeny, 1f);
            context.Target.isValidHandTarget = false;
            context.Target.gameObject.AddComponent<DenyOwnershipHand>();
        }
    }
}
