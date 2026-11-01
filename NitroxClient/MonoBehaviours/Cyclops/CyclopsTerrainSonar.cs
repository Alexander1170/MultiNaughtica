using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.Settings;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;
using UnityEngine.Rendering;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// On every real Cyclops: while the Cyclops has the Cyclops Terrain Sonar and the local player is aboard and near, the terrain around the Cyclops floats as a hologram in the middle of the build room, at
/// waist height (<see cref="NavigatorConsoleLayout" />): the game's own terrain hologram (<see cref="TerrainHologram" />) sized by <see cref="TerrainSonarRules" />, a disc that is tilted towards whoever looks at it
/// and keeps the real world's directions (what is away from you in the room is away from you on the map). It is the map of the navigator console as well (<see cref="NavigatorConsole" /> turns, tilts and
/// zooms this same hologram while a navigator works at it), and the marks and the route hang on it for everybody aboard (<see cref="HelmMarkLayer" />). The display at the helm is the game's again: this
/// component no longer puts terrain on it. The disc's size is the player's to choose (<see cref="SetRadius" />, within what the room holds) and is remembered on their machine. It polls from its own
/// <c>Update</c>: the game's <c>SubRoot.Update</c> returns at once while the Cyclops is in the minimal level of detail (see <see cref="CyclopsPowerShare" />).
/// <para>
/// The hologram is a picture of the static terrain and is never synced. It costs while it is shown (the terrain is drawn and the game repositions its chunks every frame, which it has to: the fade of the
/// disc follows the Cyclops), so it is shown only while the player is near it and not at the helm. Everything about the game's own hologram that could not be read without running the game is logged once when
/// it is built and again a few seconds later (<see cref="TerrainHologram.Measure" />).
/// </para>
/// </summary>
public class CyclopsTerrainSonar : MonoBehaviour
{
    private const float POLL_SECONDS = 0.25f;
    private const float RETRY_SECONDS = 30f;
    private const float MEASURE_AFTER_SECONDS = 8f;
    private const float WAIT_FOR_WORLD_SECONDS = 60f;
    private const float TURN_DEGREES_PER_SECOND = 240f;
    private const float SAVE_AFTER_SECONDS = 1f;
    private const string FRAME_NAME = "NitroxBuildRoomMapFrame";
    private const string HOLOGRAM_NAME = "NitroxTerrainHologram";
    private const string RADIUS_PREF = "Nitrox.NavigatorHologramRadius";
    private const string PROJECTOR_SOURCE = "SonarMap_Small";
    private const string PROJECTOR_NAME = "NitroxHologramProjector";

    /// <summary>The y of the top of the floor at the helm, where the game's own little hologram stands on its projector (read from the Cyclops scene); the build room's floor is <see cref="NavigatorConsoleLayout.ROOM_MIN_Y" />.</summary>
    private const float HELM_FLOOR_TOP_Y = -0.81f;

    private SubRoot subRoot;
    private Transform projector;
    private bool projectorTried;
    private TerrainHologram hologram;
    private bool building;
    private float nextPoll;
    private float retryAt;
    private Transform mapFrame;
    private bool near;
    private bool builtClamped;
    private float radius = NavigatorConsoleLayout.DEFAULT_RADIUS;
    private bool radiusLoaded;
    private float radiusSaveAt;
    private float frameYaw;
    private bool frameYawKnown;
    private NavigatorMapView activeView;
    private string lastWaiting = string.Empty;
    private bool gateOpened;

    public SubRoot SubRoot => subRoot;
    public TerrainHologram Hologram => hologram;

    /// <summary>The frame of the map in the build room (a child of the Cyclops): its position, turn and scale are the map's; null until the first poll.</summary>
    public Transform MapFrame => mapFrame;

    /// <summary>The range in metres that the hologram was built for (the setting, and more while the engineer sends the power to the systems); 0 when there is none.</summary>
    public float BuiltRange => hologram != null ? hologram.Range : 0f;

    /// <summary>The radius in metres, in the world, of the disc as the player has sized it (the hologram's own radius is <see cref="TerrainHologram.DiscRadiusWorld" />).</summary>
    public float Radius => radius;

    /// <summary>
    /// The view the map is drawn with (its turn, tilt and zoom): the console's, which keeps where the last navigator left it, whether anybody is working at the map or not. Null only before the console has linked it
    /// (a moment after the Cyclops is in the game): the map then has its defaults. Set by <see cref="NavigatorConsole" />.
    /// </summary>
    public NavigatorMapView ActiveView
    {
        get => activeView;
        set => activeView = value;
    }

#if DEBUG
    private float rangeOverride;
    private bool forceHidden;

    /// <summary>The range the hologram is built for: the server's setting and the engineer's factor (a Debug override of the live check aside).</summary>
    public float WantedRange => rangeOverride > 0f ? rangeOverride : LiveRange;

    private bool ForceHidden => forceHidden;

    private bool ClampToPad => rangeOverride <= 0f;

    /// <summary>Debug (the live check): build the hologram for this range instead of the server's; 0 gives the setting back.</summary>
    public void DebugSetRange(float range)
    {
        rangeOverride = range;
    }

    /// <summary>Debug (the live check): keep the hologram hidden even when it would be shown, to measure the frame time without it.</summary>
    public void DebugSetHidden(bool hidden)
    {
        forceHidden = hidden;
    }
#else
    /// <summary>The range the hologram is built for: the server's setting and the engineer's factor.</summary>
    public float WantedRange => LiveRange;

    private static bool ForceHidden => false;

    private static bool ClampToPad => true;
#endif

    /// <summary>The server's range and farther while the engineer sends the power to the systems, never beyond the biggest range the server allows.</summary>
    private float LiveRange => Mathf.Min(VehicleUpgradeSettingsRules.MAX_TERRAIN_SONAR_RANGE, VehicleUpgradeSettings.TerrainSonarRange * EngineerState.SonarFactor(subRoot));

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
    }

    private void OnDestroy()
    {
        SaveRadiusNow();
        hologram?.Destroy();
        hologram = null;
        if (mapFrame)
        {
            Destroy(mapFrame.gameObject);
        }
        if (projector)
        {
            Destroy(projector.gameObject);
        }
    }

    /// <summary>
    /// Shows or hides the projector on the floor under the map: the disc and the faint cone of light of the game's own little hologram at the helm (<c>SonarMap_Small</c>), copied by their data (mesh, materials, light)
    /// without any of its scripts, standing where the game stands its own: the same place under the map's axis and the same depth in the floor. It has no collider. Built the first time the map is shown; when the
    /// Cyclops has no such object, or building it fails, there is no projector and the map is as it was.
    /// </summary>
    private void SetProjectorShown(bool shown)
    {
        if (shown && !projectorTried)
        {
            projectorTried = true;
            try
            {
                BuildProjector();
            }
            catch (Exception exception)
            {
                Log.Warn($"[VehicleUpgrades] terrain sonar: {subRoot.name}: the projector under the map could not be built ({exception.GetType().Name}: {exception.Message}); the map has none");
            }
        }
        if (projector && projector.gameObject.activeSelf != shown)
        {
            projector.gameObject.SetActive(shown);
        }
    }

    private void BuildProjector()
    {
        Transform source = null;
        foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == PROJECTOR_SOURCE)
            {
                source = candidate;
                break;
            }
        }
        if (!source)
        {
            Log.Warn($"[VehicleUpgrades] terrain sonar: {subRoot.name}: no '{PROJECTOR_SOURCE}' in the Cyclops, so there is no projector under the map");
            return;
        }
        GameObject root = new(PROJECTOR_NAME);
        root.transform.SetParent(transform, false);
        // the game's map stands over its projector; ours has the same axis as the hologram of the build room, and the same distance from the floor
        (float x, float _, float z) = NavigatorConsoleLayout.HologramCenter;
        Vector3 sourceLocal = transform.InverseTransformPoint(source.position);
        root.transform.localPosition = new Vector3(x, sourceLocal.y + (NavigatorConsoleLayout.ROOM_MIN_Y - HELM_FLOOR_TOP_Y), z);
        root.transform.localRotation = Quaternion.Inverse(transform.rotation) * source.rotation;

        List<Renderer> copied = [];
        int discs = CopyPart(source.Find("submarine_hologram_projector"), root.transform, "Disc", copied) ? 1 : 0;
        Transform lightSource = source.Find("VolumetricLight");
        bool beam = false;
        if (lightSource)
        {
            GameObject lightObject = new("Beam") { layer = lightSource.gameObject.layer };
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = lightSource.localPosition;
            lightObject.transform.localRotation = lightSource.localRotation;
            lightObject.transform.localScale = lightSource.localScale;
            Light original = lightSource.GetComponent<Light>();
            if (original)
            {
                Light light = lightObject.AddComponent<Light>();
                light.type = original.type;
                light.color = original.color;
                light.intensity = original.intensity;
                light.range = original.range;
                light.spotAngle = original.spotAngle;
                light.shadows = LightShadows.None;
                beam = true;
            }
            beam |= CopyPart(lightSource.Find("x_FakeVolumletricLight"), lightObject.transform, "Cone", copied);
        }
        if (copied.Count > 0)
        {
            // the light of the water and the sky reaches the copies through the game's sky applier, like it does for the other things that are made here
            SkyApplier applier = root.AddComponent<SkyApplier>();
            applier.anchorSky = Skies.Auto;
            applier.emissiveFromPower = false;
            applier.dynamic = true;
            applier.renderers = copied.ToArray();
        }
        projector = root.transform;
        Log.Info($"[VehicleUpgrades] terrain sonar: {subRoot.name}: the projector under the map is built from '{PROJECTOR_SOURCE}' ({discs} disc, beam {(beam ? "yes" : "no")}, {copied.Count} renderers) at hull-local {root.transform.localPosition.ToString("F2")}");
    }

    /// <summary>A copy of a part of the game's object that is only its picture: the mesh and the materials, at the same place under the new parent. False when there is nothing to copy.</summary>
    private static bool CopyPart(Transform original, Transform parent, string name, List<Renderer> into)
    {
        if (!original)
        {
            return false;
        }
        MeshFilter filter = original.GetComponent<MeshFilter>();
        MeshRenderer renderer = original.GetComponent<MeshRenderer>();
        if (!filter || !renderer || !filter.sharedMesh)
        {
            return false;
        }
        GameObject copy = new(name) { layer = original.gameObject.layer };
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = original.localPosition;
        copy.transform.localRotation = original.localRotation;
        copy.transform.localScale = original.localScale;
        copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        MeshRenderer made = copy.AddComponent<MeshRenderer>();
        made.sharedMaterials = renderer.sharedMaterials;
        made.shadowCastingMode = ShadowCastingMode.Off;
        into.Add(made);
        return true;
    }

    private void Update()
    {
        if (mapFrame && hologram != null && hologram.Exists)
        {
            PlaceFrame();
        }
        if (radiusSaveAt > 0f && Time.unscaledTime >= radiusSaveAt)
        {
            SaveRadiusNow();
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        Refresh();
    }

    /// <summary>
    /// Sets the size of the disc (the radius in metres, kept between the smallest and the biggest the room holds) and remembers it on this machine. The hologram follows in the same frame: the frame it hangs
    /// on is scaled, so the terrain and the icons scale with it, and the fade of the shader is moved to match (<see cref="TerrainHologram.ApplyAnchorScale" />). The terrain is not built again.
    /// </summary>
    public void SetRadius(float wanted)
    {
        float clamped = NavigatorConsoleLayout.ClampRadius(wanted);
        if (Mathf.Approximately(clamped, radius))
        {
            return;
        }
        radius = clamped;
        radiusSaveAt = Time.unscaledTime + SAVE_AFTER_SECONDS;
    }

    /// <summary>Makes the disc bigger (positive) or smaller (negative) by that many notches (<see cref="NavigatorConsoleLayout.RADIUS_NOTCH" /> each).</summary>
    public void StepRadius(float notches)
    {
        SetRadius(NavigatorConsoleLayout.StepRadius(radius, notches));
    }

    private void LoadRadius()
    {
        if (radiusLoaded)
        {
            return;
        }
        radiusLoaded = true;
        try
        {
            radius = NavigatorConsoleLayout.ClampRadius(PrefsStore.GetFloat(RADIUS_PREF, NavigatorConsoleLayout.DEFAULT_RADIUS));
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] terrain sonar: the saved size of the hologram could not be read ({exception.GetType().Name}); the default is used");
        }
        Log.Info($"[VehicleUpgrades] terrain sonar: the hologram's radius is {radius:F2} m (the player's own size, kept between {NavigatorConsoleLayout.MIN_RADIUS:F2} and {NavigatorConsoleLayout.MAX_RADIUS:F2} m)");
    }

    private void SaveRadiusNow()
    {
        if (radiusSaveAt <= 0f)
        {
            return;
        }
        radiusSaveAt = 0f;
        try
        {
            PrefsStore.SetFloat(RADIUS_PREF, radius);
            PrefsStore.Save();
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] terrain sonar: the size of the hologram could not be saved ({exception.GetType().Name})");
        }
    }

    /// <summary>The frame of the map: in the middle of the build room at waist height, scaled to the player's size and tilted towards the viewer (<see cref="PlaceFrame" /> keeps it so).</summary>
    private void EnsureFrame()
    {
        if (mapFrame)
        {
            return;
        }
        GameObject frame = new(FRAME_NAME);
        frame.transform.SetParent(transform, false);
        mapFrame = frame.transform;
        ApplyFramePose();
        Log.Info($"[VehicleUpgrades] terrain sonar: {subRoot.name}: the map's frame is in the build room, hull-local {mapFrame.localPosition:F2}, radius {radius:F2} m");
    }

    private Vector3 FrameCenter()
    {
        (float x, float _, float z) = NavigatorConsoleLayout.HologramCenter;
        Vector3 center = new(x, NavigatorConsoleLayout.CenterY(radius), z);
#if DEBUG
        center += NavigatorConsole.DebugMapNudge;
#endif
        return center;
    }

    /// <summary>
    /// Puts the frame where it belongs with the turn towards the viewer, the tilt and the scale (the position, the turn and the scale of the frame are all that this component sets of it). The tilt is any angle
    /// (<see cref="NavigatorMapView" />: the navigator rolls the map right over and round), which a single turn about the frame's own z axis takes in whole, so there is no angle at which it jumps or stops.
    /// </summary>
    private void ApplyFramePose()
    {
        float tilt = activeView != null ? activeView.Tilt : NavigatorMapView.DEFAULT_TILT;
        mapFrame.localPosition = FrameCenter();
        mapFrame.localRotation = Quaternion.Euler(0f, frameYaw, 0f) * Quaternion.Euler(0f, 0f, -tilt);
        float scale = radius / NavigatorConsoleLayout.DEFAULT_RADIUS;
        if (!Mathf.Approximately(mapFrame.localScale.x, scale))
        {
            mapFrame.localScale = Vector3.one * scale;
        }
    }

    /// <summary>The camera's place in the hull's frame (the local player's eyes), or the player's own place; null when neither exists.</summary>
    private Vector3? ViewerLocal()
    {
        if (MainCamera.camera)
        {
            return transform.InverseTransformPoint(MainCamera.camera.transform.position);
        }
        if (Player.main)
        {
            return transform.InverseTransformPoint(Player.main.transform.position);
        }
        return null;
    }

    /// <summary>
    /// Every frame while the hologram exists: the frame turns about the vertical until its tilt is towards the viewer (a few turns a second at most, so that it follows a player who walks round it without a jump),
    /// and the map takes the navigator's view while one works at it. The turn of the frame is taken out of the map's own turn again, so the map keeps the world's directions: only the tilt follows the viewer.
    /// </summary>
    private void PlaceFrame()
    {
        Vector3? viewer = ViewerLocal();
        Vector3 center = FrameCenter();
        if (viewer.HasValue)
        {
            float target = NavigatorConsoleLayout.FrameYawToward((viewer.Value.x, viewer.Value.y, viewer.Value.z), (center.x, center.y, center.z), frameYaw);
            frameYaw = frameYawKnown ? Mathf.MoveTowardsAngle(frameYaw, target, TURN_DEGREES_PER_SECOND * Time.unscaledDeltaTime) : target;
            frameYawKnown = true;
        }
        ApplyFramePose();
        float yaw = activeView != null ? activeView.Yaw : NavigatorMapView.DEFAULT_YAW;
        float zoom = activeView != null ? activeView.Zoom : 1f;
        TerrainHologramAligner aligner = hologram.Aligner;
        aligner.View = Quaternion.AngleAxis(yaw - frameYaw, Vector3.up);
        if (!Mathf.Approximately(aligner.Zoom, zoom))
        {
            aligner.SetZoom(zoom);
        }
        hologram.ApplyAnchorScale();
    }

    /// <summary>
    /// Whether the hologram may exist yet: the join is finished, the world has settled and no loading screen or intro shows (<see cref="HologramStartRules" />). Until then nothing of it is set up, not even its
    /// frame; the log says what it waits for, and when it starts.
    /// </summary>
    private bool GateIsOpen()
    {
        bool loading = uGUI.main && uGUI.main.loading && uGUI.main.loading.isLoading;
        // (the world has to settle once, before the first start; the terrain keeps loading as the Cyclops travels, and that must not switch the hologram off)
        bool settled = gateOpened || (LargeWorldStreamer.main && LargeWorldStreamer.main.IsReady() && LargeWorldStreamer.main.IsWorldSettled());
        string waiting = HologramStartRules.WaitingFor(Multiplayer.Active, Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted, uGUI.isIntro, loading, settled);
        if (waiting != null)
        {
            if (waiting != lastWaiting)
            {
                lastWaiting = waiting;
                Log.Info($"[VehicleUpgrades] terrain sonar: {subRoot.name}: waiting to start, {waiting}");
            }
            return false;
        }
        if (!gateOpened)
        {
            gateOpened = true;
            Log.Info($"[VehicleUpgrades] terrain sonar: {subRoot.name}: the join is finished and the world has settled; the hologram may stream terrain now");
        }
        lastWaiting = string.Empty;
        return true;
    }

    private void Refresh()
    {
        if (!GateIsOpen())
        {
            hologram?.SetShown(false);
            return;
        }
        LoadRadius();
        EnsureFrame();
        bool aboard = Player.main && Player.main.currentSub == subRoot;
        bool available = TerrainSonar.IsAvailable(subRoot);
        bool navigating = NavigatorConsole.Current && NavigatorConsole.Current.SubRoot == subRoot;

        float distance = Player.main && mapFrame ? Vector3.Distance(Player.main.transform.position, mapFrame.position) : float.NaN;
        near = NavigatorConsoleLayout.ShouldShowMap(distance, near);
        bool piloting = Player.main && Player.main.isPiloting;
        bool show = TerrainSonarRules.ShouldShow(available, aboard) && !ForceHidden && (navigating || (near && !piloting));
        SetProjectorShown(show);
        float range = WantedRange;
        // (the clamp to the pad changes with the live check's explicit range: a hologram built the other way is not the one that is asked for)
        if (hologram != null && (!hologram.Exists || (show && (TerrainSonarRules.NeedsRebuild(hologram.RequestedRange, range) || builtClamped != ClampToPad))))
        {
            hologram.Destroy();
            hologram = null;
        }
        if (!show)
        {
            hologram?.SetShown(false);
            return;
        }
        if (hologram != null)
        {
            hologram.SetShown(true);
        }
        else if (!building && Time.unscaledTime >= retryAt)
        {
            StartCoroutine(Build(range));
        }
    }

    private IEnumerator Build(float range)
    {
        building = true;
        try
        {
            // The game's hologram reads the world streamer in its first frame and its loader coroutine dies for good when the streamer is not there
            float until = Time.unscaledTime + WAIT_FOR_WORLD_SECONDS;
            while ((LargeWorldStreamer.main == null || !LargeWorldStreamer.main.IsReady()) && Time.unscaledTime < until)
            {
                yield return null;
            }
            if (LargeWorldStreamer.main == null || !LargeWorldStreamer.main.IsReady())
            {
                Log.Warn("[VehicleUpgrades] terrain sonar: the world streamer is not ready, no hologram yet");
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }

            TerrainHologramTemplate template = null;
            yield return TerrainHologramTemplate.Request(found => template = found);
            if (template == null || !subRoot || !mapFrame)
            {
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }

            // Built at the default radius for the frame as it is scaled now (the player's size): the hologram reads the anchor's scale when it is built, so what is drawn is the size they chose
            hologram = TerrainHologram.TryCreateOn(subRoot, mapFrame, HOLOGRAM_NAME, NavigatorConsoleLayout.DEFAULT_RADIUS / Mathf.Max(1f, range), range, template);
            if (hologram == null)
            {
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }
            builtClamped = ClampToPad;
        }
        finally
        {
            building = false;
        }
        TerrainHologram built = hologram;
        yield return new WaitForSecondsRealtime(MEASURE_AFTER_SECONDS);
        if (built != null && built.Exists)
        {
            Log.Info($"[VehicleUpgrades] terrain sonar: hologram of {subRoot.name} after {MEASURE_AFTER_SECONDS:F0} s: {built.Measure()}");
        }
    }
}
