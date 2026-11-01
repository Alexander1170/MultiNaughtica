using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class KeypadDoorConsole_AcceptNumberField_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((KeypadDoorConsole t) => t.AcceptNumberField());

    public static void Postfix(KeypadDoorConsole __instance)
    {
        // The console is not always on the object that carries the id: the keypads of the Aurora cargo room and generic lab have it on a child (Keypad1) of the prefab root
        if (__instance.TryGetComponentInParent(out NitroxEntity entity, true))
        {
            KeypadMetadata keypadMetadata = new(__instance.unlocked);
            Resolve<Entities>().BroadcastMetadataUpdate(entity.Id, keypadMetadata);
        }
        else
        {
            Log.Warn($"[{nameof(KeypadDoorConsole_AcceptNumberField_Patch)}] No NitroxEntity found in the hierarchy of {__instance.gameObject.GetFullHierarchyPath()}");
        }
    }
}
