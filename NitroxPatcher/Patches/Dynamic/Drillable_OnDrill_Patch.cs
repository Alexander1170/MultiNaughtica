using System;
using System.Collections.Generic;
using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class Drillable_OnDrill_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((Drillable t) => t.OnDrill(default, default, out Reflect.Ref<GameObject>.Field));

    /// <summary>
    /// Deposits whose destruction was announced from the last drill hit; <see cref="Drillable_DestroySelf_Patch" /> then does not announce it a second time.
    /// </summary>
    internal static readonly HashSet<NitroxId> AnnouncedGone = [];

    public static void Postfix(Drillable __instance)
    {
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            // The deposit is gone for the server already (announced when its last chunk broke): the drill arm may still hit the husk for up to 6 s, every hit used to send a metadata update
            // for an entity the server no longer knows (one error line each) and a second EntityDestroyed
            if (AnnouncedGone.Contains(id))
            {
                return;
            }

            // The object is only destroyed by DestroySelf, up to 6 s after its last chunk broke. If the driller leaves or the cell unloads in
            // between, the server would keep an invisible deposit with all chunks at 0 forever: tell it now, once.
            if (__instance.deleteWhenDrilled && Array.TrueForAll(__instance.health, chunkHealth => chunkHealth <= 0f))
            {
                AnnouncedGone.Add(id);
                Resolve<IPacketSender>().Send(new EntityDestroyed(id));
                return;
            }

            Resolve<Entities>().EntityMetadataChanged(__instance, id);
            return;
        }

        // For some reason the drillable ion cube deposit in the primary containment facility
        // is not automatically tagged as an entity. We spawn it here as a workaround.
        // TODO: spawn this entity server-side in a bootstrapper instead of client-side

        AnteChamber antechamber = __instance.GetComponentInParent<AnteChamber>();

        if (antechamber && antechamber.TryGetIdOrWarn(out NitroxId parentId))
        {
            id = NitroxEntity.GenerateNewId(__instance.gameObject);

            Optional<EntityMetadata> metadata = Resolve<EntityMetadataManager>().Extract(__instance.gameObject);
            Validate.IsPresent(metadata);

            PathBasedChildEntity entity = new(__instance.name, id, NitroxTechType.None, metadata.Value, parentId, []);
            Resolve<IPacketSender>().Send(new EntitySpawnedByClient(entity));
        }
    }
}
