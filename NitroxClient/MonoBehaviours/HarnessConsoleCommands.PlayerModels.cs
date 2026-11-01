#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.ChatUI;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel;
using NitroxClient.GameLogic.PlayerModels;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug command of the custom player models (docs/coop/PLAYER_MODELS.md). Logs <c>[SyncHarness] vumodel: key=value</c> lines.
///     <c>vumodel list</c> lists the models that were found. <c>vumodel spawn|ground|beside [id]</c> reads the model (the first one found when no id is given) and puts a copy in the world, facing the camera:
///     <c>spawn</c> 3 m in front of the camera, <c>ground</c> 4 m ahead on the floor below, <c>beside</c> next to the first remote player (so that the picture shows it beside a vanilla body); the camera turns to
///     it, except for <c>spawn</c>. (The server's /console passes a command and two words, so the verb and the id are all there is.) <c>vumodel clear</c> takes the copies away. <c>vumodel shot tag</c> is
///     <c>liveshot now tag</c> with the chat panel put away first.
///     <c>vumodel dump</c> logs the skeleton of the vanilla body (names, local poses, the Animator's culling mode, the bind poses of its biggest skinned mesh), which is what the bone map of the pose copy is made from.
///     <c>vumodel material</c> lists the properties of the dive suit material that the copies are made from. <c>vumodel normal rgb|dxt5nm</c> picks the encoding of the normal map and forgets the loaded models (spawn
///     again). <c>vumodel reload</c> forgets the loaded models and looks at the folders again.
/// </summary>
public partial class HarnessConsoleCommands
{
    private readonly List<GameObject> spawnedPlayerModels = [];
    private string playerModelFilmPrefix = "film";
    private float playerModelFilmInterval = 2f;

    private void LiveCommand_vumodel(NotificationCenter.Notification n)
    {
        switch (LiveArgString(n, 0, "list"))
        {
            case "spawn":
                PlayerModelSpawn(LiveArgString(n, 1, ""), "");
                break;
            case "ground":
                PlayerModelSpawn(LiveArgString(n, 1, ""), "ground");
                break;
            case "beside":
                PlayerModelSpawn(LiveArgString(n, 1, ""), "beside");
                break;
            case "clear":
                PlayerModelClear();
                Log.Info($"{VU}vumodel: cleared");
                break;
            case "shot":
                StartCoroutine(PlayerModelShot(LiveArgString(n, 1, "shot")));
                break;
            case "wear":
                PlayerModelWear(LiveArgString(n, 1, ""));
                break;
            case "unwear":
                PlayerModelUnwear();
                break;
            case "vanilla":
                PlayerModelVanilla(LiveArgString(n, 1, "on") != "off");
                break;
            case "scale":
                CustomPlayerModels.ScaleSource = LiveArgString(n, 1, "hips") == "head" ? PlayerModelScaleSource.Head : PlayerModelScaleSource.Hips;
                Log.Info($"{VU}vumodel: scale source is now {CustomPlayerModels.ScaleSource} (applies to the next wear)");
                break;
            case "look":
                StartCoroutine(PlayerModelLook());
                break;
            case "interval":
                if (float.TryParse(LiveArgString(n, 1, "2"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float filmInterval))
                {
                    playerModelFilmInterval = Mathf.Clamp(filmInterval, 0.5f, 10f);
                    Log.Info($"{VU}vumodel: interval is now {playerModelFilmInterval:F1} s");
                }
                break;
            case "prefix":
                playerModelFilmPrefix = Regex.Replace(LiveArgString(n, 1, "film"), "[^A-Za-z0-9_-]", string.Empty);
                Log.Info($"{VU}vumodel: prefix is now {playerModelFilmPrefix}");
                break;
            case "film":
                StartCoroutine(PlayerModelFilm(float.TryParse(LiveArgString(n, 1, "10"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds) ? seconds : 10f));
                break;
            case "error":
                StartCoroutine(PlayerModelError());
                break;
            case "hold":
                StartCoroutine(PlayerModelHold(LiveArgString(n, 1, "FireExtinguisher")));
                break;
            case "holster":
                if (Inventory.main != null && Inventory.main.quickSlots != null)
                {
                    Inventory.main.quickSlots.DeselectImmediate();
                }
                Log.Info($"{VU}vumodel: holster done");
                break;
            case "nudge":
                if (Player.main && Camera.main && float.TryParse(LiveArgString(n, 1, "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float meters))
                {
                    Vector3 ahead = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                    Player.main.SetPosition(Player.main.transform.position + ahead * meters);
                    Log.Info($"{VU}vumodel: nudge meters={meters:F1} now={Player.main.transform.position.ToString("F1")}");
                }
                break;
            case "side":
                if (Player.main && Camera.main && float.TryParse(LiveArgString(n, 1, "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sideMeters))
                {
                    Vector3 right = Vector3.ProjectOnPlane(Camera.main.transform.right, Vector3.up).normalized;
                    Player.main.SetPosition(Player.main.transform.position + right * sideMeters);
                    Log.Info($"{VU}vumodel: side meters={sideMeters:F1} now={Player.main.transform.position.ToString("F1")}");
                }
                break;
            case "turn":
                if (MainCameraControl.main && float.TryParse(LiveArgString(n, 1, "0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float yaw))
                {
                    MainCameraControl.main.rotationX = yaw;
                    MainCameraControl.main.rotationY = 0f;
                    Log.Info($"{VU}vumodel: turn yaw={yaw:F0}");
                }
                break;
            case "where":
                {
                    RemotePlayer whereTarget = PlayerModelFirstRemotePlayer();
                    CustomPlayerModel whereModel = whereTarget != null ? CustomPlayerModels.Of(whereTarget) : null;
                    Log.Info(whereModel ? $"{VU}vumodel: where {whereModel.Where()}" : $"{VU}vumodel: where outcome=NothingWorn");
                }
                break;
            case "who":
                PlayerModelWho();
                break;
            case "choose":
                PlayerModelSelection.Choose(LiveArgString(n, 1, ""));
                Log.Info($"{VU}vumodel: choose saved='{PlayerModelSelection.SavedId}'");
                break;
            case "forget":
                PlayerModelLibrary.DebugForget(LiveArgString(n, 1, ""));
                Log.Info($"{VU}vumodel: forget models={string.Join(",", PlayerModelLibrary.Ids)}");
                break;
            case "skew":
                PlayerModelLibrary.DebugLengthSkew = long.TryParse(LiveArgString(n, 1, "0"), out long skew) ? skew : 0;
                Log.Info($"{VU}vumodel: skew {PlayerModelLibrary.DebugLengthSkew}");
                break;
            case "menu":
                StartCoroutine(PlayerModelMenu(LiveArgString(n, 1, "list")));
                break;
            case "inroom":
                {
                    // inroom dx or inroom dx,dz (a console command takes one word after the verb: the two offsets share it)
                    string[] roomOffsets = LiveArgString(n, 1, "0").Split(',');
                    float roomDx = float.TryParse(roomOffsets[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDx) ? parsedDx : 0f;
                    float roomDz = roomOffsets.Length > 1 && float.TryParse(roomOffsets[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDz) ? parsedDz : 0f;
                    PlayerModelInRoom(roomDx, roomDz);
                }
                break;
            case "emotes":
                PlayerModelEmotes();
                break;
            case "emote":
                PlayerModelEmote(LiveArgString(n, 1, ""));
                break;
            case "emotestate":
                PlayerModelEmoteState();
                break;
            case "camstate":
                PlayerModelCamState();
                break;
            case "nearby":
                PlayerModelNearby();
                break;
            case "holdemote":
                // holdemote <id>: as selfemote, and the emote does not end by itself (its clip goes round) until selfemote stop: the live check's commands take seconds each
                Log.Info($"{VU}vumodel: holdemote {PlayerEmoteDriver.DebugStartEmote(LiveArgString(n, 1, ""), true)}");
                break;
            case "selfemote":
                {
                    // selfemote <id>: this player's own emote, as if picked on the wheel (the camera goes out with it); selfemote stop: ends it
                    string selfId = LiveArgString(n, 1, "");
                    string selfOutcome = string.Equals(selfId, "stop", System.StringComparison.OrdinalIgnoreCase) ? PlayerEmoteDriver.DebugStopEmote() : PlayerEmoteDriver.DebugStartEmote(selfId);
                    Log.Info($"{VU}vumodel: selfemote {selfOutcome}");
                }
                break;
            case "orbit":
                {
                    // orbit yaw,pitch,distance: the emote camera's orbit (degrees, degrees, metres), as the mouse and the wheel would turn it
                    string[] orbit = LiveArgString(n, 1, "0,-12,2.8").Split(',');
                    float orbitYaw = orbit.Length > 0 && float.TryParse(orbit[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedYaw) ? parsedYaw : 0f;
                    float orbitPitch = orbit.Length > 1 && float.TryParse(orbit[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedPitch) ? parsedPitch : -12f;
                    float orbitDistance = orbit.Length > 2 && float.TryParse(orbit[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedDistance) ? parsedDistance : 2.8f;
                    Log.Info($"{VU}vumodel: orbit {PlayerEmoteDriver.DebugOrbit(orbitYaw, orbitPitch, orbitDistance)}");
                }
                break;
            case "remotepose":
                PlayerModelRemotePose();
                break;
            case "tags":
                PlayerModelTags();
                break;
            case "forever":
                // forever [off]: the next emote that starts (on the wheel with the real mouse too) does not end by itself, until it is stopped: the live check's commands take seconds each
                PlayerEmoteDriver.DebugForeverNext = LiveArgString(n, 1, "on") != "off";
                Log.Info($"{VU}vumodel: forever next={PlayerEmoteDriver.DebugForeverNext}");
                break;
            case "camprobe":
                {
                    // camprobe yaw,pitch,distance: what the sweeps of the emote camera see along that way from the character (see PlayerEmoteCamera.DebugProbe)
                    string[] probe = LiveArgString(n, 1, "0,-5,2.8").Split(',');
                    float probeYaw = probe.Length > 0 && float.TryParse(probe[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedProbeYaw) ? parsedProbeYaw : 0f;
                    float probePitch = probe.Length > 1 && float.TryParse(probe[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedProbePitch) ? parsedProbePitch : -5f;
                    float probeDistance = probe.Length > 2 && float.TryParse(probe[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedProbeDistance) ? parsedProbeDistance : 2.8f;
                    Log.Info($"{VU}vumodel: camprobe {PlayerEmoteDriver.DebugProbe(probeYaw, probePitch, probeDistance)}");
                }
                break;
            case "wheel":
                PlayerModelWheel(LiveArgString(n, 1, "state"));
                break;
            case "hurt":
                PlayerModelHurt();
                break;
            case "state":
                PlayerModelState();
                break;
            case "perf":
                PlayerModelPerf();
                break;
            case "dump":
                PlayerModelDump();
                break;
            case "material":
                PlayerModelMaterial();
                break;
            case "normal":
                PlayerModelClear();
                PlayerModelLibrary.Unload();
                PlayerModelBuilder.DefaultEncoding = LiveArgString(n, 1, "rgb") == "dxt5nm" ? NormalMapEncoding.Dxt5nm : NormalMapEncoding.Rgb;
                Log.Info($"{VU}vumodel: normal map encoding is now {PlayerModelBuilder.DefaultEncoding}");
                break;
            case "reload":
                PlayerModelClear();
                PlayerModelLibrary.Unload();
                PlayerModelLibrary.Rescan();
                Log.Info($"{VU}vumodel: reloaded");
                break;
            default:
                Log.Info($"{VU}vumodel: models={string.Join(",", PlayerModelLibrary.Ids)} shipped={PlayerModelLibrary.ShippedFolder} user={PlayerModelLibrary.UserFolder} saved='{PlayerModelSelection.SavedId}'");
                break;
        }
    }

    /// <summary>
    ///     A picture like <c>liveshot now &lt;tag&gt;</c> (same file, same <c>[SyncHarness] liveshot: saved</c> line), with the chat panel put away first (it stays open after a typed command and covers the left of
    ///     the picture) and a second for the frame to settle.
    /// </summary>
    private IEnumerator PlayerModelShot(string tag)
    {
        yield return PlayerModelPutChatAway();
        if (liveShotBusy)
        {
            Log.Info($"{VU}vumodel: shot outcome=Busy");
            yield break;
        }
        string clean = Regex.Replace(tag, "[^A-Za-z0-9_-]", string.Empty);
        StartCoroutine(LiveShotRoutine(false, 0f, clean.Length == 0 ? "shot" : clean));
    }

    /// <summary>
    ///     The server's echo of the typed command ("Alice runs 'vumodel shot ...'") opens the chat panel again when it arrives, so it is put away after that, and at once: its own fade is a hundred frames long.
    /// </summary>
    private IEnumerator PlayerModelPutChatAway()
    {
        yield return new WaitForSeconds(1.5f);
        PlayerChatManager.Instance.HideChat();
        yield return null;
        yield return null;
        Transform chat = PlayerChatManager.Instance.PlayerChatTransform;
        CanvasGroup chatGroup = chat ? chat.GetComponent<CanvasGroup>() : null;
        if (chatGroup)
        {
            chatGroup.alpha = 0f;
        }
        yield return new WaitForSeconds(0.4f);
    }

    private static RemotePlayer PlayerModelFirstRemotePlayer()
    {
        return NitroxServiceLocator.LocateService<PlayerManager>().GetAll().FirstOrDefault(player => player.Body != null);
    }

    private void PlayerModelWear(string id)
    {
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        if (player == null)
        {
            Log.Info($"{VU}vumodel: wear outcome=NoTeammate");
            return;
        }
        if (id.Length == 0)
        {
            id = PlayerModelLibrary.Ids.FirstOrDefault() ?? "";
        }
        bool worn = CustomPlayerModels.Wear(player, id, out string message);
        Log.Info($"{VU}vumodel: wear outcome={(worn ? "Worn" : "Failed")} player={player.PlayerName} {message}");
        if (worn)
        {
            StringBuilder fixes = new();
            foreach (PlayerModelBoneConstant bone in CustomPlayerModels.Of(player).Calibration.Bones)
            {
                fixes.Append($" {bone.Mixamo}:{bone.FixDegrees:F0}");
            }
            Log.Info($"{VU}vumodel: wear fix angles in degrees{fixes}");
        }
    }

    private void PlayerModelUnwear()
    {
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        bool removed = player != null && CustomPlayerModels.Remove(player);
        Log.Info($"{VU}vumodel: unwear outcome={(player == null ? "NoTeammate" : removed ? "Removed" : "NothingWorn")}");
    }

    private void PlayerModelVanilla(bool visible)
    {
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        CustomPlayerModel model = player != null ? CustomPlayerModels.Of(player) : null;
        if (!model)
        {
            Log.Info($"{VU}vumodel: vanilla outcome=NothingWorn");
            return;
        }
        model.SetVanillaVisible(visible);
        Log.Info($"{VU}vumodel: vanilla visible={visible} meshes={model.HiddenCount}");
    }

    /// <summary>
    ///     Turns the camera one step towards the middle of the first remote player's body (a step: the yaw and the pitch that are still missing are added to the camera control's, as read from where the camera
    ///     really looks, so that it works in the frame of a Cyclops too). <paramref name="error" /> is the angle in degrees that was left before the step. False when there is no remote player.
    /// </summary>
    private static bool PlayerModelAimAtRemotePlayer(out float error)
    {
        error = 0f;
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        Camera camera = Camera.main;
        if (player == null || !camera || !MainCameraControl.main)
        {
            return false;
        }
        CustomPlayerModel model = CustomPlayerModels.Of(player);
        Vector3 target = model ? model.AimPoint : player.Body.transform.position + Vector3.up;
        Vector3 toTarget = target - camera.transform.position;
        if (toTarget.sqrMagnitude < 0.01f)
        {
            return true;
        }
        Vector3 forward = camera.transform.forward;
        float currentYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float wantedYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        float currentPitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float wantedPitch = Mathf.Asin(Mathf.Clamp(toTarget.y / toTarget.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
        MainCameraControl.main.rotationX += Mathf.DeltaAngle(currentYaw, wantedYaw);
        MainCameraControl.main.rotationY += wantedPitch - currentPitch;
        error = Vector3.Angle(forward, toTarget);
        return true;
    }

    /// <summary><c>vumodel look</c>: aims the camera at the first remote player's body for a few frames and says how far off it was at the end.</summary>
    private static IEnumerator PlayerModelLook()
    {
        float error = 0f;
        bool aimed = false;
        for (int i = 0; i < 8; i++)
        {
            aimed = PlayerModelAimAtRemotePlayer(out error);
            if (!aimed)
            {
                break;
            }
            yield return null;
        }
        Log.Info($"{VU}vumodel: look {(aimed ? $"outcome=Aimed errorBeforeLastStep={error:F1}deg" : "outcome=NoTeammate")}");
    }

    /// <summary>
    ///     <c>vumodel film seconds</c>: for that long the camera follows the first remote player's body every frame and a picture is taken every two seconds, or every <c>vumodel interval seconds</c> (tags film1, film2, ...), each with a line that says how
    ///     the body faces the camera (<c>facing</c> is the angle between the way the body faces and the way to the camera: 0 front, 90 side, 180 back).
    /// </summary>
    private IEnumerator PlayerModelFilm(float seconds)
    {
        yield return PlayerModelPutChatAway();
        float end = Time.unscaledTime + seconds;
        float nextShot = Time.unscaledTime;
        int count = 0;
        while (Time.unscaledTime < end)
        {
            if (!PlayerModelAimAtRemotePlayer(out _))
            {
                Log.Info($"{VU}vumodel: film outcome=NoTeammate");
                yield break;
            }
            if (Time.unscaledTime >= nextShot && !liveShotBusy)
            {
                RemotePlayer player = PlayerModelFirstRemotePlayer();
                CustomPlayerModel model = CustomPlayerModels.Of(player);
                Vector3 toCamera = Camera.main.transform.position - (model ? model.AimPoint : player.Body.transform.position);
                Vector3 facing = model ? model.Facing : player.Body.transform.forward;
                float angle = Vector3.Angle(Vector3.ProjectOnPlane(facing, Vector3.up), Vector3.ProjectOnPlane(toCamera, Vector3.up));
                count++;
                Log.Info($"{VU}vumodel: film shot={count} tag={playerModelFilmPrefix}{count} facing={angle:F0} distance={toCamera.magnitude:F1} pose={(player.PoseTracker.Swimming ? "swimming" : "walking")}");
                StartCoroutine(LiveShotRoutine(false, 0f, $"{playerModelFilmPrefix}{count}"));
                nextShot = Time.unscaledTime + playerModelFilmInterval;
            }
            yield return null;
        }
        Log.Info($"{VU}vumodel: film done shots={count}");
    }

    /// <summary>
    ///     <c>vumodel hold [tool]</c>: this client's player gets a new tool (a TechType name, a fire extinguisher by default), it goes into the first quick slot and is drawn, which is what the others see as a held tool.
    ///     <c>vumodel holster</c> puts it away.
    /// </summary>
    private static IEnumerator PlayerModelHold(string toolName)
    {
        if (!Enum.TryParse(toolName, true, out TechType techType))
        {
            Log.Info($"{VU}vumodel: hold outcome=UnknownTool tool={toolName}");
            yield break;
        }
        TaskResult<GameObject> prefabResult = new();
        yield return CraftData.InstantiateFromPrefabAsync(techType, prefabResult, false);
        GameObject item = prefabResult.Get();
        Pickupable pickupable = item ? item.GetComponent<Pickupable>() : null;
        if (!pickupable)
        {
            Log.Info($"{VU}vumodel: hold outcome=NoPickupable tool={techType}");
            yield break;
        }
        pickupable.Initialize();
        NitroxEntity.GetIdOrGenerateNew(item);
        InventoryItem inventoryItem = Inventory.main.container.AddItem(pickupable);
        if (inventoryItem == null)
        {
            Log.Info($"{VU}vumodel: hold outcome=InventoryFull tool={techType}");
            UnityEngine.Object.Destroy(item);
            yield break;
        }
        // the other clients have to get the new item (the entity of the inventory add) before they are told that it is drawn: a draw that comes first is dropped with "Could not find entity"
        yield return new WaitForSeconds(6f);
        Inventory.main.quickSlots.Bind(0, inventoryItem);
        // Select(0) only asks (the slot is activated when the game gets round to it, which took half a minute in the live check); SelectImmediate draws it now
        Inventory.main.quickSlots.SelectImmediate(0);
        PlayerTool held = null;
        for (int i = 0; i < 10 && !held; i++)
        {
            yield return new WaitForSeconds(0.5f);
            held = Inventory.main.GetHeldTool();
        }
        Log.Info($"{VU}vumodel: hold outcome={(held ? "Held" : "NotHeld")} tool={techType} heldTool={(held ? held.name : "none")}");
    }

    /// <summary>
    ///     <c>vumodel error</c>: at the end of this frame, how well the worn model follows the vanilla body (<see cref="CustomPlayerModel.Measure" />).
    /// </summary>
    private IEnumerator PlayerModelError()
    {
        yield return new WaitForEndOfFrame();
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        CustomPlayerModel model = player != null ? CustomPlayerModels.Of(player) : null;
        Log.Info(model ? $"{VU}vumodel: error {model.Measure()}" : $"{VU}vumodel: error outcome=NothingWorn");
    }

    /// <summary>
    ///     <c>vumodel who</c>: the own choice and what was last sent, what is remembered of the others and which of them wear a model here.
    /// </summary>
    private static void PlayerModelWho()
    {
        PlayerManager playerManager = NitroxServiceLocator.LocateService<PlayerManager>();
        StringBuilder worn = new();
        foreach (RemotePlayer player in playerManager.GetAll())
        {
            CustomPlayerModel model = player.Body != null ? CustomPlayerModels.Of(player) : null;
            worn.Append($" {player.PlayerName}={(model ? model.ModelId : "none")}");
        }
        Log.Info($"{VU}vumodel: who own='{PlayerModelSelection.SavedId}' lastSent={(PlayerModelAnnouncer.LastSent.Length == 0 ? "none" : PlayerModelAnnouncer.LastSent)} remembered={PlayerModelChoices.Describe(playerManager)} worn:{worn} loaded={string.Join(",", PlayerModelLibrary.LoadedIds)}");
    }

    private static uGUI_OptionsPanel PlayerModelOptionsPanel()
    {
        return IngameMenu.main ? IngameMenu.main.GetComponentInChildren<uGUI_OptionsPanel>(true) : null;
    }

    /// <summary>The choice control of the Options menu that lists the models: the one whose entries are the list of the library ("Default" and the models).</summary>
    private static uGUI_Choice PlayerModelChoiceControl(out string items)
    {
        items = "";
        uGUI_OptionsPanel panel = PlayerModelOptionsPanel();
        if (!panel)
        {
            return null;
        }
        string[] wanted = PlayerModelLibrary.ListItems();
        FieldInfo field = typeof(uGUI_Choice).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (uGUI_Choice choice in panel.GetComponentsInChildren<uGUI_Choice>(true))
        {
            if (field != null && field.GetValue(choice) is IEnumerable<string> options && options.SequenceEqual(wanted))
            {
                items = string.Join("|", wanted);
                return choice;
            }
        }
        return null;
    }

    private static void PlayerModelClick(Button button)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem)
        {
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(eventSystem), ExecuteEvents.pointerClickHandler);
        }
        else
        {
            button.onClick.Invoke();
        }
    }

    /// <summary>
    ///     <c>vumodel menu open|list|next|prev|close</c>: the real Options menu. <c>open</c> opens the in-game menu, its Options screen and the Nitrox tab and says which entries the list of player models has;
    ///     <c>next</c> and <c>prev</c> click the arrows of that list (the click goes through the event system to the button, as a mouse click does); <c>close</c> closes the menu. Every line says what the control
    ///     shows: <c>index</c> and <c>text</c>.
    /// </summary>
    private IEnumerator PlayerModelMenu(string action)
    {
        if (!IngameMenu.main)
        {
            Log.Info($"{VU}vumodel: menu {action} outcome=NoMenu");
            yield break;
        }
        if (action == "close")
        {
            IngameMenu.main.Close();
            yield return new WaitForSeconds(0.3f);
            Log.Info($"{VU}vumodel: menu close done");
            yield break;
        }
        if (action == "open")
        {
            IngameMenu.main.Open();
            yield return new WaitForSeconds(0.5f);
            IngameMenu.main.ChangeSubscreen("Options");
            yield return new WaitForSeconds(1f);
            uGUI_OptionsPanel panel = PlayerModelOptionsPanel();
            if (!panel)
            {
                Log.Info($"{VU}vumodel: menu open outcome=NoOptionsPanel");
                yield break;
            }
            int tab = -1;
            StringBuilder labels = new();
            for (int i = 0; i < panel.tabsContainer.childCount; i++)
            {
                TextMeshProUGUI text = panel.tabsContainer.GetChild(i).GetComponentInChildren<TextMeshProUGUI>(true);
                string label = text ? text.text : "";
                labels.Append($"{(i > 0 ? "|" : "")}{label}");
                if (label == "Nitrox")
                {
                    tab = i;
                }
            }
            if (tab < 0)
            {
                Log.Info($"{VU}vumodel: menu open outcome=NoNitroxTab tabs={labels}");
                yield break;
            }
            panel.SelectTab(tab);
            yield return new WaitForSeconds(0.5f);
        }

        uGUI_Choice choice = PlayerModelChoiceControl(out string items);
        if (!choice)
        {
            Log.Info($"{VU}vumodel: menu {action} outcome=NoModelList");
            yield break;
        }
        if (action == "next" || action == "prev")
        {
            PlayerModelClick(action == "next" ? choice.nextButton : choice.previousButton);
            yield return new WaitForSeconds(0.5f);
        }
        Log.Info($"{VU}vumodel: menu {action} outcome=Ok items={items} index={choice.value} text='{(choice.currentText ? choice.currentText.text : "")}'");
    }

    /// <summary>
    ///     <c>vumodel inroom [dx[,dz]]</c>: puts this player on the floor in the middle of the nearest base room (the middle of its colliders, the floor found with a ray, <c>dx</c> metres along +X and <c>dz</c>
    ///     along +Z from there) and makes the base his current one, as walking in through the hatch does. <c>enterbase</c> puts a player at the origin of the room's cell, which is at its edge, outside.
    /// </summary>
    private static void PlayerModelInRoom(float dx, float dz)
    {
        if (!Player.main)
        {
            return;
        }
        Base nearest = null;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (!nearest || Vector3.Distance(candidate.transform.position, Player.main.transform.position) < Vector3.Distance(nearest.transform.position, Player.main.transform.position))
            {
                nearest = candidate;
            }
        }
        SubRoot subRoot = nearest ? nearest.GetComponent<SubRoot>() : null;
        if (!subRoot)
        {
            Log.Info($"{VU}vumodel: inroom outcome=NoBase");
            return;
        }
        Bounds bounds = new(nearest.transform.position, Vector3.zero);
        foreach (Collider collider in nearest.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled && !collider.isTrigger)
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        Vector3 middle = new(bounds.center.x + dx, bounds.center.y, bounds.center.z + dz);
        bool floorFound = Physics.Raycast(middle, Vector3.down, out RaycastHit floor, 10f, ~0, QueryTriggerInteraction.Ignore);
        Vector3 feet = floorFound ? floor.point + Vector3.up * 0.15f : middle;
        Player.main.OnPlayerPositionCheat();
        Player.main.SetPosition(feet);
        Player.main.SetCurrentSub(subRoot, true);
        Log.Info($"{VU}vumodel: inroom outcome=Placed at={feet.ToString("F1")} colliderCenter={bounds.center.ToString("F1")} colliderSize={bounds.size.ToString("F1")} floorFound={floorFound} floor={(floorFound ? floor.collider.name : "none")} inside={Player.main.IsInside()} walkable={Player.main.IsInsideWalkable()} sub={(Player.main.GetCurrentSub() ? Player.main.GetCurrentSub().name : "none")}");
    }

    /// <summary>
    ///     <c>vumodel emotes</c>: the emotes of the model that this player chose (what the wheel lists: the id, the length, an L for a loop) and of the model that the first remote player wears.
    /// </summary>
    private static void PlayerModelEmotes()
    {
        string saved = PlayerModelSelection.SavedId;
        IReadOnlyList<EmoteEntry> own = saved.Length > 0 ? PlayerModelLibrary.EmotesOf(saved) : Array.Empty<EmoteEntry>();
        RemotePlayer other = PlayerModelFirstRemotePlayer();
        CustomPlayerModel worn = other != null ? CustomPlayerModels.Of(other) : null;
        string ownText = own == null ? "loading" : string.Join("|", own.Select(entry => $"{entry.Id}:{entry.Duration:F2}{(entry.Loop ? "L" : "")}"));
        string theirs = worn ? string.Join("|", worn.Emotes.Select(entry => entry.Id)) : "none";
        Log.Info($"{VU}vumodel: emotes own='{saved}' list={ownText} teammate={theirs}");
    }

    /// <summary>
    ///     <c>vumodel emote id</c>: plays the clip of that name on the model that the first remote player wears, here only (what a <c>PlayerEmote</c> packet does, without the network); <c>vumodel emote stop</c> stops it.
    /// </summary>
    private static void PlayerModelEmote(string id)
    {
        RemotePlayer other = PlayerModelFirstRemotePlayer();
        CustomPlayerModel worn = other != null ? CustomPlayerModels.Of(other) : null;
        if (!worn)
        {
            Log.Info($"{VU}vumodel: emote outcome=NothingWorn");
            return;
        }
        if (id.Length == 0 || id == "stop")
        {
            Log.Info($"{VU}vumodel: emote outcome={(worn.StopEmote() ? "Stopped" : "NoneRunning")}");
            return;
        }
        bool started = worn.PlayEmote(id, out string message);
        Log.Info($"{VU}vumodel: emote outcome={(started ? "Started" : "Refused")} {message}");
    }

    /// <summary><c>vumodel emotestate</c>: whether an emote plays on the model of the first remote player, and which.</summary>
    private static void PlayerModelEmoteState()
    {
        RemotePlayer other = PlayerModelFirstRemotePlayer();
        CustomPlayerModel worn = other != null ? CustomPlayerModels.Of(other) : null;
        if (!worn)
        {
            Log.Info($"{VU}vumodel: emotestate outcome=NothingWorn");
            return;
        }
        Log.Info($"{VU}vumodel: emotestate emoting={worn.IsEmoting} id={(worn.IsEmoting ? worn.EmoteId : "none")} model={worn.ModelId}");
    }

    /// <summary>
    ///     <c>vumodel nearby</c>: the 25 colliders nearest to the player within 4 m (name, layer, a trigger marked), and whether each is in the layers that pull the emote camera in. Where a wall does not pull the camera in, this
    ///     says which layer the wall is on.
    /// </summary>
    private static void PlayerModelNearby()
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Info($"{VU}vumodel: nearby no player");
            return;
        }
        Vector3 center = player.transform.position + Vector3.up;
        Collider[] hits = Physics.OverlapSphere(center, 4f, ~0, QueryTriggerInteraction.Collide);
        int mask = PlayerEmoteCamera.SolidMask;
        StringBuilder list = new();
        int shown = 0;
        foreach (Collider hit in hits.OrderBy(candidate => (candidate.ClosestPoint(center) - center).sqrMagnitude))
        {
            if (shown++ >= 25)
            {
                break;
            }
            int layer = hit.gameObject.layer;
            bool solid = (mask & (1 << layer)) != 0;
            list.Append($" {hit.name.Replace(' ', '_')}@{LayerMask.LayerToName(layer)}{(hit.isTrigger ? "(trigger)" : string.Empty)}{(solid && !hit.isTrigger ? "(SOLID)" : string.Empty)}:{Vector3.Distance(hit.ClosestPoint(center), center):F1}");
        }
        Log.Info($"{VU}vumodel: nearby count={hits.Length} mask={mask}{list}");
    }

    private static Transform VuFindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }
        return null;
    }

    private static string VuPathOf(Transform transform)
    {
        StringBuilder path = new(transform.name);
        for (Transform parent = transform.parent; parent && path.Length < 160; parent = parent.parent)
        {
            path.Insert(0, $"{parent.name}/");
        }
        return path.ToString().Replace(' ', '_');
    }

    /// <summary>
    ///     <c>vumodel remotepose</c>: what the body of every remote player looks like to the game on this client, as plain tokens: the animator (enabled, culling mode, speed, state, the parameters that the replicator writes),
    ///     whether the renderers were seen by a camera (<c>isVisible</c>), the span of the two hands and how high they are above the body (a T-pose has hands a body's width and more apart, level with the shoulders),
    ///     and where the body is on the screen of the first-person camera. The live check reads it when a friend stands in a T-pose in a picture.
    /// </summary>
    private static void PlayerModelRemotePose()
    {
        int count = 0;
        Camera camera = MainCamera.camera;
        foreach (RemotePlayer remote in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
        {
            if (remote == null || !remote.Body)
            {
                continue;
            }
            count++;
            Animator animator = remote.PlayerModel ? remote.PlayerModel.GetComponent<Animator>() : null;
            string animatorText = "animator=none";
            if (animator)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                animatorText = $"enabled={animator.enabled} active={animator.isActiveAndEnabled} init={animator.isInitialized} culling={animator.cullingMode} speed={animator.speed:F2} layers={animator.layerCount} state={info.shortNameHash} t={info.normalizedTime:F2} underwater={animator.GetBool("is_underwater")} moveSpeed={animator.GetFloat("move_speed"):F2} viewPitch={animator.GetFloat("view_pitch"):F1}";
            }
            int visible = 0;
            int total = 0;
            foreach (SkinnedMeshRenderer renderer in remote.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                total++;
                visible += renderer.isVisible ? 1 : 0;
            }
            Transform left = remote.PlayerModel ? VuFindDeep(remote.PlayerModel.transform, "hand_L") : null;
            Transform right = remote.PlayerModel ? VuFindDeep(remote.PlayerModel.transform, "hand_R") : null;
            float span = left && right ? Vector3.Distance(left.position, right.position) : -1f;
            float handsAbove = left && right ? (left.position.y + right.position.y) / 2f - remote.Body.transform.position.y : float.NaN;
            Vector3 view = camera ? camera.WorldToViewportPoint(remote.Body.transform.position) : Vector3.zero;
            Log.Info($"{VU}vumodel: remotepose player={remote.PlayerName} {animatorText} renderersVisible={visible}/{total} handSpan={span:F2} handsAbove={handsAbove:F2} at={PlayerEmoteCamera.Compact(remote.Body.transform.position)} animating={(remote.AnimationController ? remote.AnimationController.UpdatePlayerAnimations : false)} firstPersonViewport={view.x:F2},{view.y:F2},{view.z:F2} apart={(camera ? Vector3.Distance(camera.transform.position, remote.Body.transform.position) : -1f):F2}");
        }
        if (count == 0)
        {
            Log.Info($"{VU}vumodel: remotepose none");
        }
    }

    /// <summary>
    ///     <c>vumodel tags</c>: every marker that the game's HUD shows now for a ping (the name tag of a remote player is the game's own <c>PingInstance</c> marker, and so is a quick ping): its label, distance, the canvas that
    ///     draws it and how it is turned and scaled (a mirrored label has a negative scale or is turned half a round from the camera that draws it), and every text in the world (not on the screen) that carries a name.
    /// </summary>
    private static void PlayerModelTags()
    {
        Camera camera = MainCamera.camera;
        int shown = 0;
        foreach (uGUI_Ping ping in Resources.FindObjectsOfTypeAll<uGUI_Ping>())
        {
            if (!ping || !ping.gameObject.activeInHierarchy)
            {
                continue;
            }
            shown++;
            RectTransform rect = ping.rectTransform;
            Canvas canvas = ping.GetComponentInParent<Canvas>();
            string canvasText = canvas ? $"{canvas.renderMode}/{(canvas.worldCamera ? canvas.worldCamera.name : "none")}" : "none";
            Log.Info($"{VU}vumodel: tags ping label='{(ping.infoText ? ping.infoText.text : "?")}' distance='{(ping.distanceText ? ping.distanceText.text : "?")}' pos={PlayerEmoteCamera.Compact(rect.position)} anchored={rect.anchoredPosition.x:F0},{rect.anchoredPosition.y:F0} lossyScale={PlayerEmoteCamera.Compact(rect.lossyScale)} euler={PlayerEmoteCamera.Compact(rect.eulerAngles)} canvas={canvasText} cameraPos={(camera ? PlayerEmoteCamera.Compact(camera.transform.position) : "none")} path={VuPathOf(ping.transform)}");
        }
        // the name and the stats that hang above a remote player (RemotePlayerVitals): the way their canvas faces, against the way the camera that draws them looks (the emote camera while it is out, else the head's)
        bool emoteOut = PlayerEmoteDriver.TryGetCameraForward(out Vector3 emoteForward);
        Vector3 reference = emoteOut ? emoteForward : camera ? camera.transform.forward : Vector3.forward;
        foreach (NitroxClient.MonoBehaviours.Gui.HUD.RemotePlayerVitals vitals in FindObjectsOfType<NitroxClient.MonoBehaviours.Gui.HUD.RemotePlayerVitals>())
        {
            Canvas vitalsCanvas = vitals.GetComponent<Canvas>();
            if (!vitalsCanvas)
            {
                continue;
            }
            Log.Info($"{VU}vumodel: tags vitals canvasForward={PlayerEmoteCamera.Compact(vitalsCanvas.transform.forward)} reference={(emoteOut ? "emote" : "head")} referenceForward={PlayerEmoteCamera.Compact(reference)} facing={Vector3.Dot(vitalsCanvas.transform.forward, reference):F2} active={vitalsCanvas.gameObject.activeInHierarchy} at={PlayerEmoteCamera.Compact(vitalsCanvas.transform.position)}");
        }
        int world = 0;
        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!text || !text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text))
            {
                continue;
            }
            Canvas canvas = text.canvas;
            if (canvas && canvas.renderMode != RenderMode.WorldSpace)
            {
                continue;
            }
            if (world++ >= 12)
            {
                break;
            }
            Log.Info($"{VU}vumodel: tags worldText='{text.text.Replace('\n', '|')}' pos={PlayerEmoteCamera.Compact(text.transform.position)} lossyScale={PlayerEmoteCamera.Compact(text.transform.lossyScale)} euler={PlayerEmoteCamera.Compact(text.transform.eulerAngles)} path={VuPathOf(text.transform)}");
        }
        Log.Info($"{VU}vumodel: tags done pings={shown} worldTexts={world} cameraForward={(camera ? PlayerEmoteCamera.Compact(camera.transform.forward) : "none")}");
    }

    /// <summary>
    ///     <c>vumodel camstate</c>: what the first-person view and the emote camera of this client look like now, as plain tokens: where the camera and the player are, what the camera control, the pause menu lock, the hand and
    ///     the tool are doing, how many of the meshes of the local first-person body are drawn, and whether the copy of the model is on the body. The live check reads it before, during and after an own emote.
    /// </summary>
    private static void PlayerModelCamState()
    {
        Camera camera = MainCamera.camera;
        Player player = Player.main;
        MainCameraControl control = MainCameraControl.main;
        FPSInputModule input = FPSInputModule.current;
        int shown = 0;
        int total = 0;
        try
        {
            GameObject view = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerModel;
            if (view)
            {
                foreach (SkinnedMeshRenderer renderer in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    total++;
                    shown += renderer.enabled && !renderer.forceRenderingOff ? 1 : 0;
                }
            }
        }
        catch (Exception exception)
        {
            Log.Info($"{VU}vumodel: camstate the body could not be read: {exception.Message}");
        }
        PlayerTool held = Inventory.main != null ? Inventory.main.GetHeldTool() : null;
        Vector3 cameraAt = camera ? camera.transform.position : Vector3.zero;
        Vector3 playerAt = player ? player.transform.position : Vector3.zero;
        Log.Info($"{VU}vumodel: camstate cameraPos={PlayerEmoteCamera.Compact(cameraAt)} playerPos={PlayerEmoteCamera.Compact(playerAt)} apart={Vector3.Distance(cameraAt, playerAt):F2} mouseLook={(control && control.mouseLookEnabled)} pauseLock={(input && input.lockPauseMenu)} hand={(player && player.guiHand && player.guiHand.enabled)} held={(held ? held.name : "none")} slot={(Inventory.main != null && Inventory.main.quickSlots != null ? Inventory.main.quickSlots.activeSlot : -2)} bodyRenderers={shown}/{total} copy={(CustomPlayerModels.Local ? CustomPlayerModels.Local.ModelId : "none")} cameraOut={PlayerEmoteDriver.CameraOut} underwater={(player && player.IsUnderwater())} menu={(IngameMenu.main && IngameMenu.main.selected)} inRoom={(player && player.currentSub)} look={(control ? control.rotationX.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "none")},{(control ? control.rotationY.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "none")} {PlayerEmoteDriver.StateText()}");
    }

    /// <summary>
    ///     <c>vumodel wheel state</c> says what the emote wheel of this player is doing; <c>vumodel wheel N</c> makes the next wheel start with the pointer on the sector N (from 0 at the top), because a live check can
    ///     hold the real key but cannot move the real mouse; <c>vumodel wheel shot</c> takes a picture half a second after the next wheel opens.
    /// </summary>
    private void PlayerModelWheel(string argument)
    {
        if (argument == "state")
        {
            Log.Info($"{VU}vumodel: wheel {PlayerEmoteDriver.StateText()}");
            return;
        }
        if (argument == "shot")
        {
            StartCoroutine(PlayerModelWheelShot());
            Log.Info($"{VU}vumodel: wheel shot armed");
            return;
        }
        if (int.TryParse(argument, out int sector))
        {
            PlayerEmoteDriver.DebugPresetSector = sector;
            Log.Info($"{VU}vumodel: wheel preset sector={sector}");
            return;
        }
        Log.Info($"{VU}vumodel: wheel outcome=UnknownArgument");
    }

    private IEnumerator PlayerModelWheelShot()
    {
        float until = Time.unscaledTime + 90f;
        while (!PlayerEmoteDriver.WheelOpen && Time.unscaledTime < until)
        {
            yield return null;
        }
        if (!PlayerEmoteDriver.WheelOpen)
        {
            Log.Info($"{VU}vumodel: wheel shot outcome=NeverOpened");
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.5f);
        if (liveShotBusy)
        {
            Log.Info($"{VU}vumodel: wheel shot outcome=Busy");
            yield break;
        }
        StartCoroutine(LiveShotRoutine(false, 0f, "emote-wheel"));
    }

    /// <summary><c>vumodel hurt</c>: this player loses ten health (never below one), which is what stops an emote that he plays.</summary>
    private static void PlayerModelHurt()
    {
        if (!Player.main || !Player.main.liveMixin)
        {
            Log.Info($"{VU}vumodel: hurt outcome=NoPlayer");
            return;
        }
        LiveMixin mixin = Player.main.liveMixin;
        mixin.health = Mathf.Max(1f, mixin.health - 10f);
        Log.Info($"{VU}vumodel: hurt health={mixin.health:F1}");
    }

    /// <summary>
    ///     <c>vumodel state</c>: what the first remote player's body looks like to the code now: the model he wears, the culling mode of his Animator, the number of vanilla meshes that are enabled and which
    ///     transform the items he holds hang on (the check that taking the model off gave the vanilla body back).
    /// </summary>
    private static void PlayerModelState()
    {
        RemotePlayer player = PlayerModelFirstRemotePlayer();
        if (player == null)
        {
            Log.Info($"{VU}vumodel: state outcome=NoTeammate");
            return;
        }
        Animator animator = player.PlayerModel ? player.PlayerModel.GetComponent<Animator>() : null;
        int enabledMeshes = 0;
        foreach (Renderer candidate in player.PlayerModel.GetComponentsInChildren<Renderer>(true))
        {
            if ((candidate is SkinnedMeshRenderer || candidate is MeshRenderer) && candidate.enabled)
            {
                enabledMeshes++;
            }
        }
        CustomPlayerModel model = CustomPlayerModels.Of(player);
        Log.Info($"{VU}vumodel: state worn={(model ? model.ModelId : "none")} animatorCulling={(animator ? animator.cullingMode.ToString() : "none")} enabledVanillaMeshes={enabledMeshes} itemAttach={(player.ItemAttachPoint ? player.ItemAttachPoint.name : "none")}");
    }

    /// <summary>
    ///     <c>vumodel perf</c>: what the pose copy cost per frame since the last call (the stopwatch around the first onPreCull of every frame), and the time of 2000 pose copies in a row for the models that are worn
    ///     now (so that the cost of four remote players can be read off: the budget is 0.2 ms).
    /// </summary>
    private static void PlayerModelPerf()
    {
        PoseCopyStats stats = CustomPlayerModels.Stats;
        int worn = CustomPlayerModels.Worn.Count;
        Log.Info($"{VU}vumodel: perf live worn={worn} frames={stats.Frames} modelFrames={stats.ModelFrames} averageMicroseconds={stats.AverageMicroseconds:F1} maxMicroseconds={stats.MaxMicroseconds:F1}");
        stats.Reset();
        if (worn == 0)
        {
            return;
        }
        const int CALLS = 2000;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < CALLS; i++)
        {
            CustomPlayerModels.PoseAll();
        }
        double microseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency / CALLS;
        Log.Info($"{VU}vumodel: perf bench calls={CALLS} worn={worn} microsecondsPerCall={microseconds:F1} microsecondsPerModel={microseconds / worn:F1} fourModels={microseconds / worn * 4:F1} budget=200");
    }

    private void PlayerModelClear()
    {
        foreach (GameObject copy in spawnedPlayerModels)
        {
            if (copy)
            {
                Destroy(copy);
            }
        }
        spawnedPlayerModels.Clear();
    }

    private void PlayerModelSpawn(string id, string where)
    {
        Camera camera = Camera.main;
        if (!camera || !Player.main)
        {
            Log.Info($"{VU}vumodel: spawn outcome=NoCamera");
            return;
        }
        if (id.Length == 0)
        {
            id = PlayerModelLibrary.Ids.FirstOrDefault() ?? "";
        }
        LoadedPlayerModel model = PlayerModelLibrary.Load(id);
        if (model == null)
        {
            Log.Info($"{VU}vumodel: spawn outcome=NoModel id={id}");
            return;
        }
        Material source = PlayerModelBuilder.SuitMaterial();
        if (!source)
        {
            Log.Info($"{VU}vumodel: spawn outcome=NoMaterial id={id}");
            return;
        }

        Vector3 eye = camera.transform.position;
        Vector3 ahead = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        if (ahead.sqrMagnitude < 0.01f)
        {
            ahead = Vector3.forward;
        }
        Vector3 feet = eye + ahead * 3f + Vector3.down * 1.6f;
        Vector3 lookAt = feet + Vector3.up * 1f;
        string kind = "ahead";
        if (where == "ground")
        {
            Vector3 above = eye + ahead * 4f;
            kind = Physics.Raycast(above, Vector3.down, out RaycastHit floor, 80f, ~0, QueryTriggerInteraction.Ignore) ? "ground" : "ground-missed";
            feet = kind == "ground" ? floor.point : above + Vector3.down * 1.6f;
            lookAt = feet + Vector3.up * 1f;
        }
        else if (where == "beside")
        {
            RemotePlayer teammate = NitroxServiceLocator.LocateService<PlayerManager>().GetAll().FirstOrDefault(player => player.Body != null);
            if (teammate == null)
            {
                Log.Info($"{VU}vumodel: spawn outcome=NoTeammate");
                return;
            }
            Vector3 center = teammate.Collider ? teammate.Collider.bounds.center : teammate.Body.transform.position;
            float bottom = teammate.Collider ? teammate.Collider.bounds.min.y : center.y - 0.9f;
            Vector3 toTeammate = Vector3.ProjectOnPlane(center - eye, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toTeammate).normalized;
            feet = new Vector3(center.x, bottom, center.z) + side * 1.3f;
            lookAt = (new Vector3(center.x, bottom + 0.9f, center.z) + feet + Vector3.up * 0.9f) / 2f;
            kind = "beside";
        }

        GameObject copy = PlayerModelBuilder.Instantiate(model, null, source);
        Vector3 toCamera = Vector3.ProjectOnPlane(eye - feet, Vector3.up);
        copy.transform.SetPositionAndRotation(feet, toCamera.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCamera.normalized) : Quaternion.identity);
        SkyApplier applier = copy.AddComponent<SkyApplier>();
        applier.anchorSky = Skies.Auto;
        applier.emissiveFromPower = false;
        applier.dynamic = true;
        applier.renderers = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        spawnedPlayerModels.Add(copy);

        if (where.Length > 0 && MainCameraControl.main)
        {
            Vector3 toTarget = lookAt - eye;
            MainCameraControl.main.rotationX = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            MainCameraControl.main.rotationY = -Mathf.Asin(-toTarget.y / toTarget.magnitude) * Mathf.Rad2Deg;
        }
        SkinnedMeshRenderer renderer = copy.GetComponentInChildren<SkinnedMeshRenderer>();
        Log.Info($"{VU}vumodel: spawned id={id} kind={kind} at={feet.ToString("F1")} height={model.Height:F2} bones={renderer.bones.Length} vertices={model.Glb.VertexCount} shader={renderer.sharedMaterial.shader.name} boundsSize={renderer.bounds.size.ToString("F2")} distance={Vector3.Distance(eye, feet):F1}");
    }

    private static void PlayerModelMaterial()
    {
        Material material = PlayerModelBuilder.SuitMaterial();
        if (!material)
        {
            Log.Info($"{VU}vumodel: material outcome=NoMaterial");
            return;
        }
        Shader shader = material.shader;
        Log.Info($"{VU}vumodel: material name={material.name} shader={shader.name} keywords={string.Join("|", material.shaderKeywords)} properties={shader.GetPropertyCount()}");
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string name = shader.GetPropertyName(i);
            UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
            string value = type switch
            {
                UnityEngine.Rendering.ShaderPropertyType.Texture => material.GetTexture(name) ? material.GetTexture(name).name : "none",
                UnityEngine.Rendering.ShaderPropertyType.Color => material.GetColor(name).ToString("F2"),
                UnityEngine.Rendering.ShaderPropertyType.Vector => material.GetVector(name).ToString("F2"),
                _ => material.GetFloat(name).ToString("F3")
            };
            Log.Info($"{VU}vumodel: property {name} type={type} value={value}");
        }
    }

    private static void PlayerModelDump()
    {
        GameObject prototype = NitroxServiceLocator.LocateService<LocalPlayer>().BodyPrototype;
        Transform view = prototype ? prototype.transform.Find("player_view") : null;
        if (!view)
        {
            Log.Info($"{VU}vumodel: dump outcome=NoBody");
            return;
        }
        Animator animator = view.GetComponent<Animator>();
        Log.Info($"{VU}vumodel: animator found={(animator ? "yes" : "no")} cullingMode={(animator ? animator.cullingMode.ToString() : "none")} updateMode={(animator ? animator.updateMode.ToString() : "none")} applyRootMotion={(animator && animator.applyRootMotion)} controller={(animator && animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "none")}");

        int bones = 0;
        void Walk(Transform t, string path)
        {
            bones++;
            Log.Info($"{VU}vumodel: bone path={path} localPos={t.localPosition.ToString("F3")} localRot={t.localRotation.ToString("F3")} localScale={t.localScale.ToString("F3")} children={t.childCount}");
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                Walk(child, $"{path}/{child.name}");
            }
        }
        Transform skeleton = view.Find("export_skeleton");
        if (skeleton)
        {
            Walk(skeleton, "export_skeleton");
        }
        Log.Info($"{VU}vumodel: skeleton bones={bones}");

        SkinnedMeshRenderer biggest = null;
        foreach (SkinnedMeshRenderer renderer in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Log.Info($"{VU}vumodel: smr name={renderer.name} bones={renderer.bones.Length} rootBone={(renderer.rootBone ? renderer.rootBone.name : "none")} vertices={(renderer.sharedMesh ? renderer.sharedMesh.vertexCount : 0)} active={renderer.gameObject.activeSelf}");
            if (renderer.sharedMesh && (!biggest || renderer.bones.Length > biggest.bones.Length))
            {
                biggest = renderer;
            }
        }
        if (biggest)
        {
            Log.Info($"{VU}vumodel: bind of {biggest.name}: bone positions and rotations at bind, relative to player_view");
            Matrix4x4 toView = view.worldToLocalMatrix;
            Matrix4x4[] bindPoses = biggest.sharedMesh.bindposes;
            for (int i = 0; i < biggest.bones.Length && i < bindPoses.Length; i++)
            {
                if (!biggest.bones[i])
                {
                    continue;
                }
                Matrix4x4 bindWorld = biggest.transform.localToWorldMatrix * bindPoses[i].inverse;
                Matrix4x4 relative = toView * bindWorld;
                Vector3 position = relative.GetColumn(3);
                Log.Info($"{VU}vumodel: bind bone={biggest.bones[i].name} pos={position.ToString("F3")} rot={relative.rotation.ToString("F3")}");
            }
        }
        Log.Info($"{VU}vumodel: dump done");
    }
}
#endif
