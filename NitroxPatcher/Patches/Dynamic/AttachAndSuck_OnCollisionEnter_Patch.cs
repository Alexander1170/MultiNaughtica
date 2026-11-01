using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// K18, Bleeder: the contact that starts a Bleeder's attack is a physics callback that runs on the copy of the Bleeder of every client. Vanilla decides whether the contact is
/// valid (target free, 4 s since the last attack, player not in a sub or a walkable interior, attackable); when it accepted a new target, the local player asks for the exclusive
/// simulation lock of the Bleeder so that the attack, which only runs on the client that simulates the creature, runs here (<see cref="BleederClaims" />).
/// </summary>
public sealed partial class AttachAndSuck_OnCollisionEnter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.OnCollisionEnter(default));

    public static void Prefix(AttachAndSuck __instance, out BleederAttachTarget __state)
    {
        __state = __instance.currentTarget;
    }

    public static void Postfix(AttachAndSuck __instance, BleederAttachTarget __state)
    {
        // Vanilla refused the contact (or it was the target we already have)
        if (!__instance.currentTarget || __instance.currentTarget == __state)
        {
            return;
        }

        // A Bleeder the server does not know (a local-only spawn) keeps the vanilla behaviour: nobody else could simulate it
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            BleederClaims.Claim(__instance, id);
        }
    }
}
