namespace NitroxClient.GameLogic;

/// <summary>
/// Which changes of the scan target of a scanner room are a choice that the other players must follow (the design notes (not part of this repository), B9). Plain data, no game types.
/// <para>
/// The target is set through <c>MapRoomFunctionality.StartScanning</c>, called by the player's click on a resource or on the cancel button (<c>uGUI_MapRoomScanner.OnStartScan</c>,
/// <c>uGUI_MapRoomCancel.OnPointerClick</c> -> <c>OnCancelScan</c>) and by <c>MapRoomFunctionality.Start</c> with the room's own value. There is a fourth caller that is not a choice:
/// <c>uGUI_MapRoomScanner.OnDisable</c> cancels the scan (<c>StartScanning(None)</c>) whenever the screen of the room is deactivated or destroyed. That happens when the room object itself
/// is destroyed (the whole base lost on this client, which the Debug command <c>dropbase</c> simulates with <c>Object.Destroy</c> of the <c>Base</c>, a room that is deconstructed, a local
/// base the server has no entity for that a resync destroys), when the power of the room is down (<c>OnPowerDown</c>, and <c>Start</c> of a room that is not powered yet in survival) and
/// possibly when the game shuts down. Sent to the server, that cancel put <c>None</c> over the target everybody had chosen: in a live two-player test (a resync,
/// where the harness had destroyed Bob's whole base with <c>dropbase</c> before the resync) the server and the other player lost the target of the room, and the room that the resync spawned
/// got <c>None</c> back from the server.
/// </para>
/// <para>
/// A resync of an intact base does NOT destroy the room (read from the vanilla IL, not observed live): the room is a direct child of the <c>Base</c>, <c>Base.ClearGeometry</c> only destroys
/// the children of the cell objects, <c>Base.BuildMapRoomGeometry</c> returns early when the room exists, and <c>BuildingResyncProcessor.ClearBaseChildren</c> only destroys <c>IBaseModule</c>,
/// <c>Constructable</c> and <c>PlaceTool</c> children. The room stays and <c>RestoreMapRoom</c> puts the server's value on it (the design notes (not part of this repository), b8b9.ps1 checks
/// b9-target-after-intact-resync and b9-target-healed-by-intact-resync).
/// </para>
/// </summary>
public static class MapRoomScanTargetRules
{
    // The game runs on one thread; a depth instead of a flag because the finalizer of the patch that closes the scope must be harmless when it runs twice or without its prefix
    private static int screenDisableDepth;

    /// <summary>
    /// True while the game runs <c>uGUI_MapRoomScanner.OnDisable</c> (the screen of a room is going away): a <c>StartScanning</c> call in that time is the lifecycle of the room, not a choice.
    /// </summary>
    public static bool ScreenDisableRunning => screenDisableDepth > 0;

    public static void EnterScreenDisable()
    {
        screenDisableDepth++;
    }

    public static void ExitScreenDisable()
    {
        if (screenDisableDepth > 0)
        {
            screenDisableDepth--;
        }
    }

    /// <summary>
    /// A call of <c>StartScanning</c> that ended with another target than it started with is sent as metadata of the room only when a player made it. The call with the room's own value
    /// (<c>MapRoomFunctionality.Start</c>) changes nothing, and the cancel of a disabled screen is the room's lifecycle: the server keeps the last choice, and the room is restored with
    /// it at the next join or resync.
    /// </summary>
    public static bool ShouldBroadcast(bool targetChanged)
    {
        return targetChanged && !ScreenDisableRunning;
    }
}
