using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Nitrox.Model.DataStructures.Unity;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The Sea Emperor in the Prison Aquarium swims to the teleporter and blows the sand away depending on how far the LOCAL player is from the teleporter trigger
/// (<c>CheckRange</c>). Two players in the aquarium each ran their own Emperor with their own distance, so he was in two places. The distance is now the one of the nearest
/// player (all clients see the same players, give or take latency), see <see cref="PlayerRange" />.
/// </summary>
public sealed partial class PrecursorAquariumTeleporterTrigger_CheckRange_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorAquariumTeleporterTrigger t) => t.CheckRange());

    /*
     * float distance = Vector3.Distance(player.transform.position, base.transform.position);   <--- the call is replaced by NearestPlayerDistance(local position, trigger position)
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Call, Reflect.Method(() => Vector3.Distance(default, default))))
                                            .ThrowIfInvalid("Could not find the distance to the local player in PrecursorAquariumTeleporterTrigger.CheckRange")
                                            .SetOperandAndAdvance(Reflect.Method(() => NearestPlayerDistance(default, default)))
                                            .InstructionEnumeration();
    }

    public static float NearestPlayerDistance(Vector3 localPlayerPosition, Vector3 triggerPosition)
    {
        List<NitroxVector3> positions = [localPlayerPosition.ToDto()];
        foreach (RemotePlayer remote in Resolve<PlayerManager>().GetAll())
        {
            if (remote.Body)
            {
                positions.Add(remote.Body.transform.position.ToDto());
            }
        }

        return PlayerRange.NearestDistance(positions, triggerPosition.ToDto());
    }
}
