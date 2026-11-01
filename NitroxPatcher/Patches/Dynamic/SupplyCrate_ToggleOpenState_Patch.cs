using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A supply crate that a player opens (or that a laser cutter opens when it was sealed) is open for the other players, and after a restart.
/// </summary>
public sealed partial class SupplyCrate_ToggleOpenState_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SupplyCrate t) => t.ToggleOpenState());

    public static void Postfix(SupplyCrate __instance)
    {
        if (__instance.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Entities>().BroadcastMetadataUpdate(id, new SupplyCrateMetadata(__instance.open));
        }
    }
}
