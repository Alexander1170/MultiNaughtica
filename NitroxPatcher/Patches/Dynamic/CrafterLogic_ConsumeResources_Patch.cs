using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: the craft that a granted claim starts spends only the inventory's share of the ingredients (the lockers' items were removed by the
/// server before the answer), see <see cref="LockerCrafting.OnConsumeResources" />. Every other craft is vanilla.
/// </summary>
public sealed partial class CrafterLogic_ConsumeResources_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => CrafterLogic.ConsumeResources(default));

    public static bool Prefix(TechType techType, ref bool __result)
    {
        return LockerCrafting.OnConsumeResources(techType, ref __result);
    }
}
