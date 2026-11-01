#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Harmony prefixes of the Debug force probe of bug 11 (<see cref="HullForceProbe" />, a harness script that is not part of this repository, section "The force probe"). Debug builds only, the Release
/// build does not contain this class. Every prefix reads the static bool <see cref="HullForceProbe.Armed" /> first and does nothing else while the probe is not armed.
/// <para>
/// What is patched and why that works (read with ildump from the game's UnityEngine.PhysicsModule, Unity 2019.4): the Rigidbody methods that take a Vector3 are managed wrappers (4 to 10
/// IL instructions) that call an <c>extern</c> method named <c>..._Injected</c>. The patches go on the wrappers, never on an extern one: <c>AddForce(Vector3, ForceMode)</c>,
/// <c>AddRelativeForce</c>, <c>AddTorque</c>, <c>AddRelativeTorque</c>, <c>AddForceAtPosition(Vector3, Vector3, ForceMode)</c>, <c>AddExplosionForce(..., ForceMode)</c>,
/// <c>MovePosition</c>, <c>MoveRotation</c> and the setters of <c>velocity</c>, <c>angularVelocity</c>, <c>position</c>, <c>rotation</c> and <c>centerOfMass</c>. The other overloads
/// (<c>AddForce(Vector3)</c>, <c>AddForce(x, y, z)</c>, <c>AddForceAtPosition(Vector3, Vector3)</c> and so on) call these in their IL, so they are seen too. Not patched, because they
/// are <c>extern</c> without a managed wrapper: <c>ResetCenterOfMass</c> and the setters of the solver iteration counts, the sleep thresholds and the max velocities (none of them
/// moves the hull). Not seen: a write to a Transform of the hull (a Transform is not a Rigidbody, a patch on every transform write of the game is too hot): the per-step position check
/// of the probe and the counters in the Nitrox replicators cover it. A caller that was compiled before the patches were applied may have a wrapper inlined and is not seen then: the
/// line <c>coverage</c> of a run says so, the command <c>hullforce selftest</c> says which patches are live.
/// </para>
/// <para>
/// A patch that cannot be applied is logged and skipped: every target has its own try block, and the targets are looked up when the patches are applied, not in a static initializer
/// (a failing static initializer would stop the resolving of all the patches).
/// </para>
/// </summary>
public sealed class HullForceProbe_Patches : NitroxPatch, IDynamicPatch
{
    /// <summary>
    /// One patch of the probe: the name of the API for the log, how to find the method (looked up when the patches are applied, never in a static initializer) and the name of the prefix.
    /// </summary>
    public readonly record struct PatchTarget(string Api, Func<MethodBase> Resolve, string PrefixName);

    private static MethodBase Method(string name, params Type[] arguments)
    {
        return typeof(Rigidbody).GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, arguments, null) ?? throw new MissingMethodException($"Rigidbody.{name}");
    }

    private static MethodBase Setter(string property)
    {
        return typeof(Rigidbody).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetSetMethod() ?? throw new MissingMethodException($"Rigidbody.set_{property}");
    }

    public static List<PatchTarget> Targets()
    {
        return
        [
            new("AddForce", () => Method("AddForce", typeof(Vector3), typeof(ForceMode)), nameof(PrefixAddForce)),
            new("AddRelativeForce", () => Method("AddRelativeForce", typeof(Vector3), typeof(ForceMode)), nameof(PrefixAddRelativeForce)),
            new("AddTorque", () => Method("AddTorque", typeof(Vector3), typeof(ForceMode)), nameof(PrefixAddTorque)),
            new("AddRelativeTorque", () => Method("AddRelativeTorque", typeof(Vector3), typeof(ForceMode)), nameof(PrefixAddRelativeTorque)),
            new("AddForceAtPosition", () => Method("AddForceAtPosition", typeof(Vector3), typeof(Vector3), typeof(ForceMode)), nameof(PrefixAddForceAtPosition)),
            new("AddExplosionForce", () => Method("AddExplosionForce", typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(ForceMode)), nameof(PrefixAddExplosionForce)),
            new("MovePosition", () => Method("MovePosition", typeof(Vector3)), nameof(PrefixMovePosition)),
            new("MoveRotation", () => Method("MoveRotation", typeof(Quaternion)), nameof(PrefixMoveRotation)),
            new("set_velocity", () => Setter("velocity"), nameof(PrefixSetVelocity)),
            new("set_angularVelocity", () => Setter("angularVelocity"), nameof(PrefixSetAngularVelocity)),
            new("set_position", () => Setter("position"), nameof(PrefixSetPosition)),
            new("set_rotation", () => Setter("rotation"), nameof(PrefixSetRotation)),
            new("set_centerOfMass", () => Setter("centerOfMass"), nameof(PrefixSetCenterOfMass))
        ];
    }

    public override void Patch(Harmony harmony)
    {
        ApplyTargets(Targets(), (method, prefix) => PatchPrefix(harmony, method, prefix));
    }

    /// <summary>
    /// Applies every target on its own: one that cannot be found or patched is logged and noted for the probe (the line "patches" of <c>hullforce: begin</c>), the others are applied all the
    /// same. Nothing is thrown.
    /// </summary>
    public static void ApplyTargets(IEnumerable<PatchTarget> targets, Action<MethodBase, MethodInfo> apply)
    {
        foreach (PatchTarget target in targets)
        {
            try
            {
                MethodBase method = target.Resolve();
                MethodInfo prefix = typeof(HullForceProbe_Patches).GetMethod(target.PrefixName, BindingFlags.Static | BindingFlags.Public) ?? throw new MissingMethodException(target.PrefixName);
                apply(method, prefix);
                HullForceProbe.NotePatch(target.Api, true, string.Empty);
            }
            catch (Exception exception)
            {
                HullForceProbe.NotePatch(target.Api, false, exception.GetType().Name);
                Log.Warn($"[HullForceProbe] could not patch {target.Api}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    public static void PrefixAddForce(Rigidbody __instance, Vector3 force, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddForce, (int)mode, force);
        }
    }

    public static void PrefixAddRelativeForce(Rigidbody __instance, Vector3 force, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddRelativeForce, (int)mode, force);
        }
    }

    public static void PrefixAddTorque(Rigidbody __instance, Vector3 torque, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddTorque, (int)mode, torque);
        }
    }

    public static void PrefixAddRelativeTorque(Rigidbody __instance, Vector3 torque, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddRelativeTorque, (int)mode, torque);
        }
    }

    public static void PrefixAddForceAtPosition(Rigidbody __instance, Vector3 force, Vector3 position, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddForceAtPosition, (int)mode, force);
        }
    }

    public static void PrefixAddExplosionForce(Rigidbody __instance, float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteForce(__instance, HullForceRules.ForceKind.AddExplosionForce, (int)mode, new Vector3(explosionForce, 0f, 0f));
        }
    }

    public static void PrefixMovePosition(Rigidbody __instance, Vector3 position)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWrite(__instance, HullForceRules.ForceKind.MovePosition, position);
        }
    }

    public static void PrefixMoveRotation(Rigidbody __instance, Quaternion rot)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWriteRotation(__instance, HullForceRules.ForceKind.MoveRotation, rot);
        }
    }

    public static void PrefixSetVelocity(Rigidbody __instance, Vector3 value)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWrite(__instance, HullForceRules.ForceKind.SetVelocity, value);
        }
    }

    public static void PrefixSetAngularVelocity(Rigidbody __instance, Vector3 value)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWrite(__instance, HullForceRules.ForceKind.SetAngularVelocity, value);
        }
    }

    public static void PrefixSetPosition(Rigidbody __instance, Vector3 value)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWrite(__instance, HullForceRules.ForceKind.SetPosition, value);
        }
    }

    public static void PrefixSetRotation(Rigidbody __instance, Quaternion value)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWriteRotation(__instance, HullForceRules.ForceKind.SetRotation, value);
        }
    }

    public static void PrefixSetCenterOfMass(Rigidbody __instance, Vector3 value)
    {
        if (HullForceProbe.Armed)
        {
            HullForceProbe.NoteWrite(__instance, HullForceRules.ForceKind.SetCenterOfMass, value);
        }
    }
}
#endif
