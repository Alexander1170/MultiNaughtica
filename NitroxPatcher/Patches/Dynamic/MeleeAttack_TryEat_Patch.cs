using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// MeleeAttack.TryEat kills the prey and destroys it in the same call, which stops the coroutine that would have broadcast its death
/// (see <see cref="CreatureDeath_OnKillAsync_Patch"/>). Without this, a fish eaten by a creature we simulate only disappears for us and comes back on reload.
/// </summary>
public sealed partial class MeleeAttack_TryEat_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((MeleeAttack t) => t.TryEat(default, default));

    public static void Prefix(MeleeAttack __instance, GameObject preyGameObject, out NitroxId __state)
    {
        __state = null;
        if (!preyGameObject || !preyGameObject.TryGetNitroxId(out NitroxId preyId))
        {
            return;
        }

        Creature predator = __instance.GetComponentInParent<Creature>();
        if (predator && predator.TryGetNitroxId(out NitroxId predatorId) && Resolve<SimulationOwnership>().HasAnyLockType(predatorId))
        {
            __state = preyId;
        }
    }

    public static void Postfix(bool __result, NitroxId __state)
    {
        if (__result && __state != null)
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(__state));
        }
    }
}
