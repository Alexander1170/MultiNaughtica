using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A Mobile Vehicle Bay creates its build bots inside <c>Constructor.Deploy(true)</c> (<c>UpdateBuildBots</c>, also from <c>OnEnable</c>), and the rocket's build console does the same,
/// but a bot assigns its hover sound (<c>assignedHoverLoopSound</c>) only in its own <c>Start()</c>, which runs later. <c>Constructor.Update</c> and <c>RocketConstructor.Update</c> set
/// <c>launch</c> on every bot each frame, so a bot that was created earlier in the same frame, before its <c>Start()</c>, made the setter throw a <c>NullReferenceException</c>
/// (it plays or stops that sound). On the player who receives a deployment the bay is deployed from the packet processing (<c>ConstructorMetadataProcessor</c>, run from
/// <c>Multiplayer.Update</c>, which usually runs before <c>Constructor.Update</c>), so every bay deployment logged one NullReferenceException in <c>ConstructorBuildBot.set_launch</c> and aborted
/// that frame's <c>Constructor.Update</c>. Skipping the call for a bot that has not started loses nothing: <c>Start()</c> ends with <c>launch = false</c>, which is what the skipped call would set
/// (the bay was just deployed, the bot is at its rest position and there is no build target), and the next frame sets the real value. A bot that has started is not affected.
/// </summary>
public sealed partial class ConstructorBuildBot_launch_set_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Property((ConstructorBuildBot t) => t.launch).SetMethod;

    /// <param name="___assignedHoverLoopSound">The private hover sound that only <c>ConstructorBuildBot.Start()</c> assigns; null (or destroyed) until then.</param>
    public static bool Prefix(FMOD_CustomLoopingEmitter ___assignedHoverLoopSound)
    {
        return ___assignedHoverLoopSound != null;
    }
}
