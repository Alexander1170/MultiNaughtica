using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.Helpers;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sends the charge of a generator of a base (see <see cref="NitroxClient.GameLogic.Spawning.Metadata.Extractor.PowerSourceMetadataExtractor"/>) whenever it changes,
/// but only from the player who simulates the base, and at most every few seconds per generator. The server stores it with the entity, so a player who joins
/// later, a remote build or a resync gets the stored charge instead of a full generator, and the other clients show the simulating player's charge.
/// Use and generation of power are still computed on every client, the sent value is the one that is kept.
/// </summary>
public sealed partial class PowerSource_ModifyPower_Patch : NitroxPatch, IDynamicPatch
{
    private const float THROTTLE_SECONDS = 5f;

    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PowerSource t) => t.ModifyPower(default, out Reflect.Ref<float>.Field));

    public static void Postfix(PowerSource __instance)
    {
        Broadcast(__instance);
    }

    /// <summary>
    /// A fuel item or a rod was used up: the burn progress dropped by a whole item and the item is gone on the server and on the other clients at this moment. The value that
    /// is waiting in the throttle is older (almost a full item) and would be applied by a player who takes over the base in the next seconds, which would burn the next item
    /// at once; so the older snapshot is dropped and the new one goes out immediately.
    /// </summary>
    internal static void BroadcastNow(PowerSource powerSource)
    {
        Base parentBase = powerSource.GetComponentInParent<Base>();
        if (!parentBase || !parentBase.TryGetNitroxId(out NitroxId baseId) || !Resolve<SimulationOwnership>().HasAnyLockType(baseId))
        {
            return;
        }

        NitroxEntity generator = powerSource.gameObject.FindAncestor<NitroxEntity>();
        if (generator && !generator.Id.Equals(baseId))
        {
            Resolve<ThrottledPacketSender>().RemovePendingPackets(generator.Id);
            Resolve<Entities>().EntityMetadataChanged(powerSource, generator.Id);
        }
    }

    internal static void Broadcast(PowerSource powerSource)
    {
        Base parentBase = powerSource.GetComponentInParent<Base>();
        if (!parentBase || !parentBase.TryGetNitroxId(out NitroxId baseId) || !Resolve<SimulationOwnership>().HasAnyLockType(baseId))
        {
            return;
        }

        NitroxEntity generator = powerSource.gameObject.FindAncestor<NitroxEntity>();
        if (generator && !generator.Id.Equals(baseId))
        {
            Resolve<Entities>().EntityMetadataChangedThrottled(powerSource, generator.Id, THROTTLE_SECONDS);
        }
    }
}
