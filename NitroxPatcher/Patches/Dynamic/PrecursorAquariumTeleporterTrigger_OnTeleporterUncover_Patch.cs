using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// When the teleporter of the Prison Aquarium is uncovered, the Emperor is told whether the player is at the teleporter (inside its trigger). Only the local player was asked:
/// with another player there he was in two places. Anyone inside counts (see <see cref="PrecursorAquariumTeleporterTrigger_CheckRange_Patch" />).
/// </summary>
public sealed partial class PrecursorAquariumTeleporterTrigger_OnTeleporterUncover_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorAquariumTeleporterTrigger t) => t.OnTeleporterUncover());

    /*
     * bool atTeleporter = UWE.Utils.IsInsideCollider(this.trigger, playerPosition);   <--- the call is replaced by AnyPlayerInside(trigger, local position)
     */
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions).MatchStartForward(new CodeMatch(OpCodes.Call, Reflect.Method(() => UWE.Utils.IsInsideCollider(default(Collider), default(Vector3)))))
                                            .ThrowIfInvalid("Could not find the inside check in PrecursorAquariumTeleporterTrigger.OnTeleporterUncover")
                                            .SetOperandAndAdvance(Reflect.Method(() => AnyPlayerInside(default, default)))
                                            .InstructionEnumeration();
    }

    public static bool AnyPlayerInside(Collider trigger, Vector3 localPlayerPosition)
    {
        if (UWE.Utils.IsInsideCollider(trigger, localPlayerPosition))
        {
            return true;
        }

        foreach (RemotePlayer remote in Resolve<PlayerManager>().GetAll())
        {
            if (remote.Body && UWE.Utils.IsInsideCollider(trigger, remote.Body.transform.position))
            {
                return true;
            }
        }

        return false;
    }
}
