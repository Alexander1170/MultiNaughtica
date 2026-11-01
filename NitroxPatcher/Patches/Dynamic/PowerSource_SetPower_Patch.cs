using System.Reflection;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The other way a generator's charge changes, see <see cref="PowerSource_ModifyPower_Patch"/>.
/// </summary>
public sealed partial class PowerSource_SetPower_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PowerSource t) => t.SetPower(default));

    public static void Postfix(PowerSource __instance)
    {
        PowerSource_ModifyPower_Patch.Broadcast(__instance);
    }
}
