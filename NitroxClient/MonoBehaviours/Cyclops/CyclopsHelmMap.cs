using System.Collections;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.Settings;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The helm's own copy of the navigator's map: with the Cyclops Terrain Sonar, a second terrain hologram hangs in front of the windshield where the pilot's crosshair is when looking ahead from the wheel
/// (<see cref="HelmLayout" />), about 15 percent smaller than the helm display's was, for the pilot who likes to see the map while driving alone. It is the same kind of hologram as the build room's
/// (<see cref="TerrainHologram" />), built for at most 130 m, tilted towards the seat with the bow at its far edge, and it shows what the navigator's map shows: the marks and the route on it
/// (<see cref="HelmMarkLayer" />) and the contacts (<see cref="HelmContactLayer" />). It is shown while the player is aboard and near it, and <b>the pilot's own button in the helm HUD</b>
/// (<see cref="CyclopsHelmHud" />) switches it off and on for them alone; the choice is kept on their machine. Like the build room's, nothing of it is set up until the join is finished and the world has
/// settled (<see cref="HologramStartRules" />). It does not follow the navigator's zoom and turn: those are the navigator's own view on their own machine, and another player's helm would need them sent.
/// </summary>
public class CyclopsHelmMap : MonoBehaviour
{
    private const float POLL_SECONDS = 0.25f;
    private const float RETRY_SECONDS = 30f;
    private const float WAIT_FOR_WORLD_SECONDS = 60f;
    private const string FRAME_NAME = "NitroxHelmMapFrame";
    private const string HOLOGRAM_NAME = "NitroxHelmHologram";
    private const string ON_PREF = "Nitrox.HelmMapOn";

    private SubRoot subRoot;
    private CyclopsTerrainSonar sonar;
    private TerrainHologram hologram;
    private Transform frame;
    private bool building;
    private float nextPoll;
    private float retryAt;
    private bool near;
    private bool prefLoaded;
    private bool on = true;
    private bool gateOpened;
    private string lastWaiting = string.Empty;

    public SubRoot SubRoot => subRoot;
    public TerrainHologram Hologram => hologram;
    public Transform MapFrame => frame;

    /// <summary>The pilot's choice: the map is on (the first time, and until they switch it off). Kept on this machine.</summary>
    public bool IsOn => on;

    /// <summary>The map is on screen now.</summary>
    public bool Shown => hologram != null && hologram.Exists && hologram.Shown;

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        sonar = GetComponent<CyclopsTerrainSonar>();

        // what the map shows besides the terrain, on this copy as on the build room's: the marks and the route, and the contacts
        GameObject marks = new("NitroxHelmMapMarks");
        marks.transform.SetParent(transform, false);
        marks.AddComponent<HelmMarkLayer>().Bind(subRoot, () => hologram, false, "NitroxHelmMapMarksCanvas");
        GameObject contactsObject = new("NitroxHelmMapContacts");
        contactsObject.transform.SetParent(transform, false);
        contactsObject.AddComponent<HelmContactLayer>().Bind(subRoot, () => hologram);
    }

    private void OnDestroy()
    {
        hologram?.Destroy();
        hologram = null;
        if (frame)
        {
            Destroy(frame.gameObject);
        }
    }

    /// <summary>Switches the map on or off for this player and remembers it.</summary>
    public void SetOn(bool value)
    {
        on = value;
        try
        {
            PrefsStore.SetInt(ON_PREF, on ? 1 : 0);
            PrefsStore.Save();
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] helm map: the choice could not be saved ({exception.GetType().Name})");
        }
        Log.Info($"[VehicleUpgrades] helm map: {(on ? "on" : "off")} for this player");
    }

    private void Update()
    {
        if (frame && hologram != null && hologram.Exists)
        {
            PlaceFrame();
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        Refresh();
    }

    private bool GateIsOpen()
    {
        bool loading = uGUI.main && uGUI.main.loading && uGUI.main.loading.isLoading;
        bool settled = gateOpened || (LargeWorldStreamer.main && LargeWorldStreamer.main.IsReady() && LargeWorldStreamer.main.IsWorldSettled());
        string waiting = HologramStartRules.WaitingFor(Multiplayer.Active, Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted, uGUI.isIntro, loading, settled);
        if (waiting != null)
        {
            if (waiting != lastWaiting)
            {
                lastWaiting = waiting;
                Log.Info($"[VehicleUpgrades] helm map: {subRoot.name}: waiting to start, {waiting}");
            }
            return false;
        }
        if (!gateOpened)
        {
            gateOpened = true;
            Log.Info($"[VehicleUpgrades] helm map: {subRoot.name}: the join is finished and the world has settled; the map may stream terrain now");
        }
        lastWaiting = string.Empty;
        return true;
    }

    private void LoadPreference()
    {
        if (prefLoaded)
        {
            return;
        }
        prefLoaded = true;
        try
        {
            on = PrefsStore.GetInt(ON_PREF, 1) != 0;
        }
        catch (System.Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] helm map: the saved choice could not be read ({exception.GetType().Name}); the map is on");
        }
        Log.Info($"[VehicleUpgrades] helm map: {(on ? "on" : "off")} for this player (their own choice, kept on their machine)");
    }

    private void EnsureFrame()
    {
        if (frame)
        {
            return;
        }
        GameObject frameObject = new(FRAME_NAME);
        frameObject.transform.SetParent(transform, false);
        frame = frameObject.transform;
        (float x, float y, float z) = HelmLayout.MapCenter;
        frame.localPosition = new Vector3(x, y, z);
        frame.localRotation = Quaternion.Euler(0f, HelmLayout.FRAME_YAW, 0f) * Quaternion.Euler(0f, 0f, -HelmLayout.TILT);
        frame.localScale = Vector3.one;
        Log.Info($"[VehicleUpgrades] helm map: {subRoot.name}: the map's frame is in front of the pilot, hull-local {frame.localPosition:F2}, radius {HelmLayout.RADIUS:F2} m");
    }

    /// <summary>Every frame while the map exists: the frame is where it belongs, and the map keeps the real directions (the turn of the frame is taken out of the map's own turn, so that the bow is at the far edge).</summary>
    private void PlaceFrame()
    {
        (float x, float y, float z) = HelmLayout.MapCenter;
        frame.localPosition = new Vector3(x, y, z);
        frame.localRotation = Quaternion.Euler(0f, HelmLayout.FRAME_YAW, 0f) * Quaternion.Euler(0f, 0f, -HelmLayout.TILT);
        TerrainHologramAligner aligner = hologram.Aligner;
        aligner.View = Quaternion.AngleAxis(NavigatorMapView.DEFAULT_YAW - HelmLayout.FRAME_YAW, Vector3.up);
        if (!Mathf.Approximately(aligner.Zoom, 1f))
        {
            aligner.SetZoom(1f);
        }
    }

    private float WantedRange => HelmLayout.RangeFor(sonar ? sonar.WantedRange : HelmLayout.MAX_RANGE);

    private void Refresh()
    {
        if (!GateIsOpen())
        {
            hologram?.SetShown(false);
            return;
        }
        LoadPreference();
        EnsureFrame();
        bool aboard = Player.main && Player.main.currentSub == subRoot;
        bool available = TerrainSonar.IsAvailable(subRoot);
        float distance = Player.main && frame ? Vector3.Distance(Player.main.transform.position, frame.position) : float.NaN;
        near = NavigatorConsoleLayout.ShouldShowMap(distance, near);
        bool show = on && near && TerrainSonarRules.ShouldShow(available, aboard);
        float range = WantedRange;
        if (hologram != null && (!hologram.Exists || (show && TerrainSonarRules.NeedsRebuild(hologram.RequestedRange, range))))
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
            float until = Time.unscaledTime + WAIT_FOR_WORLD_SECONDS;
            while ((LargeWorldStreamer.main == null || !LargeWorldStreamer.main.IsReady()) && Time.unscaledTime < until)
            {
                yield return null;
            }
            if (LargeWorldStreamer.main == null || !LargeWorldStreamer.main.IsReady())
            {
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }

            TerrainHologramTemplate template = null;
            yield return TerrainHologramTemplate.Request(found => template = found);
            if (template == null || !subRoot || !frame)
            {
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }

            hologram = TerrainHologram.TryCreateOn(subRoot, frame, HOLOGRAM_NAME, HelmLayout.RADIUS / Mathf.Max(1f, range), range, template);
            if (hologram == null)
            {
                retryAt = Time.unscaledTime + RETRY_SECONDS;
                yield break;
            }
            Log.Info($"[VehicleUpgrades] helm map: {subRoot.name}: built for {range:F0} m, radius {HelmLayout.RADIUS:F2} m");
        }
        finally
        {
            building = false;
        }
    }
}
