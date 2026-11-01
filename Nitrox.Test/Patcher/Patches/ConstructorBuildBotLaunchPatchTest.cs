using System.Runtime.CompilerServices;
using HarmonyLib;
using NitroxPatcher.Patches.Dynamic;
using UnityObject = UnityEngine.Object;

namespace Nitrox.Test.Patcher.Patches;

/// <summary>
///     Guards what a typo or a game update would break silently: the target is the build bot's launch setter and the field that the prefix asks Harmony to inject still exists with the same type
///     (a mismatch makes Harmony refuse the patch). It also pins the direction of the one decision that the prefix makes: a bot without a hover sound (null, or a destroyed object) is skipped,
///     a bot with one is not. That decision is a single Unity object comparison, and in the game's UnityEngine it is managed code over the object's native pointer field (<c>m_CachedPtr</c>),
///     so it runs here without the engine: the tests build an emitter without running any constructor and set that field themselves.
///     What the original setter does to a bot that has not started can only be seen live (<c>patchcheck ConstructorBuildBot set_launch</c> and no NullReferenceException at the first deployment
///     of a bay, see the design notes (not part of this repository)).
/// </summary>
[TestClass]
public class ConstructorBuildBotLaunchPatchTest
{
    private static readonly MethodInfo prefix = typeof(ConstructorBuildBot_launch_set_Patch).GetMethod("Prefix");

    [TestMethod]
    public void TargetIsTheLaunchSetterOfTheBuildBot()
    {
        MethodInfo target = ConstructorBuildBot_launch_set_Patch.TARGET_METHOD;

        target.Should().NotBeNull();
        target.Name.Should().Be("set_launch");
        target.DeclaringType.Should().Be(typeof(ConstructorBuildBot));
        target.GetParameters().Should().ContainSingle().Which.ParameterType.Should().Be(typeof(bool));
    }

    [TestMethod]
    public void PrefixReturnsABoolSoThatItCanSkipTheOriginal()
    {
        prefix.Should().NotBeNull();
        prefix.IsStatic.Should().BeTrue();
        prefix.ReturnType.Should().Be(typeof(bool));
    }

    [TestMethod]
    public void PrefixOnlyInjectsFieldsThatTheBuildBotHasWithTheSameType()
    {
        ParameterInfo[] parameters = prefix.GetParameters();

        parameters.Should().NotBeEmpty();
        foreach (ParameterInfo parameter in parameters)
        {
            parameter.Name.Should().StartWith("___", "Harmony injects a field by the name of the parameter");
            string fieldName = parameter.Name.Substring(3);
            FieldInfo field = AccessTools.Field(typeof(ConstructorBuildBot), fieldName);

            field.Should().NotBeNull($"ConstructorBuildBot has to have a field called {fieldName}");
            parameter.ParameterType.Should().Be(field.FieldType);
        }
    }

    [TestMethod]
    public void PrefixInjectsTheHoverSoundThatOnlyStartAssigns()
    {
        prefix.GetParameters().Select(parameter => parameter.Name).Should().Equal("___assignedHoverLoopSound");
    }

    [TestMethod]
    public void PrefixSkipsTheSetterWhenTheBotHasNoHoverSoundYet()
    {
        ConstructorBuildBot_launch_set_Patch.Prefix(null).Should().BeFalse("a bot that has not run Start() has no hover sound yet, so the setter would throw and has to be skipped");
    }

    [TestMethod]
    public void PrefixSkipsTheSetterWhenTheHoverSoundIsDestroyed()
    {
        // A non-null managed object whose native object is gone: Unity treats it as null, a plain reference check would not.
        FMOD_CustomLoopingEmitter destroyed = CreateEmitterWithNativePointer(IntPtr.Zero);

        ConstructorBuildBot_launch_set_Patch.Prefix(destroyed).Should().BeFalse("a destroyed hover sound counts as missing, the null check has to be Unity's and not a reference check");
    }

    [TestMethod]
    public void PrefixRunsTheSetterForABotThatHasStarted()
    {
        FMOD_CustomLoopingEmitter alive = CreateEmitterWithNativePointer(new IntPtr(1));

        ConstructorBuildBot_launch_set_Patch.Prefix(alive).Should().BeTrue("a bot with a live hover sound is not affected by the patch, its setter has to run");
    }

    /// <summary>
    ///     Creates the emitter without running a constructor (the engine is not there to back it) and sets the native pointer field that Unity's null comparison reads.
    /// </summary>
    private static FMOD_CustomLoopingEmitter CreateEmitterWithNativePointer(IntPtr nativePointer)
    {
        FieldInfo cachedPointer = AccessTools.Field(typeof(UnityObject), "m_CachedPtr");
        cachedPointer.Should().NotBeNull("Unity's Object keeps the pointer to its native object in m_CachedPtr and its null comparison tests it");
        cachedPointer.FieldType.Should().Be(typeof(IntPtr));

        FMOD_CustomLoopingEmitter emitter = (FMOD_CustomLoopingEmitter)RuntimeHelpers.GetUninitializedObject(typeof(FMOD_CustomLoopingEmitter));
        cachedPointer.SetValue(emitter, nativePointer);
        return emitter;
    }
}
