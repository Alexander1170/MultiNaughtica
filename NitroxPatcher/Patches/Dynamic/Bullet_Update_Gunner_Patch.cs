using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The sphere of a gunner station does not stop at the hull of its own Cyclops: <c>Bullet.Update</c>'s <c>Physics.SphereCast(...)</c> becomes <see cref="GunnerBullet.SphereCast" /> with the bullet as an extra
/// argument, which casts as the game does for every bullet that is not a gunner's. A patch of its own next to <see cref="Bullet_Update_Patch" />, which handles the time and the remote bullets, so that the
/// two can be read and tested apart.
/// </summary>
public sealed partial class Bullet_Update_Gunner_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly MethodInfo TARGET_METHOD = Reflect.Method((Bullet t) => t.Update());

    private static readonly MethodInfo SphereCast = typeof(Physics).GetMethod(nameof(Physics.SphereCast), [typeof(Vector3), typeof(float), typeof(Vector3), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int)]);

    private static readonly MethodInfo GunnerSphereCast = typeof(GunnerBullet).GetMethod(nameof(GunnerBullet.SphereCast));

    /*
     * REPLACE:
     * Physics.SphereCast(this.tr.position, this.shellRadius, this.tr.forward, out raycastHit, num, this.layerMask.value)
     * BY:
     * GunnerBullet.SphereCast(this.tr.position, this.shellRadius, this.tr.forward, out raycastHit, num, this.layerMask.value, this)
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Call, SphereCast))
                                            .ThrowIfInvalid("Bullet.Update has no Physics.SphereCast(Vector3, float, Vector3, out RaycastHit, float, int)")
                                            // the bullet is the last argument
                                            .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0))
                                            .SetOperandAndAdvance(GunnerSphereCast)
                                            .InstructionEnumeration();
    }
}
