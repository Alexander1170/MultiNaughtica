using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The button inside a room that unlocks the door from the inside (the Aurora cargo room keypad has one next to its numpad): it unlocks the whole keypad group, which the
/// other players were never told.
/// </summary>
public sealed partial class KeypadDoorConsoleUnlock_UnlockDoorButtonPress_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((KeypadDoorConsoleUnlock t) => t.UnlockDoorButtonPress());

    public static void Postfix(KeypadDoorConsoleUnlock __instance)
    {
        if (__instance.TryGetComponentInParent(out NitroxEntity entity, true))
        {
            Resolve<Entities>().BroadcastMetadataUpdate(entity.Id, new KeypadMetadata(true));
        }
        else
        {
            Log.Warn($"[{nameof(KeypadDoorConsoleUnlock_UnlockDoorButtonPress_Patch)}] No NitroxEntity found in the hierarchy of {__instance.gameObject.GetFullHierarchyPath()}");
        }
    }
}
