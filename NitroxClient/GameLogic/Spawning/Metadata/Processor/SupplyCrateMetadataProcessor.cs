using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class SupplyCrateMetadataProcessor : EntityMetadataProcessor<SupplyCrateMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, SupplyCrateMetadata metadata)
    {
        SupplyCrate crate = gameObject.GetComponent<SupplyCrate>();
        if (crate)
        {
            SetOpen(crate, metadata.Open);
        }
    }

    /// <summary>
    /// Not ToggleOpenState: that plays the opening sound again, flips whatever the state is now and is the call that reports the change
    /// </summary>
    public static void SetOpen(SupplyCrate crate, bool open)
    {
        if (crate.open == open)
        {
            return;
        }

        crate.open = open;
        Animation animation = crate.GetComponentInChildren<Animation>();
        if (animation)
        {
            animation.Play(open ? crate.openClipName : crate.closeClipName);
        }

        if (open && crate.setActiveOnOpen)
        {
            crate.setActiveOnOpen.SetActive(true);
        }
    }
}
