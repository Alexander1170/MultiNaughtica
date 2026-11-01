using System.Collections.Generic;
using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Bases;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sends the water of a base to the server and the other clients, only from the player who simulates the base (the holder of its lock), every
/// <see cref="BaseFloodRules.SEND_INTERVAL_SECONDS"/> at most and only when it changed. Without this every client floods its own copy of the base: a compartment looks different
/// to each player, a player who joins later sees the flood start again from nothing, and the water is gone after a save. The others apply what the holder sends
/// (<see cref="NitroxClient.GameLogic.Spawning.Metadata.Processor.BaseFloodMetadataProcessor"/>). Based on upstream pull request 2811 (extractor, processor, metadata); the sending is
/// limited to the holder and to changes, and what is applied no longer has to be "more flooded", so that the water can drain after a repair.
/// </summary>
public sealed partial class BaseFloodSim_Update_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((BaseFloodSim t) => t.Update());

    private static readonly Dictionary<NitroxId, float> nextSendTimeByBaseId = [];
    private static readonly Dictionary<NitroxId, float> lastSentSumByBaseId = [];
    private static readonly Dictionary<NitroxId, float> lastSentTimeByBaseId = [];
    private static readonly Dictionary<NitroxId, int> lastSentLengthByBaseId = [];

    public static void Postfix(BaseFloodSim __instance)
    {
        if (!__instance.IsInitialized() || !__instance.baseComp || !__instance.baseComp.TryGetNitroxId(out NitroxId baseId) || !Resolve<SimulationOwnership>().HasAnyLockType(baseId))
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (nextSendTimeByBaseId.TryGetValue(baseId, out float nextSendTime) && now < nextSendTime)
        {
            return;
        }
        nextSendTimeByBaseId[baseId] = now + BaseFloodRules.SEND_INTERVAL_SECONDS;

        // flatValueGrid is only filled by OnProtoSerialize, which the game calls when it saves
        __instance.OnProtoSerialize(null);
        float sum = BaseFloodRules.Sum(__instance.flatValueGrid);
        float secondsSinceLastSend = lastSentTimeByBaseId.TryGetValue(baseId, out float lastSentTime) ? now - lastSentTime : 0f;
        int gridLength = __instance.flatValueGrid.Length;
        if (!BaseFloodRules.ShouldSend(sum, lastSentSumByBaseId.TryGetValue(baseId, out float lastSent) ? lastSent : null, secondsSinceLastSend, gridLength,
                                       lastSentLengthByBaseId.TryGetValue(baseId, out int lastLength) ? lastLength : null))
        {
            return;
        }

        lastSentSumByBaseId[baseId] = sum;
        lastSentTimeByBaseId[baseId] = now;
        lastSentLengthByBaseId[baseId] = gridLength;
        Resolve<Entities>().EntityMetadataChanged(__instance, baseId);
    }
}
