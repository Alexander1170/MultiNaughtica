using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.Extensions;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.Settings;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The trigger on the hologram in the middle of the build room that the player's hand finds (<see cref="NavigatorConsole" />): "Use navigator console" when the player can, nothing otherwise. The game's hand walks up
/// from the collider it looks at to the first object that has a hand target, which is this one.
/// </summary>
public class NavigatorConsoleHandTarget : MonoBehaviour, IHandTarget
{
    public NavigatorConsole Console { get; set; }

    public void OnHandHover(GUIHand hand)
    {
        if (!Console || !Console.CanEnter(out _))
        {
            return;
        }
        HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_NavigatorConsoleUse", true, GameInput.Button.LeftHand);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, string.Empty, false, GameInput.Button.None);
        HandReticle.main.SetIcon(HandReticle.IconType.Interact, 1f);
    }

    public void OnHandClick(GUIHand hand)
    {
        if (Console)
        {
            Console.Enter();
        }
    }
}

/// <summary>
/// The navigator console of a Cyclops with the Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md): the terrain hologram that floats in the middle of the build room. A hand target on the hologram itself (a
/// trigger the size of its disc, nothing solid) opens it; while it is open the player stays where they stand (they stay in the normal mode, so everybody else keeps seeing them there), the camera is turned
/// to the map, the cursor is free and the walking and looking are locked (the game's own input group, the way the PDA and the chat do it). The mouse turns, tilts and zooms the hologram
/// (<see cref="CyclopsTerrainSonar" />), Shift and the wheel (or + and -, or the buttons) make it bigger or smaller, and a screen overlay (<see cref="NavigatorConsoleScreen" />) draws the icons of the
/// contacts on it (<see cref="NavigatorContacts" />). It closes with the exit key, Escape or the button, and by itself when the player leaves, dies, is moved or the module goes.
/// <para>
/// One component on every real Cyclops (added by <c>SubRoot_Start_Patch</c>). The hand target exists while the module is in; nothing else runs while the console is closed.
/// </para>
/// </summary>
public class NavigatorConsole : MonoBehaviour
{
    private const string HAND_TARGET_NAME = "NitroxNavigatorConsoleHandTarget";
    private const float POLL_SECONDS = 0.25f;
    private const float CONTACTS_SECONDS = 0.5f;
    private const int TURN_FRAMES = 14;
    private const float TURN_STEP = 0.4f;
    private const float TURN_DONE_DEGREES = 0.5f;

    private readonly List<Contact> contacts = [];
    private SubRoot subRoot;
    private CyclopsTerrainSonar sonar;
    private GameObject handTarget;
    private NavigatorConsoleScreen screen;
    private float nextPoll;
    private float nextContacts;
    private bool opening;
    private bool closing;
    private int savedQuickSlot = -1;
    private NitroxId cyclopsId;
    private readonly NavigatorViewKeeper keeper = new();

#if DEBUG
    /// <summary>Debug (the live check): kept for the harness; the hand target is the hologram's own volume now and is not moved by it; zero in play.</summary>
    public static Vector3 DebugHandNudge { get; set; }

    /// <summary>Debug (the live check): moves the middle of the map in the hull's frame; zero in play.</summary>
    public static Vector3 DebugMapNudge { get; set; }
#endif

    /// <summary>The console that is open on this client, if any.</summary>
    public static NavigatorConsole Current { get; private set; }

    public SubRoot SubRoot => subRoot;
    public bool IsOpen => screen;

    /// <summary>
    /// The view of the map (its turn, roll and zoom). It stays where the navigator left it: the console does not touch it when someone sits down or gets up (<see cref="NavigatorViewKeeper" />), the map is drawn
    /// with it all the time, and it is kept on this machine for this Cyclops. Only the navigator's Reset puts it back.
    /// </summary>
    public NavigatorMapView View => keeper.View;

    public IReadOnlyList<Contact> Contacts => contacts;

    /// <summary>The terrain hologram in the build room (the one of <see cref="CyclopsTerrainSonar" />), null while it is not built.</summary>
    public TerrainHologram Hologram => Sonar ? Sonar.Hologram : null;

    /// <summary>The frame of the map in the build room: its middle is the middle of the map.</summary>
    public Transform MapFrame => Sonar ? Sonar.MapFrame : null;

    private CyclopsTerrainSonar Sonar => sonar ? sonar : sonar = GetComponent<CyclopsTerrainSonar>();

    /// <summary>Whether the map also draws the minor creatures (the small biters); the screen's key toggles it.</summary>
    public bool IncludeMinor { get; set; }

    /// <summary>
    /// The range in metres that the map shows at zoom 1: the one that the hologram was built for (the server's terrain range, and farther while the engineer sends the power to the systems). The scale of the
    /// hologram and the radius of its terrain are fixed with it, and the icons and the range label must keep to them.
    /// </summary>
    public float Range
    {
        get
        {
            TerrainHologram hologram = Hologram;
            return hologram != null && hologram.Exists ? hologram.Range : LiveRange;
        }
    }

    private float LiveRange => Mathf.Min(VehicleUpgradeSettingsRules.MAX_TERRAIN_SONAR_RANGE, VehicleUpgradeSettings.TerrainSonarRange * EngineerState.SonarFactor(subRoot));

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        NavigatorContacts.Subscribe();
        NavMarks.Subscribe();
        NavRoute.Subscribe();
    }

    private void OnDestroy()
    {
        Close();
        if (cyclopsId != null)
        {
            NavMarks.ForgetCyclops(cyclopsId);
        }
        if (handTarget)
        {
            Destroy(handTarget);
        }
    }

    private void Update()
    {
        if (IsOpen)
        {
            UpdateOpen();
        }
        if (cyclopsId != null)
        {
            WriteView(keeper.TakeDue(Time.unscaledTime));
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        RefreshHandTarget();
        LinkView();
        SyncMarks();
    }

    /// <summary>The map is drawn with this console's view all the time, not only while the console is open: it stands at the angle where the last navigator left it.</summary>
    private void LinkView()
    {
        CyclopsTerrainSonar linked = Sonar;
        if (linked && linked.ActiveView != View)
        {
            linked.ActiveView = View;
        }
    }

    /// <summary>The view that was kept for this Cyclops on this machine, taken once the Cyclops has its id (a text that cannot be read leaves the map as it first stands).</summary>
    private void LoadView()
    {
        if (keeper.Loaded)
        {
            return;
        }
        try
        {
            string key = NavigatorViewKeeper.PrefKey(cyclopsId.ToString());
            if (keeper.Load(PrefsStore.GetString(key, string.Empty)))
            {
                Log.Info($"[VehicleUpgrades] navigator console: the map of {subRoot.name} is as it was left: {View.ToText()} (turn, roll, zoom)");
            }
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] navigator console: the kept view of the map could not be read ({exception.GetType().Name}); it stands as it first did");
        }
    }

    /// <summary>Keeps the view on this machine (the text from <see cref="NavigatorViewKeeper" />, null when there is nothing to write).</summary>
    private void WriteView(string text)
    {
        if (text == null || cyclopsId == null)
        {
            return;
        }
        try
        {
            PrefsStore.SetString(NavigatorViewKeeper.PrefKey(cyclopsId.ToString()), text);
            PrefsStore.Save();
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] navigator console: the view of the map could not be kept ({exception.GetType().Name})");
        }
    }

    /// <summary>
    /// The marks of this Cyclops get their pings on the HUD of the players who are aboard (<see cref="NavMarks.Sync" />). Done here, a few times a second, because the marks are known by the Cyclops' id and
    /// this is the component that knows when the Cyclops is in the game.
    /// </summary>
    private void SyncMarks()
    {
        if (cyclopsId == null && !subRoot.TryGetNitroxId(out cyclopsId))
        {
            return;
        }
        LoadView();
        bool aboard = Player.main && Player.main.currentSub == subRoot;
        NavMarks.Sync(cyclopsId, aboard && VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received);
    }

    /// <summary>The id of this Cyclops, once the game has given it one.</summary>
    public bool TryGetCyclopsId(out NitroxId id)
    {
        id = cyclopsId;
        return id != null;
    }

    /// <summary>Places a mark of this kind at a place in the world on the map of this Cyclops (shared with everybody). Null when the Cyclops has the most marks it keeps.</summary>
    public NavMark PlaceMark(Vector3 world, byte kind)
    {
        return cyclopsId == null ? null : NavMarks.Place(cyclopsId, world, kind);
    }

    public void RemoveMark(NitroxId markId)
    {
        if (cyclopsId != null && markId != null)
        {
            NavMarks.RemoveByPlayer(cyclopsId, markId);
        }
    }

    public void ClearMarks()
    {
        if (cyclopsId != null)
        {
            NavMarks.ClearByPlayer(cyclopsId);
        }
    }

    /// <summary>Appends a waypoint at a place in the world to the route of this Cyclops (shared with everybody). Null when the route has no room or the point is on top of the last one.</summary>
    public NavWaypoint AddWaypoint(Vector3 world)
    {
        return cyclopsId == null ? null : NavRoute.Append(cyclopsId, world);
    }

    public bool RemoveLastWaypoint() => cyclopsId != null && NavRoute.RemoveLastByPlayer(cyclopsId);

    public void ClearRoute()
    {
        if (cyclopsId != null)
        {
            NavRoute.ClearByPlayer(cyclopsId);
        }
    }

    /// <summary>The waypoints of the route of this Cyclops and the index of the next.</summary>
    public IReadOnlyList<NavWaypoint> RoutePoints => cyclopsId == null ? System.Array.Empty<NavWaypoint>() : NavRoute.PointsOf(cyclopsId);

    public int RouteNext => cyclopsId == null ? 0 : NavRoute.NextOf(cyclopsId);

    /// <summary>The kind of mark that the next click places (keys 1, 2 and 3 in the console).</summary>
    public byte MarkKind { get; set; } = CyclopsNavRules.MARK_POINT;

    /// <summary>The hand target exists while the module is in, and is the hologram's volume.</summary>
    private void RefreshHandTarget()
    {
        bool available = TerrainSonar.IsAvailable(subRoot);
        if (available && !handTarget && MapFrame)
        {
            handTarget = new GameObject(HAND_TARGET_NAME) { layer = LayerID.Useable };
            // a child of the frame, so that the slab turns, tilts and scales with the disc
            handTarget.transform.SetParent(MapFrame, false);
            handTarget.transform.localPosition = Vector3.zero;
            handTarget.transform.localRotation = Quaternion.identity;
            BoxCollider slab = handTarget.AddComponent<BoxCollider>();
            slab.isTrigger = true;
            handTarget.AddComponent<NavigatorConsoleHandTarget>().Console = this;
            Log.Info($"[VehicleUpgrades] navigator console: hand target on the hologram of {subRoot.name} in the build room, hull-local {MapFrame.localPosition:F2} (a slab on the disc)");
        }
        if (handTarget)
        {
            handTarget.SetActive(available);
            if (MapFrame)
            {
                // the trigger is a slab on the disc (as wide as the disc, not under the smallest a hand can aim at, half a metre thick) and nothing solid: it never blocks the walking, and a ray from eyes
                // that are inside a big hologram's volume still finds it when they look at the disc from above. The frame is scaled, so the size is given in its own units.
                (float width, float thickness) = NavigatorConsoleLayout.TargetSize(Sonar ? Sonar.Radius : NavigatorConsoleLayout.DEFAULT_RADIUS);
                float scale = Mathf.Max(0.0001f, MapFrame.localScale.x);
                handTarget.GetComponent<BoxCollider>().size = new Vector3(width / scale, thickness / scale, width / scale);
            }
        }
        if (!available && IsOpen)
        {
            Close();
        }
    }

    /// <summary>Whether the local player can use the console now; <paramref name="why" /> says why not.</summary>
    public bool CanEnter(out string why)
    {
        why = null;
        Player player = Player.main;
        TerrainHologram hologram = Hologram;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!TerrainSonar.IsAvailable(subRoot))
        {
            why = "the Cyclops has no Cyclops Terrain Sonar, or the vehicle upgrades are off";
        }
        else if (IsOpen || opening || Current)
        {
            why = "a console is open already";
        }
        else if (!player || player.currentSub != subRoot)
        {
            why = "the player is not aboard this Cyclops";
        }
        else if (!player.IsAlive() || player.GetMode() != Player.Mode.Normal || player.cinematicModeActive)
        {
            why = "the player is busy (dead, at the helm, in a cinematic or a vehicle)";
        }
        else if (!MainCamera.camera)
        {
            why = "there is no camera";
        }
        else if (hologram == null || !hologram.Exists || !MapFrame)
        {
            why = "the hologram is not built yet";
        }
        return why == null;
    }

    public void Enter()
    {
        if (!CanEnter(out string why))
        {
            Log.Info($"[VehicleUpgrades] navigator console: cannot be used now: {why}");
            return;
        }
        StartCoroutine(OpenRoutine());
    }

    private IEnumerator OpenRoutine()
    {
        opening = true;
        // (the view is not reset here: the map stands as the last navigator left it)
        IncludeMinor = false;
        MarkKind = CyclopsNavRules.MARK_POINT;
        if (Sonar)
        {
            Sonar.ActiveView = View;
        }

        // the camera is turned to the map first: the input group locks the look as soon as it is taken, and a map that is out of view cannot be used
        yield return TurnToMap();

        GameObject screenObject = new("NitroxNavigatorConsole");
        screen = screenObject.AddComponent<NavigatorConsoleScreen>();
        screen.Setup(this);
        yield return null; // the input group needs its first frame before it can be selected (the chat does the same)

        string why = null;
        if (!this || !screen || !CanStayOpen(out why))
        {
            Log.Info($"[VehicleUpgrades] navigator console: not opened: {why ?? "closed meanwhile"}");
            opening = false;
            Close();
            yield break;
        }

        // The hands: put away what is held and remember the quick slot, as the helm's chair does
        Inventory inventory = Inventory.main;
        savedQuickSlot = inventory.quickSlots.activeSlot;
        inventory.ReturnHeld(true);
        GameInput.AutoMove = false;
        Current = this;
        screen.Open();
        opening = false;
        Log.Info($"[VehicleUpgrades] navigator console: opened on {subRoot.name}, map middle hull-local {MapFrame.localPosition:F2}, range {Range:F0} m, camera {CameraAimError():F1} degrees from the map");
    }

    /// <summary>
    /// Turns the camera to the middle of the map in a few frames (what is still missing is read off where the camera really looks and added to the camera control's yaw and pitch, so that it works in the frame of
    /// a Cyclops too). The hologram floats at waist height; without this the navigator looks wherever they happened to look when they clicked, which is where the hand target is.
    /// </summary>
    private IEnumerator TurnToMap()
    {
        for (int frame = 0; frame < TURN_FRAMES; frame++)
        {
            Camera camera = MainCamera.camera;
            if (!camera || !MainCameraControl.main || !MapFrame)
            {
                yield break;
            }
            (float yawError, float pitchError) = AimError(camera);
            if (Mathf.Abs(yawError) < TURN_DONE_DEGREES && Mathf.Abs(pitchError) < TURN_DONE_DEGREES)
            {
                yield break;
            }
            MainCameraControl.main.rotationX += yawError * TURN_STEP;
            MainCameraControl.main.rotationY += pitchError * TURN_STEP;
            yield return null;
        }
    }

    /// <summary>The yaw and the pitch that the camera still has to turn to look at the middle of the map.</summary>
    private (float Yaw, float Pitch) AimError(Camera camera)
    {
        Vector3 eye = camera.transform.position;
        Vector3 target = MapFrame.position;
        Vector3 forward = camera.transform.forward;
        (float wantedYaw, float wantedPitch) = NavigatorConsoleLayout.AimAngles((eye.x, eye.y, eye.z), (target.x, target.y, target.z));
        (float currentYaw, float currentPitch) = NavigatorConsoleLayout.AimAngles((0f, 0f, 0f), (forward.x, forward.y, forward.z));
        return (Mathf.DeltaAngle(currentYaw, wantedYaw), wantedPitch - currentPitch);
    }

    private float CameraAimError()
    {
        Camera camera = MainCamera.camera;
        if (!camera || !MapFrame)
        {
            return -1f;
        }
        return Vector3.Angle(camera.transform.forward, MapFrame.position - camera.transform.position);
    }

    /// <summary>Where a real position is on the map, in the world (the icons use it); the position itself when the map is not built yet.</summary>
    public bool TryMapPoint(Vector3 world, out Vector3 point)
    {
        TerrainHologram hologram = Hologram;
        if (hologram != null && hologram.Exists)
        {
            point = hologram.Aligner.WorldToMap(world);
            return true;
        }
        point = world;
        return false;
    }

    /// <summary>The radius in metres, in the world, of the disc of terrain at the current zoom (where the icons stop and the rim begins).</summary>
    public float DiscRadius => Hologram != null ? Hologram.DiscRadiusWorld : 0f;

    /// <summary>Makes the hologram bigger (positive) or smaller (negative) by that many notches: the player's own size, kept on their machine (<see cref="CyclopsTerrainSonar.SetRadius" />).</summary>
    public void ChangeSize(float notches)
    {
        if (Sonar)
        {
            Sonar.StepRadius(notches);
        }
    }

    /// <summary>Gives the hologram its first size.</summary>
    public void ResetSize()
    {
        if (Sonar)
        {
            Sonar.SetRadius(NavigatorConsoleLayout.DEFAULT_RADIUS);
        }
    }

    private void UpdateOpen()
    {
        if (!CanStayOpen(out string why))
        {
            Log.Info($"[VehicleUpgrades] navigator console: closed because {why}");
            Close();
            return;
        }
        if (Time.unscaledTime >= nextContacts)
        {
            nextContacts = Time.unscaledTime + CONTACTS_SECONDS;
            NavigatorContacts.Collect(subRoot, Range, IncludeMinor, contacts);
        }
    }

    /// <summary>Whether the console may stay open: the player is still near the hologram, alive and free, the module is still in and the hologram is still there.</summary>
    private bool CanStayOpen(out string why)
    {
        why = null;
        Player player = Player.main;
        Transform frame = MapFrame;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!TerrainSonar.IsAvailable(subRoot))
        {
            why = "the Terrain Sonar is out or the upgrades are off";
        }
        else if (!player || !player.IsAlive())
        {
            why = "the player is gone or dead";
        }
        else if (player.currentSub != subRoot)
        {
            why = "the player left the Cyclops";
        }
        else if (player.GetMode() != Player.Mode.Normal || player.cinematicModeActive)
        {
            why = "the player is in another mode";
        }
        else if (!frame || Hologram == null || !Hologram.Exists)
        {
            why = "the hologram is gone (rebuilt for another range, or the Cyclops is out of the game)";
        }
        else if (Vector3.Distance(player.transform.position, frame.position) > NavigatorConsoleLayout.MaxUseDistance(DiscRadius))
        {
            why = "the player was moved away from the hologram";
        }
        return why == null;
    }

    /// <summary>Closes the console: gives the input back, takes the overlay away, keeps the map's view where the navigator left it and puts the hands back. Safe to call at any time and more than once.</summary>
    public void Close()
    {
        if (closing)
        {
            return;
        }
        closing = true;
        try
        {
            bool wasCurrent = Current == this;
            if (screen)
            {
                screen.Shut();
                Destroy(screen.gameObject);
            }
            screen = null;
            // the hologram stays, and so does what the navigator did to its view: it is kept where it was left
            if (cyclopsId != null)
            {
                WriteView(keeper.Flush());
            }
            contacts.Clear();
            if (wasCurrent)
            {
                Current = null;
                if (savedQuickSlot >= 0 && Inventory.main != null && Player.main && Player.main.GetMode() == Player.Mode.Normal)
                {
                    Inventory.main.quickSlots.SelectImmediate(savedQuickSlot);
                }
                Log.Info($"[VehicleUpgrades] navigator console: closed on {(subRoot ? subRoot.name : "a destroyed Cyclops")}");
            }
            savedQuickSlot = -1;
        }
        finally
        {
            opening = false;
            closing = false;
        }
    }

    /// <summary>The input group was taken from the screen by something else (the game menu, another group): the console is over.</summary>
    public void OnScreenDeselected()
    {
        if (!closing && IsOpen)
        {
            Close();
        }
    }
}
