using System;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sends the charge of a battery when it changes (see <see cref="BatteryChargeRules"/>). It is sent from a prefix (the setter has to be seen before it runs: this is
/// where the old value is known), but the value that is sent is the one that the setter is about to store (<c>Min(value, capacity)</c>, as in the game's setter).
/// </summary>
public sealed partial class Battery_charge_set_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Property((Battery t) => t.charge).SetMethod;

    public static void Prefix(Battery __instance, float value)
    {
        float newCharge = Math.Min(value, __instance._capacity);
        if (BatteryChargeRules.ShouldSend(__instance._charge, newCharge) && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Entities>().BroadcastMetadataUpdate(id, new BatteryMetadata(newCharge));
        }
    }
}
