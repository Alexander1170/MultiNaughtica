#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of feature group 3, station 1, the Cyclops Terrain Sonar and the navigator console (docs/coop/VEHICLE_UPGRADES.md, a harness script that is not part of this repository). Each logs
///     <c>[SyncHarness] vuhelm: key=value</c> lines. <c>vuhelm [state|scale|dump] [factor]</c> the terrain hologram of the Cyclops the player is in (or the nearest): its state and measurements; <c>scale</c>
///     multiplies the size of the terrain on the display by a factor (the first live run corrects the game's mesh unit with it); <c>dump</c> the helm's components and the position of the display in the hull's
///     frame. <c>vunavdump</c> the cockpit around the helm in the hull's frame (every collider and renderer, and the floor under a grid of points), which names the side desks for the console of the next milestone.
/// </summary>
public partial class HarnessConsoleCommands
{
    private const int NAVDUMP_MAX_LINES = 400;

    private bool vuHelmPerfRunning;
    private bool vuNavDemoRunning;

    /// <summary>The Cyclops the player is in, else the nearest real one that has the terrain sonar component.</summary>
    private static CyclopsTerrainSonar NavCyclops()
    {
        if (Player.main && Player.main.currentSub && VuIsRealCyclops(Player.main.currentSub))
        {
            CyclopsTerrainSonar own = Player.main.currentSub.GetComponent<CyclopsTerrainSonar>();
            if (own)
            {
                return own;
            }
        }
        CyclopsTerrainSonar nearest = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (CyclopsTerrainSonar candidate in FindObjectsOfType<CyclopsTerrainSonar>())
        {
            float distance = Vector3.Distance(from, candidate.transform.position);
            if (distance < best && VuIsRealCyclops(candidate.GetComponent<SubRoot>()))
            {
                best = distance;
                nearest = candidate;
            }
        }
        return nearest;
    }

    private void LiveCommand_vuhelm(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        CyclopsTerrainSonar sonar = NavCyclops();
        if (!sonar)
        {
            Log.Warn($"{VU}vuhelm: no Cyclops with the terrain sonar component");
            return;
        }
        SubRoot cyclops = sonar.SubRoot;
        TerrainHologram hologram = sonar.Hologram;
        if (what == "scale")
        {
            if (hologram == null || !hologram.Exists)
            {
                Log.Warn($"{VU}vuhelm: scale: no hologram is built");
                return;
            }
            hologram.Aligner.SetUserScale(LiveArgFloat(n, 1, 1f));
        }
        else if (what == "fade")
        {
            // vuhelm fade <world units>: sets the shader's fade radius of the helm hologram directly, to find out where the pad's edge is (the build line says what it was set to)
            if (hologram == null || !hologram.Exists)
            {
                Log.Warn($"{VU}vuhelm: fade: no hologram is built");
                return;
            }
            float radius = LiveArgFloat(n, 1, hologram.MiniWorld.fadeRadius);
            hologram.MiniWorld.fadeRadius = radius;
            Material material = hologram.MiniWorld.materialInstance;
            if (material)
            {
                material.SetFloat(Shader.PropertyToID("_FadeRadius"), radius);
            }
            Log.Info($"{VU}vuhelm: fade set to {VuNum(radius)} (material {(material ? "set" : "missing")})");
        }
        else if (what == "perf")
        {
            if (vuHelmPerfRunning)
            {
                Log.Warn($"{VU}vuhelm: perf is already running");
                return;
            }
            StartCoroutine(VuHelmPerf(sonar, LiveArgFloat(n, 1, 200f), Mathf.Clamp(LiveArgFloat(n, 2, 20f), 2f, 120f)));
            return;
        }
        else if (what == "dump")
        {
            Log.Info($"{VU}vuhelm: dump helm={TerrainHologram.DescribeHelm(cyclops)}");
            CyclopsSonarDisplay display = cyclops.GetComponentInChildren<CyclopsSonarDisplay>(true);
            if (display)
            {
                Transform pivot = display.subTransform ? display.subTransform : cyclops.transform;
                Vector3 local = cyclops.transform.InverseTransformPoint(display.transform.position);
                Vector3 turn = (Quaternion.Inverse(cyclops.transform.rotation) * display.transform.rotation).eulerAngles;
                CyclopsHUDSonarPing ping = display.sonarPingObject ? display.sonarPingObject.GetComponent<CyclopsHUDSonarPing>() : null;
                Log.Info(string.Concat($"{VU}vuhelm: display path={display.GetFullHierarchyPath()} activeInHierarchy={display.gameObject.activeInHierarchy} localInHull=({VuNum(local.x)},{VuNum(local.y)},{VuNum(local.z)}) turnInHull=({VuNum(turn.x)},{VuNum(turn.y)},{VuNum(turn.z)}) ",
                         $"localScale={VuNum(display.transform.localScale.x)} lossyScale={VuNum(display.transform.lossyScale.x)} pivotIsHull={pivot == cyclops.transform} radarDisplaySize={(ping ? VuNum(ping.radarDisplaySize) : "none")} radarScanSize={(ping ? VuNum(ping.radarScanSize) : "none")}"));
                foreach (Transform child in display.transform)
                {
                    Log.Info($"{VU}vuhelm: display child {child.name} active={child.gameObject.activeSelf} localPosition=({VuNum(child.localPosition.x)},{VuNum(child.localPosition.y)},{VuNum(child.localPosition.z)}) localScale={VuNum(child.localScale.x)}");
                }
                // the pad: every renderer of the display's own object (not our hologram's) with its size in the display's frame, which says how far the terrain may reach
                foreach (Renderer renderer in display.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.GetComponentInParent<MiniWorld>())
                    {
                        continue;
                    }
                    Bounds bounds = renderer.bounds;
                    Vector3 centre = display.transform.InverseTransformPoint(bounds.center);
                    Log.Info($"{VU}vuhelm: display renderer {renderer.gameObject.GetFullHierarchyPath()} enabled={renderer.enabled} activeInHierarchy={renderer.gameObject.activeInHierarchy} worldSize=({VuNum(bounds.size.x)},{VuNum(bounds.size.y)},{VuNum(bounds.size.z)}) centreInDisplay=({VuNum(centre.x)},{VuNum(centre.y)},{VuNum(centre.z)})");
                }
            }
            CyclopsHolographicHUD hull = cyclops.GetComponentInChildren<CyclopsHolographicHUD>(true);
            if (hull)
            {
                Vector3 local = cyclops.transform.InverseTransformPoint(hull.transform.position);
                Log.Info($"{VU}vuhelm: hullHologram path={hull.GetFullHierarchyPath()} localInHull=({VuNum(local.x)},{VuNum(local.y)},{VuNum(local.z)}) uiScale={VuNum(hull.uiScale)}");
            }
        }
        float range = VehicleUpgradeSettings.TerrainSonarRange;
        Log.Info(string.Concat($"{VU}vuhelm: cyclops={VuId(cyclops.gameObject)} module={TerrainSonar.HasModule(cyclops)} available={TerrainSonar.IsAvailable(cyclops)} aboard={Player.main && Player.main.currentSub == cyclops} range={VuNum(range)} ",
                 $"built={hologram != null && hologram.Exists} {(hologram != null && hologram.Exists ? hologram.Measure() : "no hologram")}"));
    }

    /// <summary>
    ///     <c>vuhelm perf [range] [seconds]</c>: the frame time with the terrain hologram ON and OFF at one range (both clients, 200 m and 500 m, the Cyclops moving). It builds the
    ///     hologram for the range, waits until the game has loaded its chunks, <b>waits until the Cyclops goes faster than 4 m/s (at most 40 s; the live script holds the forward key at the helm meanwhile: setting
    ///     the body's velocity from a command does not move a Cyclops)</b>, samples <paramref name="seconds" /> of frames, hides the hologram (which frees the chunks), samples the same again, and puts everything back.
    ///     The result is two lines <c>vuhelm: perf range=… hologram=ON|OFF …</c> and a <c>perf delta</c> line.
    /// </summary>
    private IEnumerator VuHelmPerf(CyclopsTerrainSonar sonar, float range, float seconds)
    {
        vuHelmPerfRunning = true;
        try
        {
            sonar.DebugSetRange(range);
            sonar.DebugSetHidden(false);
            float giveUp = Time.unscaledTime + 60f;
            while (Time.unscaledTime < giveUp && !(sonar.Hologram != null && sonar.Hologram.Shown && !TerrainSonarRules.NeedsRebuild(sonar.Hologram.Range, range)))
            {
                yield return null;
            }
            if (sonar.Hologram == null || !sonar.Hologram.Shown)
            {
                Log.Warn($"{VU}vuhelm: perf range={VuNum(range)} FAILED no hologram is shown (is the player aboard a Cyclops with the Cyclops Terrain Sonar, and are the vehicle upgrades on?)");
                yield break;
            }

            // The game's hologram loads its chunks over some seconds: wait until the number of chunks has stopped changing
            float loadStart = Time.unscaledTime;
            int lastChunks = -1;
            int steady = 0;
            while (steady < 3 && Time.unscaledTime < loadStart + 40f)
            {
                yield return new WaitForSecondsRealtime(1f);
                int chunks = sonar.Hologram.ChunkCount;
                steady = chunks > 0 && chunks == lastChunks ? steady + 1 : 0;
                lastChunks = chunks;
            }
            float loadSeconds = Time.unscaledTime - loadStart;
            int loadedChunks = sonar.Hologram.ChunkCount;

            // the window starts once the Cyclops moves (somebody holds the forward key); after 40 s it is measured as it is, and the moved distance in the lines says so
            float moveStart = Time.unscaledTime;
            float moveClock = moveStart;
            Vector3 moveFrom = sonar.transform.position;
            float moveSpeed = 0f;
            while (moveSpeed < 4f && Time.unscaledTime < moveStart + 40f)
            {
                yield return new WaitForSecondsRealtime(1f);
                Vector3 moveTo = sonar.transform.position;
                moveSpeed = Vector3.Distance(moveFrom, moveTo) / Mathf.Max(0.01f, Time.unscaledTime - moveClock);
                moveFrom = moveTo;
                moveClock = Time.unscaledTime;
            }
            float moveWaited = Time.unscaledTime - moveStart;

            List<float> on = new();
            Vector3 onStart = sonar.transform.position;
            float onClock = Time.unscaledTime;
            while (Time.unscaledTime < onClock + seconds)
            {
                on.Add(Time.unscaledDeltaTime);
                yield return null;
            }
            float onMoved = Vector3.Distance(onStart, sonar.transform.position);
            int chunksAfter = sonar.Hologram != null ? sonar.Hologram.ChunkCount : 0;

            sonar.DebugSetHidden(true);
            yield return new WaitForSecondsRealtime(4f);
            List<float> off = new();
            Vector3 offStart = sonar.transform.position;
            float offClock = Time.unscaledTime;
            while (Time.unscaledTime < offClock + seconds)
            {
                off.Add(Time.unscaledDeltaTime);
                yield return null;
            }
            float offMoved = Vector3.Distance(offStart, sonar.transform.position);

            FrameTimeSummary onSummary = FrameTimeStats.Summarize(on);
            FrameTimeSummary offSummary = FrameTimeStats.Summarize(off);
            Log.Info(string.Concat($"{VU}vuhelm: perf range={VuNum(range)} hologram=ON seconds={VuNum(seconds)} {onSummary} moved={VuNum(onMoved)}m speed={VuNum(onMoved / seconds)}m/s chunks={loadedChunks}->{chunksAfter} loadSeconds={VuNum(loadSeconds)} moveWaited={VuNum(moveWaited)} ",
                     $"cyclops={VuId(sonar.gameObject)}"));
            Log.Info($"{VU}vuhelm: perf range={VuNum(range)} hologram=OFF seconds={VuNum(seconds)} {offSummary} moved={VuNum(offMoved)}m speed={VuNum(offMoved / seconds)}m/s");
            Log.Info(string.Concat($"{VU}vuhelm: perf range={VuNum(range)} delta avg={(onSummary.AverageMs - offSummary.AverageMs).ToString("F2", CultureInfo.InvariantCulture)}ms p95={(onSummary.P95Ms - offSummary.P95Ms).ToString("F2", CultureInfo.InvariantCulture)}ms ",
                     $"max={(onSummary.MaxMs - offSummary.MaxMs).ToString("F2", CultureInfo.InvariantCulture)}ms fps={(onSummary.Fps - offSummary.Fps).ToString("F1", CultureInfo.InvariantCulture)} (ON minus OFF, a negative fps delta is a slower game)"));
        }
        finally
        {
            sonar.DebugSetHidden(false);
            sonar.DebugSetRange(0f);
            vuHelmPerfRunning = false;
        }
    }

    /// <summary>
    ///     <c>vunav [state|open|close|demo|ping [presses]|mark dx,dz|markkind k|markdel|markclear|wp dx,dz|wpdel|wpclear|hand dx,dy,dz|map dx,dy,dz|view yaw,tilt,zoom|ray|minor|stand x,z|look x,z|look x,y,z]</c>: the navigator console of the Cyclops the player is in. <c>open</c> and <c>close</c> use it as the hand target does (with
    ///     the same conditions); <c>hand</c> and <c>map</c> move the hand target and the middle of the map in the hull's frame (to fit them to the desk, the numbers are logged); <c>view</c> sets the view of the map;
    ///     <c>ray</c> lists what a ray from the camera hits within 3 m, nearest first (which collider the hand reaches first); <c>stand</c> puts the player into the Cyclops at a place of the hull's frame and <c>look</c> turns them to face one (the live check's way to stand at the desk); <c>minor</c> toggles the small creatures. Always ends with a state line.
    /// </summary>
    private void LiveCommand_vunav(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        CyclopsTerrainSonar sonar = NavCyclops();
        NavigatorConsole console = sonar ? sonar.GetComponent<NavigatorConsole>() : null;
        if (!console)
        {
            Log.Warn($"{VU}vunav: no Cyclops with the navigator console component");
            return;
        }
        switch (what)
        {
            case "open":
                console.Enter();
                break;
            case "close":
                console.Close();
                break;
            case "hand":
                NavigatorConsole.DebugHandNudge = VuNavVector(n, 0f, 0f, 0f);
                break;
            case "map":
                NavigatorConsole.DebugMapNudge = VuNavVector(n, 0f, 0f, 0f);
                break;
            case "view":
                {
                    Vector3 view = VuNavVector(n, console.View.Yaw, console.View.Tilt, console.View.Zoom);
                    console.View.Set(view.x, view.y, view.z);
                }
                break;
            case "minor":
                console.IncludeMinor = !console.IncludeMinor;
                break;
            case "ray":
                VuNavRay();
                break;
            case "stand":
                {
                    Vector3 spot = VuNavVector(n, 1.5f, 0f, -20.3f);
                    VuNavStand(console.SubRoot, spot.x, spot.z);
                }
                break;
            case "look":
                {
                    // look x,z (level) or look x,y,z (the hull-local point to face, height included)
                    Vector3 spot = VuNavVector(n, 3f, float.NaN, -20.3f);
                    StartCoroutine(VuNavLookAsync(console.SubRoot, spot.x, spot.y, spot.z));
                }
                break;
            case "mark":
                {
                    // vunav mark dx,dz (or dx,dy,dz): a mark of the current kind at that place in the hull's frame, placed as a click on the map does
                    Vector3 spot = VuNavVector(n, 0f, 0f, 0f);
                    NavMark placed = console.PlaceMark(console.SubRoot.transform.TransformPoint(spot), console.MarkKind);
                    Log.Info($"{VU}vunav: mark placed={(placed != null ? placed.Label : "refused")} at hull-local ({VuNum(spot.x)},{VuNum(spot.y)},{VuNum(spot.z)})");
                }
                break;
            case "markkind":
                console.MarkKind = (byte)Mathf.Clamp((int)LiveArgFloat(n, 1, 0f), 0, CyclopsNavRules.MARK_KIND_COUNT - 1);
                break;
            case "markdel":
                {
                    // the newest mark of this Cyclops
                    if (console.TryGetCyclopsId(out NitroxId deleteFrom) && NavMarks.Count(deleteFrom) > 0)
                    {
                        IReadOnlyList<NavMark> list = NavMarks.MarksOf(deleteFrom);
                        console.RemoveMark(list[list.Count - 1].Id);
                    }
                }
                break;
            case "markclear":
                console.ClearMarks();
                break;
            case "wp":
                {
                    // vunav wp dx,dz (or dx,dy,dz): a route point at that place in the hull's frame (as a right click on the map adds it)
                    Vector3 spot = VuNavVector(n, 0f, 0f, 0f);
                    NavWaypoint added = VuAddWaypoint(console, console.SubRoot.transform.TransformPoint(spot));
                    Log.Info($"{VU}vunav: wp added={(added != null ? "yes" : "refused")} at hull-local ({VuNum(spot.x)},{VuNum(spot.y)},{VuNum(spot.z)})");
                }
                break;
            case "ping":
                {
                    // the key P of the console: asks for the sonar ping (vunav ping 2 presses twice in the same moment, which the cooldown turns down); what the player would read is logged
                    int presses = Mathf.Clamp((int)LiveArgFloat(n, 1, 1f), 1, 3);
                    for (int press = 0; press < presses; press++)
                    {
                        NavigatorSonar.Request(console.SubRoot, text => Log.Info($"{VU}vunav: ping message={text.Replace(' ', '_')}"));
                    }
                }
                break;
            case "wpw":
                {
                    // vunav wpw dx,dz: a route point in the HORIZONTAL plane at the hull's own height: dx to port, dz toward the stern (-90 is ahead), measured along the hull's heading with its pitch and roll taken out. A
                    // Cyclops that lies nose-up on a slope would get a point in the air from "vunav wp", which no Cyclops can reach (the first live run put the first point 25 m above the water)
                    Vector3 spot = VuNavVector(n, 0f, 0f, 0f);
                    Transform hullTransform = console.SubRoot.transform;
                    Vector3 port = Vector3.ProjectOnPlane(hullTransform.right, Vector3.up).normalized;
                    Vector3 aft = Vector3.ProjectOnPlane(hullTransform.forward, Vector3.up).normalized;
                    NavWaypoint added = VuAddWaypoint(console, hullTransform.position + port * spot.x + aft * spot.z);
                    Log.Info($"{VU}vunav: wp added={(added != null ? "yes" : "refused")} at horizontal ({VuNum(spot.x)},{VuNum(spot.z)}) from the hull");
                }
                break;
            case "wpdel":
                console.RemoveLastWaypoint();
                break;
            case "wpclear":
                console.ClearRoute();
                break;
            case "demo":
                if (vuNavDemoRunning)
                {
                    Log.Warn($"{VU}vunav: demo is already running");
                }
                else
                {
                    StartCoroutine(VuNavDemo(console));
                }
                return;
        }

        Transform hand = console.transform.Find("NitroxNavigatorConsoleHandTarget");
        Vector3 handLocal = hand ? console.transform.InverseTransformPoint(hand.position) : Vector3.zero;
        Vector3 mapLocal = console.MapFrame ? console.transform.InverseTransformPoint(console.MapFrame.position) : Vector3.zero;
        System.Collections.Generic.Dictionary<NitroxClient.GameLogic.Navigator.ContactKind, int> kinds = new();
        foreach (NitroxClient.GameLogic.Navigator.Contact contact in console.Contacts)
        {
            kinds[contact.Kind] = kinds.TryGetValue(contact.Kind, out int count) ? count + 1 : 1;
        }
        string counts = string.Join(",", System.Linq.Enumerable.Select(kinds, pair => $"{pair.Key}={pair.Value}"));
        string marksText = string.Concat(VuNavMarks(console), " ", VuNavRoute(console));
        Log.Info(string.Concat($"{VU}vunav: cyclops={VuId(console.gameObject)} open={console.IsOpen} canEnter={console.CanEnter(out string why)} why=\"{why}\" handTarget={(hand ? "yes" : "no")} handLocal=({VuNum(handLocal.x)},{VuNum(handLocal.y)},{VuNum(handLocal.z)}) ",
                 $"mapLocal=({VuNum(mapLocal.x)},{VuNum(mapLocal.y)},{VuNum(mapLocal.z)}) yaw={VuNum(console.View.Yaw)} tilt={VuNum(console.View.Tilt)} zoom={VuNum(console.View.Zoom)} minor={console.IncludeMinor} contacts=[{counts}] ",
                 $"registeredCreatures={NitroxClient.GameLogic.Navigator.NavigatorContacts.RegisteredCreatures} registeredBases={NitroxClient.GameLogic.Navigator.NavigatorContacts.RegisteredBases} {marksText} ",
                 $"hologram={(console.Hologram != null && console.Hologram.Exists ? console.Hologram.Measure() : "none")}"));
    }

    /// <summary>
    ///     The second word of a vunav command as a vector: three numbers joined by commas ("0,0.1,0"), or two ("1.5,-20.3": x and z, y keeps its default) because the server's /console passes only two words after the
    ///     command. Anything that is missing or not a number keeps the default.
    /// </summary>
    private static Vector3 VuNavVector(NotificationCenter.Notification n, float defaultX, float defaultY, float defaultZ)
    {
        Vector3 result = new(defaultX, defaultY, defaultZ);
        string text = LiveArgString(n, 1, string.Empty);
        if (text.Length == 0)
        {
            return result;
        }
        string[] parts = text.Split(',');
        float[] values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return result;
            }
        }
        if (values.Length >= 3)
        {
            return new Vector3(values[0], values[1], values[2]);
        }
        if (values.Length == 2)
        {
            return new Vector3(values[0], defaultY, values[1]);
        }
        return result;
    }

    /// <summary>vunav stand x,z: puts the local player into the Cyclops at that place of the hull's frame (as the warp into a Cyclops does it: the player drops to the deck).</summary>
    private static void VuNavStand(SubRoot cyclops, float x, float z)
    {
        Player player = Player.main;
        if (!player || !cyclops)
        {
            Log.Warn($"{VU}vunav: stand: no player or Cyclops");
            return;
        }
        Vector3 world = cyclops.transform.TransformPoint(new Vector3(x, 0.9f, z));
        player.OnPlayerPositionCheat();
        player.SetPosition(world);
        player.SetCurrentSub(cyclops, true);
        Log.Info($"{VU}vunav: stand: to hull-local ({VuNum(x)},0.90,{VuNum(z)}) world {world.ToString("F2")}");
    }

    /// <summary>
    ///     vunav look x,z or x,y,z: turns the local player to face the hull-local point (x, z), and, when a height y is given, tilts the view up or down to face that height too. The game's walking camera keeps its own
    ///     yaw and pitch, and the names are the other way round from what they say: <c>MainCameraControl.rotationX</c> is the yaw (the local Y angle of the player in the frame of the Cyclops it stands in) and
    ///     <c>rotationY</c> the pitch, positive up (the camera's X angle is its negative), clamped to <c>minimumY</c>..<c>maximumY</c>; the game's <c>LookAt</c> zeroes both again at the next frame, which is why the
    ///     first version of this command did not hold. The gaze is logged two frames later, in the hull's frame.
    /// </summary>
    private IEnumerator VuNavLookAsync(SubRoot cyclops, float x, float y, float z)
    {
        Player player = Player.main;
        if (!player || !cyclops || !MainCamera.camera || !MainCameraControl.main)
        {
            Log.Warn($"{VU}vunav: look: no player, Cyclops or camera");
            yield break;
        }
        Vector3 root = cyclops.transform.InverseTransformPoint(player.transform.position);
        Vector3 eyeNow = cyclops.transform.InverseTransformPoint(MainCamera.camera.transform.position);
        float yaw = Mathf.Atan2(x - root.x, z - root.z) * Mathf.Rad2Deg;
        float horizontal = Mathf.Sqrt((x - eyeNow.x) * (x - eyeNow.x) + (z - eyeNow.z) * (z - eyeNow.z));
        float pitch = float.IsNaN(y) ? 0f : Mathf.Atan2(y - eyeNow.y, Mathf.Max(0.1f, horizontal)) * Mathf.Rad2Deg;
        MainCameraControl.main.rotationX = yaw;
        MainCameraControl.main.rotationY = pitch;
        yield return null;
        yield return null;
        Transform camera = MainCamera.camera.transform;
        Vector3 eye = cyclops.transform.InverseTransformPoint(camera.position);
        Vector3 gaze = cyclops.transform.InverseTransformDirection(camera.forward);
        Log.Info($"{VU}vunav: look: yaw={VuNum(yaw)} pitch={VuNum(pitch)} root=({VuNum(root.x)},{VuNum(root.y)},{VuNum(root.z)}) eye=({VuNum(eye.x)},{VuNum(eye.y)},{VuNum(eye.z)}) gaze=({VuNum(gaze.x)},{VuNum(gaze.y)},{VuNum(gaze.z)})");
    }

    /// <summary>
    ///     vunav demo: the console's own live check, which no chat command can do (typing into the chat takes the input group from the console, and the keys of a typed word reach the game while it is open): opens the
    ///     console as the hand does, waits for the terrain, takes pictures of the whole window (the overlay is on them) at the default view, zoomed and turned and upright, and closes it. Logs <c>vunav: demo ...</c>
    ///     lines and ends with <c>vunav: demo done</c>; the pictures are the liveshot files <c>shot-&lt;player&gt;-demo-default|demo-zoom|demo-upright</c>.
    /// </summary>
    private IEnumerator VuNavDemo(NavigatorConsole console)
    {
        vuNavDemoRunning = true;
        try
        {
            // (the console keeps the view where the last navigator left it; the first picture is the default one)
            console.View.Reset();
            console.Enter();
            float until = Time.unscaledTime + 10f;
            while (NavigatorConsole.Current != console && Time.unscaledTime < until)
            {
                yield return null;
            }
            if (NavigatorConsole.Current != console)
            {
                Log.Info($"{VU}vunav: demo FAILED the console did not open (canEnter={console.CanEnter(out string why)} why=\"{why}\")");
                yield break;
            }
            Log.Info($"{VU}vunav: demo opened");

            // the game's hologram loads its chunks over some seconds
            float loadStart = Time.unscaledTime;
            int lastChunks = -1;
            int steady = 0;
            while (steady < 3 && Time.unscaledTime < loadStart + 40f && NavigatorConsole.Current == console)
            {
                yield return new WaitForSecondsRealtime(1f);
                int chunks = console.Hologram != null && console.Hologram.Exists ? console.Hologram.ChunkCount : 0;
                steady = chunks > 0 && chunks == lastChunks ? steady + 1 : 0;
                lastChunks = chunks;
            }
            Log.Info($"{VU}vunav: demo terrain chunks={Math.Max(0, lastChunks)} after {VuNum(Time.unscaledTime - loadStart)} s open={NavigatorConsole.Current == console} contacts={console.Contacts.Count}");

            yield return VuNavDemoShot(console, "demo-default");
            // marks as clicks on the map place them: ahead (the bow is the hull's -z), to starboard and a hazard, and one beyond the range
            Transform hull = console.SubRoot.transform;
            console.MarkKind = CyclopsNavRules.MARK_POINT;
            console.PlaceMark(hull.TransformPoint(new Vector3(0f, 0f, -60f)), CyclopsNavRules.MARK_POINT);
            console.PlaceMark(hull.TransformPoint(new Vector3(-80f, 0f, 20f)), CyclopsNavRules.MARK_HAZARD);
            console.PlaceMark(hull.TransformPoint(new Vector3(40f, 0f, 90f)), CyclopsNavRules.MARK_TARGET);
            console.PlaceMark(hull.TransformPoint(new Vector3(0f, 0f, -350f)), CyclopsNavRules.MARK_POINT);
            // a route of three points: ahead, ahead and to starboard, and further on
            VuAddWaypoint(console, hull.TransformPoint(new Vector3(0f, 0f, -70f)));
            VuAddWaypoint(console, hull.TransformPoint(new Vector3(-60f, 0f, -110f)));
            VuAddWaypoint(console, hull.TransformPoint(new Vector3(-120f, 0f, -60f)));
            yield return new WaitForSecondsRealtime(2f);
            Log.Info($"{VU}vunav: demo marks placed {VuNavMarks(console)} {VuNavRoute(console)}");
            yield return VuNavDemoShot(console, "demo-marks");
            console.View.Set(-40f, 40f, 3f);
            yield return new WaitForSecondsRealtime(2f);
            yield return VuNavDemoShot(console, "demo-zoom");
            console.View.Set(-90f, 80f, 1f);
            yield return new WaitForSecondsRealtime(2f);
            yield return VuNavDemoShot(console, "demo-upright");
            console.View.Reset();
            yield return new WaitForSecondsRealtime(1f);
            Log.Info($"{VU}vunav: demo state open={NavigatorConsole.Current == console} contacts={console.Contacts.Count} hologram={(console.Hologram != null && console.Hologram.Exists ? console.Hologram.Measure() : "none")}");
        }
        finally
        {
            console.Close();
            vuNavDemoRunning = false;
            Log.Info($"{VU}vunav: demo done");
        }
    }

    private IEnumerator VuNavDemoShot(NavigatorConsole console, string tag)
    {
        if (NavigatorConsole.Current != console)
        {
            Log.Info($"{VU}vunav: demo shot {tag} skipped: the console closed");
            yield break;
        }
        yield return StartCoroutine(LiveShotRoutine(false, 0f, tag));
    }

    /// <summary>
    ///     <c>marks=2 markList=[3f9a1c2b:Alice:Mark_1/0/45.2/ping/hud,...]</c>: the marks that this client knows for the Cyclops: id, owner, label, kind, distance from the Cyclops, whether the HUD ping is registered with the
    ///     game's list (<c>PingManager.Get</c>) and shown (visible), or missing.
    /// </summary>
    private static string VuNavMarks(NavigatorConsole console)
    {
        if (!console.TryGetCyclopsId(out NitroxId id))
        {
            return "marks=0 markList=[]";
        }
        List<string> items = new();
        foreach (NavMark mark in NavMarks.MarksOf(id))
        {
            PingInstance ping = mark.PingObject ? PingManager.Get($"nitrox_navmark_{mark.Id}") : null;
            string hud = !mark.PingObject ? "noobject" : ping ? (ping.visible ? "hud" : "hidden") : "unregistered";
            float distance = Vector3.Distance(console.SubRoot.transform.position, mark.Position);
            items.Add($"{mark.Id.ToString().Substring(0, 8)}:{mark.OwnerName}:{mark.Label.Replace(' ', '_')}/{mark.Kind}/{VuNum(distance)}/{hud}");
        }
        return $"marks={items.Count} markList=[{string.Join(",", items)}]";
    }

    /// <summary>A route point that the helm goes to: the harness means the place it names, so the point is stored as an Alt+click stores it (the helm aims a clearance above a stored point, see RouteClearance).</summary>
    private static NavWaypoint VuAddWaypoint(NavigatorConsole console, Vector3 aim)
    {
        return console.AddWaypoint(new Vector3(aim.x, RouteClearance.PinYFor(aim.y), aim.z));
    }

    /// <summary>
    ///     <c>route=3 next=1 guide=[active=True index=1 hull=45.2 yaw=12.0 pitch=-3.0 off=False cross=NaN speed=5.8 eta=7.8] axisInHull=(0.00,0.00,-1.00)</c>: the route as this client knows it, what its guidance says now, and which way
    ///     the Cyclops' own axis (<c>subAxis</c>, the frame of the guidance) points in the hull's frame.
    /// </summary>
    private static string VuNavRoute(NavigatorConsole console)
    {
        SubRoot cyclops = console.SubRoot;
        string axis = "none";
        if (cyclops && cyclops.subAxis)
        {
            Vector3 forward = cyclops.transform.InverseTransformDirection(cyclops.subAxis.forward);
            axis = $"({VuNum(forward.x)},{VuNum(forward.y)},{VuNum(forward.z)})";
        }
        int count = console.RoutePoints.Count;
        RouteGuide guide = cyclops ? cyclops.GetComponent<RouteGuide>() : null;
        string guidance = "none";
        if (guide)
        {
            RouteGuidance g = guide.Current;
            guidance = $"[active={g.Active} finished={g.Finished} index={g.Index} count={g.Count} hull={VuNum(g.HullDistance)} yaw={VuNum(g.Yaw)} pitch={VuNum(g.Pitch)} off={g.OffCourse} cross={VuNum(g.CrossTrack)} speed={VuNum(g.Speed)} eta={VuNum(g.Eta)}]";
        }
        CyclopsClearance clearance = cyclops ? cyclops.GetComponent<CyclopsClearance>() : null;
        string clear = clearance ? $"[side:{clearance.Current.Side},gap:{VuNum(clearance.Current.Gap)},level:{clearance.Current.Level}]" : "none";
        return $"route={count} next={console.RouteNext} guide={guidance} axisInHull={axis} clearance={clear} sonarModule={TerrainSonar.HasVanillaSonar(cyclops)}";
    }

    private static void VuNavRay()
    {
        if (!MainCamera.camera)
        {
            Log.Warn($"{VU}vunav: ray: no camera");
            return;
        }
        Transform camera = MainCamera.camera.transform;
        RaycastHit[] hits = Physics.RaycastAll(camera.position, camera.forward, 3f, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Log.Info($"{VU}vunav: ray from {camera.position.ToString("F2")} along {camera.forward.ToString("F2")}: {hits.Length} hit(s)");
        foreach (RaycastHit hit in hits)
        {
            // the game's targeting ignores the player's own colliders (the capsule at the camera comes first otherwise)
            if (Player.main && hit.collider.transform.IsChildOf(Player.main.transform))
            {
                continue;
            }
            Component handTarget = hit.collider.GetComponentInParent<IHandTarget>() as Component;
            Log.Info($"{VU}vunav: ray hit {hit.collider.gameObject.GetFullHierarchyPath()} layer={hit.collider.gameObject.layer} trigger={hit.collider.isTrigger} distance={VuNum(hit.distance)} handTarget={(handTarget ? handTarget.GetType().Name : "none")}");
        }
    }

    private void LiveCommand_vunavdump(NotificationCenter.Notification n)
    {
        CyclopsTerrainSonar sonar = NavCyclops();
        if (!sonar)
        {
            Log.Warn($"{VU}vunavdump: no Cyclops");
            return;
        }
        Transform hull = sonar.transform;
        int lines = 0;
        PilotingChair chair = sonar.GetComponentInChildren<PilotingChair>(true);
        if (chair)
        {
            Vector3 seat = chair.sittingPosition ? hull.InverseTransformPoint(chair.sittingPosition.position) : Vector3.zero;
            Vector3 chairLocal = hull.InverseTransformPoint(chair.transform.position);
            Log.Info($"{VU}vunavdump: chair path={chair.GetFullHierarchyPath()} localInHull=({VuNum(chairLocal.x)},{VuNum(chairLocal.y)},{VuNum(chairLocal.z)}) seatInHull=({VuNum(seat.x)},{VuNum(seat.y)},{VuNum(seat.z)})");
        }
        // everything near the helm: a box in the hull's frame (x across, y up, z from the bow back; the helm is at about z=-24)
        Vector3 min = new(-6f, -3f, -29f);
        Vector3 max = new(6f, 4f, -16f);
        List<(string Kind, string Path, Vector3 Local, Vector3 Size, string Detail)> found = [];
        foreach (Collider collider in sonar.GetComponentsInChildren<Collider>(true))
        {
            Vector3 local = hull.InverseTransformPoint(collider.bounds.center);
            if (Inside(local, min, max))
            {
                found.Add(("collider", collider.gameObject.GetFullHierarchyPath(), local, collider.bounds.size, $"{collider.GetType().Name} trigger={collider.isTrigger} layer={collider.gameObject.layer} enabled={collider.enabled}"));
            }
        }
        foreach (Renderer renderer in sonar.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 local = hull.InverseTransformPoint(renderer.bounds.center);
            if (Inside(local, min, max))
            {
                found.Add(("renderer", renderer.gameObject.GetFullHierarchyPath(), local, renderer.bounds.size, $"{renderer.GetType().Name} enabled={renderer.enabled} active={renderer.gameObject.activeInHierarchy}"));
            }
        }
        found.Sort((a, b) => a.Local.x.CompareTo(b.Local.x));
        foreach ((string kind, string path, Vector3 local, Vector3 size, string detail) in found)
        {
            if (lines++ >= NAVDUMP_MAX_LINES)
            {
                break;
            }
            Log.Info($"{VU}vunavdump: {kind} path={path} localInHull=({VuNum(local.x)},{VuNum(local.y)},{VuNum(local.z)}) size=({VuNum(size.x)},{VuNum(size.y)},{VuNum(size.z)}) {detail}");
        }
        // the floor under a grid of points: the name of what a ray down the hull's axis hits first, and how far below the start (names the desks and their height)
        for (float z = -27f; z <= -21f; z += 0.5f)
        {
            for (float x = -4f; x <= 4f; x += 0.5f)
            {
                Vector3 start = hull.TransformPoint(new Vector3(x, 3f, z));
                if (Physics.Raycast(start, -hull.up, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (lines++ >= NAVDUMP_MAX_LINES)
                    {
                        break;
                    }
                    Log.Info($"{VU}vunavdump: down x={VuNum(x)} z={VuNum(z)} hit={hit.collider.gameObject.GetFullHierarchyPath()} below={VuNum(hit.distance)} layer={hit.collider.gameObject.layer}");
                }
            }
        }
        Log.Info($"{VU}vunavdump: done lines={Math.Min(lines, NAVDUMP_MAX_LINES)} cyclops={VuId(sonar.gameObject)} box=({VuNum(min.x)},{VuNum(min.y)},{VuNum(min.z)})..({VuNum(max.x)},{VuNum(max.y)},{VuNum(max.z)})");
    }

    private static bool Inside(Vector3 point, Vector3 min, Vector3 max)
    {
        return point.x >= min.x && point.x <= max.x && point.y >= min.y && point.y <= max.y && point.z >= min.z && point.z <= max.z;
    }
}
#endif
