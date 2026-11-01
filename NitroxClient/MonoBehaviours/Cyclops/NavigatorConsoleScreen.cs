using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Navigator;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The screen of the navigator console (<see cref="NavigatorConsole" />): the game's own input group, the way the PDA and the chat are, so that the cursor is free and the player can neither walk nor look
/// while it is open, and a canvas laid over the game that draws what the hologram cannot: the icons of the contacts, placed by projecting their place on the hologram to the screen, the range, the
/// legend, the keys and a line for the icon under the cursor. The mouse is read raw: dragging turns the map any way (across spins it, up and down rolls it over, without a limit), the wheel zooms.
/// </summary>
public class NavigatorConsoleScreen : ConsoleInputGroup
{
    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const float HOVER_PIXELS = 26f;
    private const float STATUS_SECONDS = 0.5f;
    private const float CLICK_PIXELS = 6f;
    private const float MESSAGE_SECONDS = 3f;

    private sealed class IconView
    {
        public RectTransform Rect;
        public Image Image;
        public Contact Contact;
        public bool Used;
        public Vector2 Canvas;
    }

    private readonly Dictionary<string, IconView> icons = [];
    private readonly List<string> gone = [];
    private NavigatorConsole console;
    private RectTransform canvasRect;
    private RectTransform buttonPanel;
    private Text title;
    private Text rangeLine;
    private Text clearanceLine;
    private Text legend;
    private Text hint;
    private Text hover;
    private Text status;
    private Text message;
    private float messageUntil;
    private float dragTravel;
    private Vector2 rightDownAt;
    private bool rightDown;
    private readonly List<Image> segments = [];
    private readonly List<WaypointView> waypointViews = [];
    private readonly List<Vector2> chain = [];

    private sealed class WaypointView
    {
        public RectTransform Rect;
        public Image Image;
        public Text Label;
    }
    private int openFrame = int.MaxValue;
    private bool dragging;
    private Vector2 lastMouse;
    private bool shut;
    private float nextStatus;
    private string hoverKey;

    protected override string StationLogName => "navigator";

    public void Setup(NavigatorConsole owner)
    {
        console = owner;
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        AddClickCatcher();
        canvasRect = (RectTransform)transform;

        title = MakeText("Title", 30, TextAnchor.UpperLeft, new Color(0.75f, 0.95f, 1f));
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(900f, 44f));
        title.text = "NAVIGATOR CONSOLE";
        rangeLine = MakeText("Range", 22, TextAnchor.UpperLeft, Color.white);
        Anchor(rangeLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -76f), new Vector2(900f, 32f));
        clearanceLine = MakeText("Clearance", 24, TextAnchor.UpperLeft, new Color(1f, 0.65f, 0.15f));
        Anchor(clearanceLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -108f), new Vector2(900f, 34f));
        legend = MakeText("Legend", 20, TextAnchor.UpperLeft, Color.white);
        Anchor(legend.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -146f), new Vector2(520f, 200f));
        legend.text = string.Join("\n",
            "<color=#FF261F>Red</color>  leviathan (Reaper, Ghost, Sea Dragon)",
            "<color=#FF8C1A>Orange</color>  other danger (Warper, Crabsquid, Ampeel, Boneshark)",
            "<color=#8CD9FF>Blue</color>  vehicle    <color=#FFD933>Yellow</color>  beacon    <color=#99E6E6>Square</color>  base",
            "Coloured arrow  teammate    <color=#4DD9FF>Pin</color>  mark of the crew");
        status = MakeText("Status", 26, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.5f));
        Anchor(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 40f));
        hint = MakeText("Hint", 22, TextAnchor.LowerCenter, new Color(0.85f, 0.95f, 1f));
        // (the legacy text wraps at the width of its box: 1700 of the 1920 reference pixels, so that no line runs off the edges of the screen)
        hint.horizontalOverflow = HorizontalWrapMode.Wrap;
        Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1700f, 90f));
        hint.text = string.Concat("Click: mark on the terrain   Right click: route point   Alt+click: at the Cyclops' own depth   1 2 3: kind   Delete: remove the mark   Backspace: remove the last route point   P: sonar ping\n",
                                  "Drag: turn it any way   Wheel: zoom   Shift+Wheel or + -: make the hologram bigger or smaller   R: reset view   Shift+R: first size   T: small creatures\n",
                                  $"{ExitKeyName()} or Esc: leave");
        message = MakeText("Message", 26, TextAnchor.LowerCenter, new Color(0.6f, 1f, 0.7f));
        Anchor(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(1200f, 40f));
        message.text = string.Empty;
        hover = MakeText("Hover", 24, TextAnchor.MiddleLeft, Color.white);
        Anchor(hover.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(520f, 36f));
        hover.gameObject.SetActive(false);

        GameObject panel = new("Buttons", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        buttonPanel = (RectTransform)panel.transform;
        Anchor(buttonPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(960f, 118f));
        MakeButton(buttonPanel, "Reset view", new Vector2(0f, 0f), new Vector2(170f, 54f), () => console.View.Reset());
        MakeButton(buttonPanel, "Clear marks", new Vector2(190f, 0f), new Vector2(180f, 54f), () => { console.ClearMarks(); ShowMessage("Marks cleared"); });
        MakeButton(buttonPanel, "Clear route", new Vector2(390f, 0f), new Vector2(180f, 54f), () => { console.ClearRoute(); ShowMessage("Route cleared"); });
        MakeButton(buttonPanel, "Sonar ping", new Vector2(590f, 0f), new Vector2(180f, 54f), () => NavigatorSonar.Request(console.SubRoot, ShowMessage));
        MakeButton(buttonPanel, "Leave", new Vector2(790f, 0f), new Vector2(170f, 54f), () => console.Close());
        // the size of the hologram, a row of its own (two notches of 10 percent a click)
        MakeButton(buttonPanel, "Smaller", new Vector2(0f, -64f), new Vector2(170f, 54f), () => console.ChangeSize(-2f));
        MakeButton(buttonPanel, "Bigger", new Vector2(190f, -64f), new Vector2(180f, 54f), () => console.ChangeSize(2f));
        MakeButton(buttonPanel, "First size", new Vector2(390f, -64f), new Vector2(180f, 54f), () => console.ResetSize());
    }

    /// <summary>Takes the input: the cursor is freed and walking and looking are locked.</summary>
    public void Open()
    {
        openFrame = Time.frameCount;
        base.Select(true);
    }

    /// <summary>Gives the input back; the console calls it when it closes.</summary>
    public void Shut()
    {
        if (shut)
        {
            return;
        }
        shut = true;
        if (selected)
        {
            Deselect();
        }
    }

    public override void OnDeselect()
    {
        base.OnDeselect();
        if (!shut && console && !TakenAwayByAPress())
        {
            console.OnScreenDeselected();
        }
    }

    public override void Update()
    {
        base.Update(); // Escape deselects the group
        if (!shut && console && RetakeIfAsked())
        {
            return;
        }
        if (shut || !console)
        {
            return;
        }
        if (focused)
        {
            LogPresses();
        }
        if (!focused)
        {
            // (the frames since Open; openFrame is int.MaxValue until the first Open, and "openFrame + 10" would wrap round to a negative number and warn at once)
            if (!loggedUnfocused && Time.frameCount - openFrame > 10)
            {
                loggedUnfocused = true;
                Log.Warn($"[VehicleUpgrades] navigator console: the input group is not focused (selected {selected}), so the mouse and the keys are ignored");
            }
            return;
        }
        if (GameInput.GetButtonDown(GameInput.Button.Exit))
        {
            console.Close();
            return;
        }
        bool shiftHeld = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        if (UnityEngine.Input.GetKeyDown(KeyCode.R))
        {
            if (shiftHeld)
            {
                console.ResetSize();
            }
            else
            {
                console.View.Reset();
            }
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Equals) || UnityEngine.Input.GetKeyDown(KeyCode.Plus) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            console.ChangeSize(1f);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Minus) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            console.ChangeSize(-1f);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.T))
        {
            console.IncludeMinor = !console.IncludeMinor;
            ShowMessage(console.IncludeMinor ? "Small creatures: shown (Stalkers, Sandsharks and the like)" : "Small creatures: hidden");
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.P))
        {
            NavigatorSonar.Request(console.SubRoot, ShowMessage);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1))
        {
            SelectKind(CyclopsNavRules.MARK_POINT);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2))
        {
            SelectKind(CyclopsNavRules.MARK_HAZARD);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3))
        {
            SelectKind(CyclopsNavRules.MARK_TARGET);
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace) && console.RemoveLastWaypoint())
        {
            ShowMessage("Removed the last route point");
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.Delete) && hoverKey != null && icons.TryGetValue(hoverKey, out IconView doomed) && doomed.Contact.Kind == ContactKind.Mark)
        {
            console.RemoveMark(doomed.Contact.MarkId);
            ShowMessage($"Removed {doomed.Contact.Label}");
        }
        if (Time.frameCount - openFrame <= 2)
        {
            // the click that opened the console is still the mouse state of the first frames
            return;
        }

        Vector2 mouse = UnityEngine.Input.mousePosition;
        bool overButtons = buttonPanel && RectTransformUtility.RectangleContainsScreenPoint(buttonPanel, mouse, null);
        if (UnityEngine.Input.GetMouseButtonDown(0) && !overButtons)
        {
            dragging = true;
            lastMouse = mouse;
            dragTravel = 0f;
        }
        if (dragging)
        {
            if (UnityEngine.Input.GetMouseButton(0))
            {
                Vector2 delta = mouse - lastMouse;
                lastMouse = mouse;
                dragTravel += delta.magnitude;
                console.View.Drag(delta.x, delta.y);
                if (delta.sqrMagnitude > 0.01f)
                {
                    LogOnce("drag", $"the left button drags the map (mouse at {mouse.x:F0},{mouse.y:F0}, moved {delta.x:F0},{delta.y:F0}; yaw {console.View.Yaw:F0} tilt {console.View.Tilt:F0})");
                }
            }
            else
            {
                dragging = false;
                if (dragTravel < CLICK_PIXELS)
                {
                    // a click that did not move the mouse places a mark
                    HandleClick(mouse);
                }
            }
        }
        if (UnityEngine.Input.GetMouseButtonDown(1) && !overButtons)
        {
            rightDown = true;
            rightDownAt = mouse;
        }
        if (rightDown && UnityEngine.Input.GetMouseButtonUp(1))
        {
            rightDown = false;
            if (Vector2.Distance(rightDownAt, mouse) < CLICK_PIXELS)
            {
                HandleRightClick(mouse);
            }
        }
        float wheel = UnityEngine.Input.mouseScrollDelta.y;
        if (!Mathf.Approximately(wheel, 0f) && !overButtons)
        {
            if (shiftHeld)
            {
                console.ChangeSize(wheel);
                LogOnce("sizewheel", $"Shift and the mouse wheel change the size of the hologram (delta {wheel:F1}, radius {console.DiscRadius:F2} m)");
            }
            else
            {
                console.View.Scroll(wheel);
                LogOnce("wheel", $"the mouse wheel zooms the map (delta {wheel:F1}, zoom {console.View.Zoom:F2})");
            }
        }
    }

    private void SelectKind(byte kind)
    {
        console.MarkKind = kind;
        ShowMessage(kind == CyclopsNavRules.MARK_HAZARD ? "Next mark: hazard (orange)" : kind == CyclopsNavRules.MARK_TARGET ? "Next mark: target (yellow)" : "Next mark: point (cyan)");
    }

    private void ShowMessage(string text)
    {
        message.text = text;
        messageUntil = Time.unscaledTime + MESSAGE_SECONDS;
    }

    /// <summary>A click on the map: the place on the map's plane under the cursor is a place in the world, where the mark goes (at the depth of the Cyclops). Clicks beyond the disc, on an icon, or before the terrain is there do nothing.</summary>
    private void HandleClick(Vector2 mouse)
    {
        if (hoverKey != null)
        {
            return;
        }
        if (!TryMapPlacement("click", mouse, out Vector3 world, out bool onTerrain, out string why))
        {
            Ignored("click", why);
            return;
        }
        NavMark placed = console.PlaceMark(world, console.MarkKind);
        ShowMessage(placed != null ? $"Marked: {placed.Label} ({DepthText(world, onTerrain)})" : $"The Cyclops keeps at most {CyclopsNavRules.MAX_MARKS} marks");
    }

    /// <summary>A right click on the map appends a waypoint to the route (shared with everybody, in order).</summary>
    private void HandleRightClick(Vector2 mouse)
    {
        if (!TryMapPlacement("right click", mouse, out Vector3 world, out bool onTerrain, out string why))
        {
            Ignored("right click", why);
            return;
        }
        // a route point is stored where its pin is, on the terrain where it was clicked, like a mark; the helm aims a clearance above it (RouteClearance.AimY: the Cyclops cannot dive into the rock).
        // A point at the Cyclops' own depth (Alt+click, or a click that crossed no terrain) means "go to this depth": it is stored that much lower, so that the helm aims at the depth.
        string where;
        if (onTerrain)
        {
            where = RouteClearance.Describe(world.y);
        }
        else
        {
            float aimY = world.y;
            world.y = RouteClearance.PinYFor(aimY);
            where = $"at your depth, {HologramMath.DepthOf(aimY):F0} m";
        }
        NavWaypoint added = console.AddWaypoint(world);
        ShowMessage(added != null ? $"Route point {console.RoutePoints.Count} ({where})" : $"The route has at most {NavigationRules.MAX_WAYPOINTS} points, and a point must be {NavigationRules.MIN_WAYPOINT_SPACING:0} m from the last");
    }

    /// <summary>The place in the world that a click on the map means: where the ray from the camera through the cursor meets the map's plane, when that is inside the disc and the terrain is there.</summary>
    private static string DepthText(Vector3 world, bool onTerrain)
    {
        return $"depth {HologramMath.DepthOf(world.y):F0} m, {(onTerrain ? "on the terrain" : "at your depth")}";
    }

    /// <summary>
    /// Where a click puts a mark or a route point: on the terrain of the hologram under the cursor, at that terrain's real depth (the ray from the camera through the cursor against the chunks of the hologram, brought
    /// back to the world by the full mapping, <see cref="TerrainHologramAligner.MapToWorld" />). With Alt held, or when the ray crosses no terrain (open water above it, beyond the disc, a hologram whose meshes cannot be
    /// read), it is the place on the flat reference plane under the cursor, at the Cyclops' own depth, as it always was.
    /// </summary>
    private bool TryMapPlacement(string action, Vector2 mouse, out Vector3 world, out bool onTerrain, out string why)
    {
        onTerrain = false;
        bool atMyDepth = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
        Camera camera = MainCamera.camera;
        string fallback = atMyDepth ? "Alt is held" : "there is no hologram to pick on";
        if (!atMyDepth && camera && console.Hologram != null && console.Hologram.Exists && console.MapFrame)
        {
            TerrainHologram hologram = console.Hologram;
            bool hitTerrain = hologram.TryRaycastRelief(camera.ScreenPointToRay(mouse), out Vector3 hit, out string reliefWhy);
            float fromMiddle = hitTerrain ? (hit - console.MapFrame.position).magnitude : float.NaN;
            if (hitTerrain && fromMiddle <= console.DiscRadius)
            {
                world = hologram.Aligner.MapToWorld(hit);
                onTerrain = true;
                why = null;
                // one line for every pick, so that a mark in the wrong place can be followed from the cursor to the world
                Log.Info($"[VehicleUpgrades] navigator console: {action} hit the terrain ({hologram.LastPickReport}); on the hologram {hit.ToString("F3")} ({fromMiddle:F2} m of {console.DiscRadius:F2} m from the middle), in the world {world.ToString("F1")}, depth {HologramMath.DepthOf(world.y):F0} m; {hologram.CheckPickedChunk(world)}");
                return true;
            }
            fallback = hitTerrain ? $"the terrain under the cursor is beyond the disc ({fromMiddle:F2} m of {console.DiscRadius:F2} m; {hologram.LastPickReport})" : $"no terrain under the cursor ({reliefWhy}; {hologram.LastPickReport})";
        }
        bool placed = TryMapClick(mouse, out world, out why);
        Log.Info(placed
                     ? $"[VehicleUpgrades] navigator console: {action} went to the Cyclops' own depth ({fallback}); in the world {world.ToString("F1")}, depth {HologramMath.DepthOf(world.y):F0} m"
                     : $"[VehicleUpgrades] navigator console: {action} found no place ({fallback}; {why})");
        return placed;
    }

    private bool TryMapClick(Vector2 mouse, out Vector3 world, out string why)
    {
        world = default;
        why = null;
        Camera camera = MainCamera.camera;
        if (!camera)
        {
            why = "there is no camera";
            return false;
        }
        if (console.Hologram == null || !console.Hologram.Exists || !console.MapFrame)
        {
            why = "the hologram is not built";
            return false;
        }
        if (!console.Hologram.Aligner.TryRayToWorld(camera.ScreenPointToRay(mouse), out world) || !console.TryMapPoint(world, out Vector3 point))
        {
            why = "the ray from the cursor does not meet the plane of the map";
            return false;
        }
        float distance = (point - console.MapFrame.position).magnitude;
        if (distance > console.DiscRadius)
        {
            why = $"the cursor is {distance:F2} m from the middle of the map and the disc has a radius of {console.DiscRadius:F2} m";
            return false;
        }
        return true;
    }

    /// <summary>A click that did nothing says why, on the screen and (once a second) in the log, so that a map that cannot be clicked explains itself.</summary>
    private void Ignored(string what, string why)
    {
        ShowMessage("Click on the map's disc");
        LogOnce(what, $"a {what} was ignored: {why}", 1f);
    }

    private readonly Dictionary<string, float> loggedAt = [];
    private bool loggedUnfocused;

    /// <summary>The first time of a kind of input, or at most every <paramref name="seconds" /> (the first only when 0), in the log.</summary>
    private void LogOnce(string key, string text, float seconds = 0f)
    {
        float now = Time.unscaledTime;
        if (loggedAt.TryGetValue(key, out float last) && (seconds <= 0f || now - last < seconds))
        {
            return;
        }
        loggedAt[key] = now;
        Log.Info($"[VehicleUpgrades] navigator console: {text}");
    }

    private void LateUpdate()
    {
        if (message && messageUntil > 0f && Time.unscaledTime > messageUntil)
        {
            message.text = string.Empty;
            messageUntil = 0f;
        }
        if (shut || !console || !console.IsOpen)
        {
            return;
        }
        Camera camera = MainCamera.camera;
        if (camera)
        {
            UpdateIcons(camera);
        }
        UpdateTexts();
    }

    private void UpdateIcons(Camera camera)
    {
        foreach (IconView icon in icons.Values)
        {
            icon.Used = false;
        }
        bool built = console.Hologram != null && console.Hologram.Exists && console.MapFrame;
        if (built)
        {
            Vector3 middle = console.MapFrame.position;
            float rim = console.DiscRadius;
            Vector2 mouse = UnityEngine.Input.mousePosition;
            float bestPixels = HOVER_PIXELS;
            hoverKey = null;
            IReadOnlyList<Contact> contacts = console.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                Contact contact = contacts[i];
                if (!contact.Alive || !console.TryMapPoint(contact.Position, out Vector3 point))
                {
                    continue;
                }
                Vector3 offset = point - middle;
                (float scale, bool offMap) = ContactRules.RimClamp(offset.magnitude, rim);
                if (offMap && !ContactRules.ClampsToRim(contact.Kind))
                {
                    continue;
                }
                if (offMap)
                {
                    point = middle + offset * scale;
                }
                Vector3 screen = camera.WorldToScreenPoint(point);
                if (screen.z <= 0.05f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local))
                {
                    continue;
                }
                IconView icon = GetIcon(contact);
                icon.Used = true;
                icon.Canvas = local;
                icon.Rect.anchoredPosition = local;
                icon.Rect.sizeDelta = NavigatorIcons.SizeOf(contact.Kind, contact.Danger) * ContactRules.IconSize(offMap);
                Color color = contact.Color;
                color.a = offMap ? 0.55f : 1f;
                icon.Image.color = color;
                icon.Rect.localEulerAngles = new Vector3(0f, 0f, Turned(contact) ? HeadingOnScreen(camera, contact, screen) : 0f);
                float pixels = Vector2.Distance(new Vector2(screen.x, screen.y), mouse);
                if (pixels < bestPixels)
                {
                    bestPixels = pixels;
                    hoverKey = contact.Key;
                }
            }
        }
        UpdateRoute(camera, built);
        gone.Clear();
        foreach (KeyValuePair<string, IconView> pair in icons)
        {
            if (!pair.Value.Used)
            {
                gone.Add(pair.Key);
            }
        }
        foreach (string key in gone)
        {
            Destroy(icons[key].Rect.gameObject);
            icons.Remove(key);
        }
        UpdateHover();
    }

    /// <summary>
    /// The route on the map: a numbered ring for every waypoint, joined by lines (the legs already flown in grey, the one to the next waypoint bright, the rest dim), from the Cyclops at the middle. Points beyond the
    /// rim are put on it.
    /// </summary>
    private void UpdateRoute(Camera camera, bool built)
    {
        IReadOnlyList<NavWaypoint> points = built ? console.RoutePoints : System.Array.Empty<NavWaypoint>();
        int next = console.RouteNext;
        chain.Clear();
        int viewsUsed = 0;
        if (points.Count > 0)
        {
            Vector3 middle = console.MapFrame.position;
            float rim = console.DiscRadius;
            chain.Add(TryCanvas(camera.WorldToScreenPoint(middle), out Vector2 here) ? here : Vector2.zero);
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 canvasPoint = chain[0];
                bool visible = false;
                if (console.TryMapPoint(points[i].Position, out Vector3 point))
                {
                    Vector3 offset = point - middle;
                    (float scale, bool beyond) = ContactRules.RimClamp(offset.magnitude, rim);
                    if (beyond)
                    {
                        point = middle + offset * scale;
                    }
                    visible = TryCanvas(camera.WorldToScreenPoint(point), out canvasPoint);
                    if (!visible)
                    {
                        canvasPoint = chain[0];
                    }
                }
                chain.Add(canvasPoint);
                WaypointView view = GetWaypointView(viewsUsed++);
                view.Rect.gameObject.SetActive(visible);
                view.Rect.anchoredPosition = canvasPoint;
                view.Label.text = (i + 1).ToString();
                Color color = i < next ? new Color(0.6f, 0.6f, 0.6f, 0.7f) : i == next ? new Color(0.4f, 1f, 1f, 1f) : new Color(0.3f, 0.8f, 0.9f, 0.8f);
                view.Image.color = color;
                view.Label.color = color;
            }
        }
        for (int i = viewsUsed; i < waypointViews.Count; i++)
        {
            waypointViews[i].Rect.gameObject.SetActive(false);
        }

        // chain[0] is the Cyclops and chain[k + 1] the waypoint k. Every waypoint has the leg that leads to it from the one before (from the Cyclops for the first): grey when flown, bright when it is the leg that the
        // Cyclops is on (the first), dim otherwise; and the line from the Cyclops itself to the next waypoint, bright, when the next one is not the first
        Color flown = new(0.6f, 0.6f, 0.6f, 0.5f);
        Color bright = new(0.4f, 1f, 1f, 0.95f);
        Color dim = new(0.3f, 0.8f, 0.9f, 0.65f);
        int segmentsUsed = 0;
        for (int k = 0; k < points.Count; k++)
        {
            Color color = k < next ? flown : k == 0 ? bright : dim;
            DrawSegment(segmentsUsed++, chain[k == 0 ? 0 : k], chain[k + 1], color);
        }
        if (next > 0 && next < points.Count)
        {
            DrawSegment(segmentsUsed++, chain[0], chain[next + 1], bright);
        }
        for (int i = segmentsUsed; i < segments.Count; i++)
        {
            segments[i].gameObject.SetActive(false);
        }
    }

    /// <summary>The place on the overlay canvas of a point of the screen; false when the point is behind the camera.</summary>
    private bool TryCanvas(Vector3 screen, out Vector2 canvas)
    {
        canvas = Vector2.zero;
        return screen.z > 0.05f && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out canvas);
    }

    private void DrawSegment(int index, Vector2 from, Vector2 to, Color color)
    {
        while (segments.Count <= index)
        {
            GameObject segmentObject = new("Leg", typeof(RectTransform), typeof(Image));
            segmentObject.transform.SetParent(transform, false);
            segmentObject.transform.SetAsFirstSibling();
            RectTransform rect = (RectTransform)segmentObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = segmentObject.GetComponent<Image>();
            image.sprite = NavigatorIcons.Get(NavigatorIcons.Shape.Square);
            image.raycastTarget = false;
            segments.Add(image);
        }
        Image segment = segments[index];
        segment.gameObject.SetActive(true);
        Vector2 delta = to - from;
        RectTransform segmentRect = segment.rectTransform;
        segmentRect.anchoredPosition = (from + to) / 2f;
        segmentRect.sizeDelta = new Vector2(Mathf.Max(1f, delta.magnitude), 4f);
        segmentRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        segment.color = color;
    }

    private WaypointView GetWaypointView(int index)
    {
        while (waypointViews.Count <= index)
        {
            GameObject viewObject = new("Waypoint", typeof(RectTransform), typeof(Image));
            viewObject.transform.SetParent(transform, false);
            RectTransform rect = (RectTransform)viewObject.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(30f, 30f);
            Image image = viewObject.GetComponent<Image>();
            image.sprite = NavigatorIcons.Get(NavigatorIcons.Shape.Ring);
            image.raycastTarget = false;
            Text label = MakeText("Number", 18, TextAnchor.MiddleCenter, Color.white);
            label.transform.SetParent(rect, false);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(30f, 30f);
            waypointViews.Add(new WaypointView { Rect = rect, Image = image, Label = label });
        }
        return waypointViews[index];
    }

    /// <summary>Whether the icon of the contact points the way the contact faces.</summary>
    private static bool Turned(Contact contact)
    {
        return contact.Kind == ContactKind.Cyclops || contact.Kind == ContactKind.OtherCyclops || contact.Kind == ContactKind.Teammate || (contact.Kind == ContactKind.Creature && contact.Danger != CreatureDanger.Leviathan);
    }

    /// <summary>The angle for an icon drawn pointing up so that it points along the contact's forward on the screen.</summary>
    private float HeadingOnScreen(Camera camera, Contact contact, Vector3 iconScreen)
    {
        // the bow of a Cyclops is at the hull's -z (the helm is at z -25), so its transform's forward points to the stern
        Vector3 forward = contact.Kind == ContactKind.Cyclops || contact.Kind == ContactKind.OtherCyclops ? -contact.Source.forward : Vector3.ProjectOnPlane(contact.Source.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f || !console.TryMapPoint(contact.Position + forward * 20f, out Vector3 ahead))
        {
            return 0f;
        }
        Vector3 aheadScreen = camera.WorldToScreenPoint(ahead);
        Vector2 direction = new(aheadScreen.x - iconScreen.x, aheadScreen.y - iconScreen.y);
        return direction.sqrMagnitude < 0.01f ? 0f : Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
    }

    private IconView GetIcon(Contact contact)
    {
        if (icons.TryGetValue(contact.Key, out IconView icon) && icon.Rect)
        {
            icon.Contact = contact;
            return icon;
        }
        GameObject iconObject = new($"Icon {contact.Key}", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(transform, false);
        iconObject.transform.SetAsFirstSibling();
        RectTransform rect = (RectTransform)iconObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        Image image = iconObject.GetComponent<Image>();
        image.sprite = NavigatorIcons.Get(NavigatorIcons.ShapeOf(contact.Kind, contact.Danger));
        image.raycastTarget = false;
        icon = new IconView { Rect = rect, Image = image, Contact = contact };
        icons[contact.Key] = icon;
        return icon;
    }

    private void UpdateHover()
    {
        if (hoverKey != null && icons.TryGetValue(hoverKey, out IconView icon) && icon.Used && icon.Contact.Alive)
        {
            Vector3 from = console.SubRoot.transform.position;
            Vector3 to = icon.Contact.Position;
            float horizontal = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
            hover.text = string.Concat(ContactRules.Describe(icon.Contact.Label, horizontal), "   ", ContactRules.Height(to.y - from.y), icon.Contact.Kind == ContactKind.Mark ? "   (Delete removes it)" : string.Empty);
            hover.rectTransform.anchoredPosition = icon.Canvas + new Vector2(24f, 18f);
            hover.gameObject.SetActive(true);
        }
        else
        {
            hover.gameObject.SetActive(false);
        }
    }

    private void UpdateTexts()
    {
        NavigatorMapView view = console.View;
        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + STATUS_SECONDS;
            bool loaded = console.Hologram != null && console.Hologram.Exists && console.Hologram.ChunkCount > 0;
            status.text = loaded ? string.Empty : "Loading terrain...";
        }
        CyclopsClearance clearance = console.SubRoot ? console.SubRoot.GetComponent<CyclopsClearance>() : null;
        ClearanceReport report = clearance ? clearance.Current : default;
        clearanceLine.text = report.Text ?? string.Empty;
        clearanceLine.color = report.Level == ClearanceLevel.Danger ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.65f, 0.15f);
        rangeLine.text = string.Concat(ContactRules.RangeLine(view.VisibleRadius(console.Range), view.Zoom), console.IncludeMinor ? "   (small creatures shown)" : string.Empty);
    }

    private Text MakeText(string name, int size, TextAnchor alignment, Color color)
    {
        GameObject textObject = new(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        textObject.transform.SetParent(transform, false);
        Text text = textObject.GetComponent<Text>();
        text.font = FindFont();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private static void MakeButton(RectTransform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new(label, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        Anchor(rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), position, size);
        Image background = buttonObject.GetComponent<Image>();
        background.color = new Color(0.05f, 0.15f, 0.2f, 0.85f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        GameObject labelObject = new("Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rect, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        Text text = labelObject.GetComponent<Text>();
        text.font = FindFont();
        text.fontSize = 24;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
    }

    private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static string ExitKeyName()
    {
        try
        {
            return NavigatorConsoleText.PlainKeyName(GameInput.FormatButton(GameInput.Button.Exit), "Esc");
        }
        catch (System.Exception)
        {
            return "Esc";
        }
    }

    private static Font FindFont()
    {
        try
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font)
            {
                return font;
            }
        }
        catch (System.Exception)
        {
            // fall through to the font of the operating system
        }
        return Font.CreateDynamicFontFromOSFont("Arial", 20);
    }
}
