using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// After the Stalker lets go of the item it falls (vanilla makes it a dynamic body again), so its position keeps changing for a while.
/// See <see cref="CollectShiny_TryPickupShinyTarget_Patch"/>.
/// </summary>
public sealed partial class CollectShiny_DropShinyTarget_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CollectShiny t) => t.DropShinyTarget(default(GameObject)));

    public static void Postfix(GameObject target)
    {
        if (!target || !target.TryGetNitroxId(out NitroxId id) || !Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return;
        }

        Log.Info($"[Stalker carry] {id} dropped at {target.transform.position.ToString("F2")}");
        target.EnsureComponent<ShinySettleWatcher>();
    }
}
