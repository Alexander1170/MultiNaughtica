using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Vanilla <c>uGUI_MapRoomScanner.OnDisable</c> cancels the scan of its room (<c>OnCancelScan</c> -> <c>MapRoomFunctionality.StartScanning(None)</c>) whenever the screen of the room is
/// deactivated or destroyed: the room object is destroyed (the whole base lost on this client, as the Debug command <c>dropbase</c> simulates it, a room that is deconstructed), a room
/// without power (<c>OnPowerDown</c>, an unpowered <c>Start</c> in survival), possibly the game shutting down. A resync of an intact base does not get here: it keeps the room, a direct
/// child of the <c>Base</c> that <c>Base.ClearGeometry</c> leaves alone (vanilla IL, see the design notes (not part of this repository)). The cancel is the life of the room on this client, not a choice of the player,
/// and <see cref="MapRoomFunctionality_StartScanning_Patch"/> must not send it: it put <c>None</c> on the server and on the other players' rooms over the target they had chosen, and the
/// resync that followed restored that <c>None</c> (seen in a live two-player test after a base was destroyed).
/// The player's own click on the cancel button (<c>uGUI_MapRoomCancel.OnPointerClick</c> -> <c>OnCancelScan</c>) does not pass through <c>OnDisable</c> and is still sent.
/// The scope is opened here and closed in a finalizer, so that an exception of the game's code cannot leave it open (see <see cref="MapRoomScanTargetRules"/>).
/// </summary>
public sealed partial class uGUI_MapRoomScanner_OnDisable_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_MapRoomScanner t) => t.OnDisable());

    public static void Prefix()
    {
        MapRoomScanTargetRules.EnterScreenDisable();
    }

    public static void Finalizer()
    {
        MapRoomScanTargetRules.ExitScreenDisable();
    }
}
