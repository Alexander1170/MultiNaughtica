using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// B10b: the power a fabricator takes when a craft starts is only taken on the crafting player's client, and the charge of a base's generators is the one of the player who
/// simulates the base, which overwrites everybody else's value within seconds, so a craft of a player who does not simulate the base was free. The draw is reported to the
/// player who does (<see cref="BasePowerConsumed" />). Only this consumer is reported: the others (lights, chargers, filtration) run on every client and a report from each
/// would count them several times.
/// </summary>
public sealed partial class CrafterLogic_ConsumeEnergy_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => CrafterLogic.ConsumeEnergy(default, default));

    public static void Postfix(PowerRelay powerRelay, float amount, bool __result)
    {
        if (!__result || !(amount > 0f) || !GameModeUtils.RequiresPower() || powerRelay is not BasePowerRelay baseRelay || !baseRelay.baseComp ||
            !baseRelay.baseComp.TryGetNitroxId(out NitroxId baseId))
        {
            return;
        }

        if (!Resolve<SimulationOwnership>().HasAnyLockType(baseId))
        {
            Resolve<IPacketSender>().Send(new BasePowerConsumed(baseId, amount));
        }
    }
}
