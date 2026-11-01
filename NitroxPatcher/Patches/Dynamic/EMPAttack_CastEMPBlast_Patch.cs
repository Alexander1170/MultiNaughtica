using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A Crabsquid's EMP blast only touches what is on the client that runs the attack (the creature's actions only run on the client that simulates it): a vehicle or Cyclops of the other
/// player was never hit. The simulating client announces the blast, the others make it at their copy of the creature (<see cref="NitroxClient.Communication.Packets.Processors.EmpFiredProcessor"/>, K18).
/// </summary>
public sealed partial class EMPAttack_CastEMPBlast_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((EMPAttack t) => t.CastEMPBlast());

    public static void Postfix(EMPAttack __instance)
    {
        Creature creature = __instance.GetComponentInParent<Creature>();
        if (creature && creature.TryGetNitroxId(out NitroxId creatureId) && Resolve<SimulationOwnership>().HasAnyLockType(creatureId))
        {
            Resolve<IPacketSender>().Send(new EmpFired(creatureId));
        }
    }
}
