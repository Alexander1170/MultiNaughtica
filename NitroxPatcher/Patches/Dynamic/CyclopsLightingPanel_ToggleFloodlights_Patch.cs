using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning.Metadata.Extractor;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class CyclopsLightingPanel_ToggleFloodlights_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsLightingPanel t) => t.ToggleFloodlights());

    public static bool Prefix(CyclopsLightingPanel __instance, out bool __state)
    {
        __state = __instance.floodlightsOn;
        return true;
    }

    public static void Postfix(CyclopsLightingPanel __instance, bool __state)
    {
        if (__state != __instance.floodlightsOn && __instance.TryGetIdOrWarn(out NitroxId id))
        {
            // The report names the part this press changed: the server keeps the rest as it has it (a Power Share switch or a light that this client has not got news of yet)
            Resolve<Entities>().BroadcastMetadataUpdate(id, CyclopsLightingMetadataExtractor.ExtractChange(__instance, CyclopsLightingMetadata.CHANGED_FLOOD));
        }
    }
}
