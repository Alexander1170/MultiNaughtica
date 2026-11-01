using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A door root with two key terminals (the Gun beach entry and elevator): the terminal that gets the key broadcasts "ToggleDoor" to its root, which marks the OTHER terminal as
/// used here. Only the terminal that was clicked was reported (<see cref="PrecursorKeyTerminal_OnHandClick_Patch" />), so for another player and after a restart the second one
/// looked unused and offered to take a key for a door that is already open.
/// </summary>
public sealed partial class PrecursorKeyTerminal_ToggleDoor_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PrecursorKeyTerminal t) => t.ToggleDoor(default(bool)));

    public static void Prefix(PrecursorKeyTerminal __instance, out bool __state)
    {
        __state = __instance.slotted;
    }

    public static void Postfix(PrecursorKeyTerminal __instance, bool open, bool __state)
    {
        // The clicked terminal was already marked when its click ran, only a terminal that this call marked is news
        if (!open || __state || !__instance.slotted || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        if (__instance.TryGetIdOrWarn(out NitroxId id))
        {
            Resolve<Entities>().BroadcastMetadataUpdate(id, new PrecursorKeyTerminalMetadata(true));
        }
    }
}
