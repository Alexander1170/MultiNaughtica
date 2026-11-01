#if DEBUG
namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Live batch check b8b9 , check b9-target-kept-on-screen-off: the screen of a scanner room going away is not a choice of a player.<br/>
///     - <c>maproomscreen off|on</c> switches the screen of the scanner room nearest to the player off or on, which is what <c>MapRoomFunctionality.OnPowerDown</c> and <c>OnPowerUp</c> do
///     (<c>screenRoot.SetActive</c>). <c>uGUI_MapRoomScanner.OnDisable</c> runs inside the call and cancels the scan of the room on this client (vanilla), so the line shows <c>after=None</c> when the
///     scan target was set; <c>MapRoomFunctionality_StartScanning_Patch</c> must not send that to the server (<c>MapRoomScanTargetRules</c>).<br/>
///     - <c>maproomident</c> lists every scanner room of the scene with its NitroxId and the Unity instance id of the room and of its base. Two calls around a <c>resyncbuild</c> on an INTACT base
///     (no <c>dropbase</c>) show whether the room object survived it (same instance) and that no second room with the same NitroxId appeared. The vanilla IL says an intact base keeps its room (the room
///     is a direct child of the <c>Base</c>, <c>Base.ClearGeometry</c> only destroys the children of the cell objects); this is what makes that visible (checks b9-target-after-intact-resync,
///     b8-chips-after-intact-resync, b8-room-kept-on-intact-resync, b9-target-healed-by-intact-resync).<br/>
///     Prints one final line in every case (<c>done</c> or <c>failed reason=...</c>).
/// </summary>
public partial class HarnessConsoleCommands
{
    private void LiveCommand_maproomident(NotificationCenter.Notification n)
    {
        int count = 0;
        foreach (MapRoomFunctionality room in B8b9Rooms())
        {
            count++;
            Base parentBase = room.GetComponentInParent<Base>();
            string baseInstance = parentBase ? parentBase.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture) : "none";
            string instance = room.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture);
            string line = $"[SyncHarness] maproomident: room id={B8b9IdOf(room)} instance={instance} baseInstance={baseInstance} type={room.typeToScan} active={room.gameObject.activeInHierarchy}";
            Log.Info(line);
        }
        string done = $"[SyncHarness] maproomident: done {count} scanner rooms";
        Log.Info(done);
    }

    private void LiveCommand_maproomscreen(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "");
        if (mode != "on" && mode != "off")
        {
            Log.Warn("[SyncHarness] maproomscreen: failed reason=needs-on-or-off");
            return;
        }

        MapRoomFunctionality room = B8b9NearestRoom();
        if (!room || !room.screenRoot)
        {
            Log.Warn("[SyncHarness] maproomscreen: failed reason=no-scanner-room-with-a-screen");
            return;
        }

        TechType before = room.typeToScan;
        bool wasActive = room.screenRoot.activeSelf;
        try
        {
            room.screenRoot.SetActive(mode == "on");
        }
        catch (System.Exception exception)
        {
            string failure = $"[SyncHarness] maproomscreen: failed reason=exception {exception.GetType().Name} {exception.Message}";
            Log.Warn(failure);
            return;
        }
        string line = $"[SyncHarness] maproomscreen: done room={B8b9IdOf(room)} screen={mode} wasActive={wasActive} before={before} after={room.typeToScan}";
        Log.Info(line);
    }
}
#endif
