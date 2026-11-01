#if DEBUG
using System.Reflection;
using HarmonyLib;
using NitroxClient.MonoBehaviours;
using NitroxPatcher.Patches.Dynamic;
using UnityEngine;

namespace Nitrox.Test.Patcher.Patches;

/// <summary>
/// The Harmony patches of the Debug force probe (bug 11) against the real Unity assembly of the game: every target exists, is a managed wrapper (not an extern method, which Harmony
/// would patch differently), its prefix has the parameters that Harmony binds by name, and a target that fails does not stop the others. The patches themselves are not applied here
/// (the wrappers call icalls that exist only inside Unity): the live check "hullforce selftest" says which of them are live in the game.
/// </summary>
[TestClass]
public class HullForceProbePatchesTest
{
    [TestMethod]
    public void EveryTarget_ExistsAndIsAManagedWrapper()
    {
        foreach (HullForceProbe_Patches.PatchTarget target in HullForceProbe_Patches.Targets())
        {
            MethodBase method = target.Resolve();

            method.DeclaringType.Should().Be(typeof(Rigidbody), target.Api);
            method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.InternalCall).Should().BeFalse($"{target.Api} must not be an icall");
            method.GetMethodBody().Should().NotBeNull($"{target.Api} must have an IL body that Harmony can wrap");
        }
    }

    [TestMethod]
    public void EveryTarget_HasAPrefixWhoseParametersHarmonyCanBind()
    {
        foreach (HullForceProbe_Patches.PatchTarget target in HullForceProbe_Patches.Targets())
        {
            MethodBase method = target.Resolve();
            MethodInfo prefix = typeof(HullForceProbe_Patches).GetMethod(target.PrefixName, BindingFlags.Static | BindingFlags.Public);

            prefix.Should().NotBeNull(target.Api);
            prefix.ReturnType.Should().Be(typeof(void), target.Api);
            foreach (ParameterInfo parameter in prefix.GetParameters())
            {
                if (parameter.Name == "__instance")
                {
                    parameter.ParameterType.Should().Be(typeof(Rigidbody), $"{target.Api} __instance");
                    continue;
                }

                ParameterInfo original = method.GetParameters().FirstOrDefault(p => p.Name == parameter.Name);
                original.Should().NotBeNull($"{target.Api}: the original has no parameter named {parameter.Name}");
                parameter.ParameterType.Should().Be(original.ParameterType, $"{target.Api}: {parameter.Name}");
            }
        }
    }

    [TestMethod]
    public void TheTargets_AreTheWrappersThatTheOtherOverloadsCall()
    {
        // The overloads without a ForceMode and the ones with x, y, z end in these (read with ildump from UnityEngine.PhysicsModule); the extern *_Injected methods are not patched
        List<string> apis = HullForceProbe_Patches.Targets().Select(t => t.Api).ToList();

        apis.Should().Contain(["AddForce", "AddRelativeForce", "AddTorque", "AddRelativeTorque", "AddForceAtPosition", "MovePosition", "MoveRotation", "set_velocity", "set_angularVelocity", "set_position", "set_rotation"]);
        apis.Should().NotContain(api => api.EndsWith("_Injected"));
        apis.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void ATargetThatFails_DoesNotStopTheOthers()
    {
        List<string> applied = [];
        List<HullForceProbe_Patches.PatchTarget> targets =
        [
            new("TestFirstFailsToResolve", () => throw new MissingMethodException("Rigidbody.nothing"), nameof(HullForceProbe_Patches.PrefixAddForce)),
            new("TestSecondApplies", () => typeof(Rigidbody).GetMethod("MovePosition"), nameof(HullForceProbe_Patches.PrefixMovePosition)),
            new("TestThirdFailsToApply", () => typeof(Rigidbody).GetMethod("MoveRotation"), nameof(HullForceProbe_Patches.PrefixMoveRotation)),
            new("TestFourthHasNoPrefix", () => typeof(Rigidbody).GetMethod("MovePosition"), "NoSuchPrefix"),
            new("TestFifthApplies", () => typeof(Rigidbody).GetMethod("AddForce", [typeof(Vector3), typeof(ForceMode)]), nameof(HullForceProbe_Patches.PrefixAddForce))
        ];

        HullForceProbe_Patches.ApplyTargets(targets, (method, prefix) =>
        {
            if (method.Name == "MoveRotation")
            {
                throw new InvalidOperationException("Harmony said no");
            }

            applied.Add($"{method.Name}->{prefix.Name}");
        });

        applied.Should().Equal("MovePosition->PrefixMovePosition", "AddForce->PrefixAddForce");
        HullForceProbe.PatchStatusOf("TestFirstFailsToResolve").Should().Be("failed:MissingMethodException");
        HullForceProbe.PatchStatusOf("TestSecondApplies").Should().Be("ok");
        HullForceProbe.PatchStatusOf("TestThirdFailsToApply").Should().Be("failed:InvalidOperationException");
        HullForceProbe.PatchStatusOf("TestFourthHasNoPrefix").Should().Be("failed:MissingMethodException");
        HullForceProbe.PatchStatusOf("TestFifthApplies").Should().Be("ok");
    }

    [TestMethod]
    public void WhileTheProbeIsNotArmed_EveryPrefixDoesNothing()
    {
        HullForceProbe.Armed.Should().BeFalse();

        // No Rigidbody exists outside Unity: a prefix that touched it (or the probe) would throw
        HullForceProbe_Patches.PrefixAddForce(null, default, default);
        HullForceProbe_Patches.PrefixAddRelativeForce(null, default, default);
        HullForceProbe_Patches.PrefixAddTorque(null, default, default);
        HullForceProbe_Patches.PrefixAddRelativeTorque(null, default, default);
        HullForceProbe_Patches.PrefixAddForceAtPosition(null, default, default, default);
        HullForceProbe_Patches.PrefixAddExplosionForce(null, default, default, default, default, default);
        HullForceProbe_Patches.PrefixMovePosition(null, default);
        HullForceProbe_Patches.PrefixMoveRotation(null, default);
        HullForceProbe_Patches.PrefixSetVelocity(null, default);
        HullForceProbe_Patches.PrefixSetAngularVelocity(null, default);
        HullForceProbe_Patches.PrefixSetPosition(null, default);
        HullForceProbe_Patches.PrefixSetRotation(null, default);
        HullForceProbe_Patches.PrefixSetCenterOfMass(null, default);
    }
}
#endif
