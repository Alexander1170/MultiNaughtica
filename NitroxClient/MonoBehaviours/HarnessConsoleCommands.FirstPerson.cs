#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.GameLogic.Settings;
using Nitrox.Model.Core;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check of the player's own model in first person (docs/coop/PLAYER_MODELS.md, "Your own model"). Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs, run with the server's
// "/console <player> vufp <what>": state (the copy and every mesh of the vanilla body: switched on, forced off, seen by a camera, its shadow), toggle on|off (the Options setting), measure (how far the hands and feet of
// the copy are from the vanilla ones), perf <seconds> (the time that a frame takes, with the copy on or off).
public partial class HarnessConsoleCommands
{
    private void LiveCommand_vufp(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        switch (what)
        {
            case "toggle":
                {
                    bool on = LiveArgString(n, 1, "on") != "off";
                    NitroxPrefs.ShowOwnModel.Value = on;
                    FirstPersonModel.Refresh();
                    Log.Info($"{VU}vufp: toggle setting={NitroxPrefs.ShowOwnModel.Value} copy={(CustomPlayerModels.Local ? CustomPlayerModels.Local.ModelId : "none")}");
                }
                break;
            case "measure":
                {
                    CustomPlayerModel local = CustomPlayerModels.Local;
                    Log.Info($"{VU}vufp: measure {(local ? local.Measure() : "no copy")}");
                }
                break;
            case "hands":
                TunedHands().Enabled = LiveArgString(n, 1, "on") != "off";
                Log.Info($"{VU}vufp: hands {HandSettingsText()}");
                break;
            case "fingers":
                TunedHands().Fingers = LiveArgString(n, 1, "on") != "off";
                Log.Info($"{VU}vufp: fingers {HandSettingsText()}");
                break;
            case "thumbs":
                TunedHands().Thumbs = LiveArgString(n, 1, "on") != "off";
                Log.Info($"{VU}vufp: thumbs {HandSettingsText()}");
                break;
            case "calibrate":
                // calibrate [id]: the auto-calibration of a model against the live diver, as a report in the log, and the measured block of its profile written beside its file
                VuFpCalibrate(LiveArgString(n, 1, ""));
                break;
            case "tool":
                // tool on|off: the hand follows the hand target of the tool it holds (on) or the hand of the vanilla body (off)
                TunedHands().ToolTargets = LiveArgString(n, 1, "on") != "off";
                Log.Info($"{VU}vufp: tool {HandSettingsText()}");
                break;
            case "stretch":
                // stretch <factor>: the most that an arm is made longer to reach a grip (1 never)
                TunedHands().MaxStretch = Mathf.Clamp(LiveArgFloat(n, 1, 1.2f), 1f, 1.5f);
                Log.Info($"{VU}vufp: stretch {HandSettingsText()}");
                break;
            case "pda":
                // pda <metres>: how far the left hand is pulled toward the camera while the PDA is open
                TunedHands().PdaPullBack = LiveArgFloat(n, 1, 0f);
                Log.Info($"{VU}vufp: pda {HandSettingsText()}");
                break;
            case "anchor":
                {
                    // anchor forward,palm,thumb: where the grip of the model's hand sits against the vanilla knuckles, in metres in the frame of the vanilla palm
                    string[] parts = LiveArgString(n, 1, "0,0,0").Split(',');
                    PlayerModelHandSettings settings = TunedHands();
                    settings.AnchorForward = parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float forward) ? forward : 0f;
                    settings.AnchorPalm = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float palm) ? palm : 0f;
                    settings.AnchorThumb = parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float thumb) ? thumb : 0f;
                    Log.Info($"{VU}vufp: anchor {HandSettingsText()}");
                }
                break;
            case "roll":
                TunedHands().RollTrimDegrees = LiveArgFloat(n, 1, 0f);
                Log.Info($"{VU}vufp: roll {HandSettingsText()}");
                break;
            case "twist":
                TunedHands().TwistShare = Mathf.Clamp01(LiveArgFloat(n, 1, PlayerModelArmIk.DEFAULT_TWIST_SHARE));
                Log.Info($"{VU}vufp: twist {HandSettingsText()}");
                break;
            case "perf":
                StartCoroutine(VuFpPerf(float.TryParse(LiveArgString(n, 1, "8"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds) ? Mathf.Clamp(seconds, 1f, 60f) : 8f));
                break;
            case "look":
                {
                    // look yaw,pitch: turns the first-person view (the yaw is the game's rotationX, the pitch its rotationY, positive up: -70 looks down at the feet)
                    string[] angles = LiveArgString(n, 1, "0,-70").Split(',');
                    float lookYaw = angles.Length > 0 && float.TryParse(angles[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedYaw) ? parsedYaw : 0f;
                    float lookPitch = angles.Length > 1 && float.TryParse(angles[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedPitch) ? parsedPitch : -70f;
                    if (MainCameraControl.main)
                    {
                        MainCameraControl.main.rotationX = lookYaw;
                        MainCameraControl.main.rotationY = lookPitch;
                    }
                    Log.Info($"{VU}vufp: look yaw={lookYaw:F0} pitch={lookPitch:F0}");
                }
                break;
            case "shots":
                // shots <tag> <count> <seconds between>: takes that many pictures of this client, named <tag>1, <tag>2 ... (the swimmer cannot type)
                StartCoroutine(VuFpShots(LiveArgString(n, 1, "fp"), (int)LiveArgFloat(n, 2, 4f), LiveArgFloat(n, 3, 1f)));
                break;
            default:
                VuFpState();
                break;
        }
    }

    private static void VuFpCalibrate(string requested)
    {
        try
        {
            string id = requested.Length > 0 ? requested : PlayerModelSelection.SavedId;
            if (id.Length == 0)
            {
                Log.Info($"{VU}vufp: calibrate no model: name one (vufp calibrate player-sample) or pick one in Options");
                return;
            }
            LoadedPlayerModel model = PlayerModelLibrary.Load(id);
            if (model == null)
            {
                Log.Info($"{VU}vufp: calibrate the model '{id}' could not be loaded");
                return;
            }
            GameObject view = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerModel;
            if (!view)
            {
                Log.Info($"{VU}vufp: calibrate the local body has no player_view yet");
                return;
            }
            IReadOnlyList<string> report = PlayerModelCalibrator.Report(model, view.transform, out IReadOnlyList<KeyValuePair<string, float>> measured);
            foreach (string line in report)
            {
                Log.Info($"{VU}vufp: calibrate {line}");
            }
            string path = PlayerModelCalibrator.WriteProfile(model, measured);
            Log.Info($"{VU}vufp: calibrate wrote the measured block of the profile to {path}: copy that file to Nitrox.Assets.Subnautica/Resources/PlayerModels in the repository");
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, $"{VU}vufp: calibrate failed");
        }
    }

    /// <summary>
    /// The settings of the hands that a command changes: the ones of the copy of the local player's own body (they start as the profile has them), or, when there is none, a set of their own. The set is kept as what a
    /// copy that is made later starts from (<see cref="CustomPlayerModel.DebugHandSettings" />).
    /// </summary>
    private static PlayerModelHandSettings TunedHands()
    {
        CustomPlayerModel local = CustomPlayerModels.Local;
        PlayerModelHandSettings settings = local ? local.HandSettings : (CustomPlayerModel.DebugHandSettings ?? new PlayerModelHandSettings());
        CustomPlayerModel.DebugHandSettings = settings;
        return settings;
    }

    private static string HandSettingsText()
    {
        CustomPlayerModel local = CustomPlayerModels.Local;
        PlayerModelHandSettings settings = local ? local.HandSettings : (CustomPlayerModel.DebugHandSettings ?? new PlayerModelHandSettings());
        return $"enabled={settings.Enabled} fingers={settings.Fingers} thumbs={settings.Thumbs} toolTargets={settings.ToolTargets} maxStretch={settings.MaxStretch:F2} pdaPullBack={settings.PdaPullBack:F3} anchor=({settings.AnchorForward:F3},{settings.AnchorPalm:F3},{settings.AnchorThumb:F3}) rollTrim={settings.RollTrimDegrees:F1} twistShare={settings.TwistShare:F2}";
    }

    private static void VuFpState()
    {
        CustomPlayerModel local = CustomPlayerModels.Local;
        Player player = Player.main;
        GameObject view = null;
        try
        {
            view = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerModel;
        }
        catch (System.Exception exception)
        {
            Log.Info($"{VU}vufp: state no view: {exception.Message}");
        }
        Camera camera = MainCamera.camera;
        string copy = local
            ? $"copy={local.ModelId} shown={local.ModelShown} headHidden={local.HeadHidden} emoteView={local.EmoteView} forcedOff={local.HiddenCount} followed={local.FollowedCount} scale={local.Scale:F3}"
            : "copy=none";
        Log.Info($"{VU}vufp: state setting={NitroxPrefs.ShowOwnModel.Value} saved='{PlayerModelSelection.SavedId}' {copy} hands[{HandSettingsText()}] last='{FirstPersonModel.LastMessage}' emoteCamera={PlayerEmoteDriver.CameraOut} cameraNear={(camera ? camera.nearClipPlane : -1f):F3} fov={(camera ? camera.fieldOfView : -1f):F0} viewActive={(view ? view.activeInHierarchy : false)} viewScale={(view ? view.transform.lossyScale.y : -1f):F3} underwater={(player && player.IsUnderwater())} inSub={(player && player.currentSub ? player.currentSub.name : "none")} held={(Inventory.main != null && Inventory.main.GetHeldTool() ? Inventory.main.GetHeldTool().name : "none")}");
        if (!view)
        {
            return;
        }
        int index = 0;
        foreach (Renderer renderer in view.GetComponentsInChildren<Renderer>(true))
        {
            string kind = renderer is SkinnedMeshRenderer ? "Skinned" : renderer is MeshRenderer ? "Mesh" : renderer.GetType().Name;
            int vertices = renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh ? skinned.sharedMesh.vertexCount : renderer is MeshRenderer && renderer.GetComponent<MeshFilter>() && renderer.GetComponent<MeshFilter>().sharedMesh ? renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount : -1;
            StringBuilder path = new(renderer.transform.name);
            for (Transform parent = renderer.transform.parent; parent && parent != view.transform && path.Length < 120; parent = parent.parent)
            {
                path.Insert(0, $"{parent.name}/");
            }
            Log.Info($"{VU}vufp: r{index++} {kind} enabled={renderer.enabled} active={renderer.gameObject.activeInHierarchy} forcedOff={renderer.forceRenderingOff} visible={renderer.isVisible} shadow={renderer.shadowCastingMode} layer={LayerMask.LayerToName(renderer.gameObject.layer)} vertices={vertices} path={path.ToString().Replace(' ', '_')}");
        }
        Log.Info($"{VU}vufp: state done renderers={index}");
    }

    private IEnumerator VuFpShots(string tag, int count, float seconds)
    {
        for (int i = 1; i <= count; i++)
        {
            yield return LiveShotRoutine(false, 0f, $"{tag}{i}");
            Log.Info($"{VU}vufp: shots {tag}{i} done");
            yield return new WaitForSecondsRealtime(seconds);
        }
        Log.Info($"{VU}vufp: shots done count={count}");
    }

    private static IEnumerator VuFpPerf(float seconds)
    {
        // a frame takes what it takes: the average and the slowest of the frames of this many seconds, and what the pose copy costs of that
        yield return null;
        CustomPlayerModels.Stats.Reset();
        float end = Time.unscaledTime + seconds;
        int frames = 0;
        float sum = 0f;
        float max = 0f;
        while (Time.unscaledTime < end)
        {
            float delta = Time.unscaledDeltaTime;
            frames++;
            sum += delta;
            max = Mathf.Max(max, delta);
            yield return null;
        }
        CustomPlayerModel local = CustomPlayerModels.Local;
        PoseCopyStats stats = CustomPlayerModels.Stats;
        Log.Info($"{VU}vufp: perf seconds={seconds:F0} frames={frames} avgFrameMs={(frames > 0 ? sum / frames * 1000f : 0f):F2} maxFrameMs={max * 1000f:F2} copy={(local ? "on" : "off")} poseAvgUs={stats.AverageMicroseconds:F0} poseMaxUs={stats.MaxMicroseconds:F0} poseFrames={stats.Frames} models={stats.ModelFrames}");
    }
}
#endif
