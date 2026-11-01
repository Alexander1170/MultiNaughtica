using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;
using NitroxClient.MonoBehaviours.Gui.Input;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// The emote wheel of this player (docs/coop/PLAYER_MODELS.md): hold the emote key (G), move the mouse towards an emote on the wheel, let go to play it. The others see the clip on the custom model that this
/// player wears (<see cref="PlayerEmote" />: a start with the name of the clip, a stop when it is cancelled); he does not see himself (no third-person camera). While the wheel is open the camera is held (the
/// game's own <c>FPSInputModule.lockRotation</c>) and the look input moves a pointer on the wheel instead; the wheel is drawn with the legacy GUI, which needs no canvas. An emote stops when the player moves,
/// loses health, presses the key again, or gets into a vehicle, a seat, the PDA or a menu; it is refused in those places to begin with. The wheel only exists for a player who chose a custom model that has
/// emotes. Whatever goes wrong in here is logged and the wheel is off: it never breaks the game.
/// </summary>
internal sealed class PlayerEmoteDriver : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Wheel,
        Playing
    }

    /// <summary>How far the pointer moves for one unit of the look input; the radius of the wheel is 1, so a few millimetres of mouse choose a sector.</summary>
    private const float POINTER_GAIN = 0.05f;

    private const float NOTICE_SECONDS = 2.5f;

    /// <summary>A player who just started an emote and still holds a movement key does not cancel it at once.</summary>
    private const float MOVE_GRACE_SECONDS = 0.35f;

    private static PlayerEmoteDriver instance;
    private static GameInput.Button? emoteButton;
    private static bool buttonBroken;

    private IPacketSender packetSender;
    private LocalPlayer localPlayer;
    private PlayerEmoteGui gui;
    private readonly PlayerEmoteCamera view = new();
    private Phase phase;
    private IReadOnlyList<EmoteEntry> entries = Array.Empty<EmoteEntry>();
    private Vector2 pointer;
    private float peakPointer;
    private int highlighted = -1;
    private bool rotationLocked;
    private EmoteEntry playing;
    private float playStart;
    private float lastHealth;
    private float lastStartAt = -1000f;
    private string notice = "";
    private float noticeUntil;
    private int errors;
    private bool wasHeld;
    private GUIStyle labelStyle;

#if DEBUG
    /// <summary>A Debug aid: the sector (from 0 at the top) that the next wheel starts on, -1 for none. The live check holds a real key but cannot move a real mouse.</summary>
    internal static int DebugPresetSector = -1;
#endif

    /// <summary>Whether the wheel is open now.</summary>
    internal static bool WheelOpen => instance && instance.phase == Phase.Wheel;

    /// <summary>What the Debug command <c>vumodel wheel state</c> says.</summary>
    internal static string StateText()
    {
        if (!instance)
        {
            return "phase=NoDriver";
        }
        return $"phase={instance.phase} highlighted={instance.highlighted} entries={instance.entries.Count} playing={(instance.playing != null ? instance.playing.Id : "none")} pointer=({instance.pointer.x:F2},{instance.pointer.y:F2}) rotationLocked={instance.rotationLocked} errors={instance.errors} {instance.view.StateText()}";
    }

    /// <summary>Whether the third-person view of the player's own emote is out now.</summary>
    internal static bool CameraOut => instance && instance.view.Active;

    /// <summary>
    /// The way the emote camera looked in its last rendering while it is out (<see cref="PlayerEmoteCamera.TryGetRenderForward" />): the name and the stats above a remote player turn to it instead of to the head's
    /// camera (<see cref="EmoteCameraRules.BillboardForward" />).
    /// </summary>
    internal static bool TryGetCameraForward(out Vector3 forward)
    {
        forward = default;
        return instance && instance.view.TryGetRenderForward(out forward);
    }

#if DEBUG
    /// <summary>
    /// Debug (the live check, which holds real keys but cannot move a real mouse): starts the emote of this id as if it had been picked on the wheel (the camera goes out with it), or says why not. The answer is plain
    /// tokens for the log line.
    /// </summary>
    internal static string DebugStartEmote(string id, bool forever = false)
    {
        if (!instance)
        {
            return "outcome=NoDriver";
        }
        DebugForever = forever;
        string outcome = instance.DebugStart(id);
        if (!outcome.StartsWith("outcome=Started", StringComparison.Ordinal))
        {
            DebugForever = false;
        }
        return outcome;
    }

    /// <summary>Debug (the live check, whose commands take seconds each): the emote of this player does not end by itself and its clip goes round, until it is stopped (<c>vumodel holdemote</c>, <c>vumodel selfemote stop</c>).</summary>
    internal static bool DebugForever;

    /// <summary>Debug (the live check with the real mouse): the next emote that starts, picked on the wheel with the real mouse too, is a <see cref="DebugForever" /> one (<c>vumodel forever</c>).</summary>
    internal static bool DebugForeverNext;

    /// <summary>Debug: ends the emote that this player plays, as if he had pressed the key again (the stop goes to the others). The answer is plain tokens.</summary>
    internal static string DebugStopEmote()
    {
        if (!instance || instance.phase != Phase.Playing)
        {
            return "outcome=NoneRunning";
        }
        instance.EndOwn(true, "the Debug command");
        return "outcome=Stopped";
    }

    /// <summary>Debug: turns the camera of the emote view to a yaw, a pitch (degrees) and a distance (metres), as the mouse and the wheel would.</summary>
    internal static string DebugOrbit(float yaw, float pitch, float distance)
    {
        if (!instance || !instance.view.Active)
        {
            return "outcome=CameraIn";
        }
        instance.view.DebugSet(yaw, pitch, distance);
        return $"outcome=Set {instance.view.StateText()}";
    }

    /// <summary>Debug: what the sweeps of the emote camera see along a yaw and a pitch from the character (<see cref="PlayerEmoteCamera.DebugProbe" />).</summary>
    internal static string DebugProbe(float yaw, float pitch, float distance)
    {
        return instance ? instance.view.DebugProbe(yaw, pitch, distance) : "outcome=NoDriver";
    }

    private string DebugStart(string id)
    {
        if (phase != Phase.Idle)
        {
            return $"outcome=Busy phase={phase}";
        }
        string saved = PlayerModelSelection.SavedId;
        IReadOnlyList<EmoteEntry> list = saved.Length > 0 ? PlayerModelLibrary.EmotesOf(saved) : Array.Empty<EmoteEntry>();
        if (list == null)
        {
            return "outcome=EmotesNotRead";
        }
        string refusal = PlayerEmoteRules.Refusal(list.Count > 0, InVehicle(), Player.main.sitting, Dead(), MenuOpen());
        if (refusal != null)
        {
            return $"outcome=Refused reason=\"{refusal}\"";
        }
        EmoteEntry entry = null;
        foreach (EmoteEntry candidate in list)
        {
            if (string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
            }
        }
        if (entry == null)
        {
            return $"outcome=NoSuchEmote ids={string.Join("|", System.Linq.Enumerable.Select(list, candidate => candidate.Id))}";
        }
        lastStartAt = float.NegativeInfinity;
        StartEmote(entry);
        return $"outcome=Started id={entry.Id} {view.StateText()}";
    }
#endif

    private void Awake()
    {
        instance = this;
        packetSender = this.Resolve<IPacketSender>();
        localPlayer = this.Resolve<LocalPlayer>();
        gui = gameObject.AddComponent<PlayerEmoteGui>();
        gui.Driver = this;
        gui.enabled = false;
        WarmUpModel();
    }

    private void OnDestroy()
    {
        view.End("the emote driver was destroyed");
        view.Settle(true);
        ReleaseRotation();
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// Reads the model that this player chose in the background (the file, the mesh and the textures, over several frames): the emote camera shows the character at once when an emote starts, instead of stopping the
    /// game for the few hundred milliseconds that the first read takes. Nothing happens for the vanilla body or when the model is read already.
    /// </summary>
    private static void WarmUpModel()
    {
        try
        {
            string saved = PlayerModelSelection.SavedId;
            if (saved.Length > 0 && !PlayerModelLibrary.IsLoaded(saved) && !PlayerModelLibrary.HasFailed(saved))
            {
                PlayerModelLibrary.LoadAsync(saved, _ => { });
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the model could not be warmed up for the emote camera: {exception.Message}");
        }
    }

    private void Update()
    {
        view.Settle(false);
        if (errors > 5)
        {
            return;
        }
        try
        {
            Tick();
            bool drawing = phase != Phase.Idle || Time.unscaledTime < noticeUntil;
            if (gui.enabled != drawing)
            {
                gui.enabled = drawing;
            }
        }
        catch (Exception exception)
        {
            errors++;
            Log.Error(exception, $"[PlayerModels] the emote wheel failed ({errors})");
            Abort();
            gui.enabled = false;
            if (errors > 5)
            {
                Log.Warn("[PlayerModels] the emote wheel is off for the rest of this session");
            }
        }
    }

    private void Tick()
    {
        Player player = Player.main;
        if (!Multiplayer.Active || !player || !TryGetButton(out GameInput.Button button))
        {
            wasHeld = false;
            if (phase != Phase.Idle)
            {
                Abort();
            }
            return;
        }

        // only GetButtonHeld is asked for (it is what the share key proved to work for the buttons that Nitrox adds): the press is the frame in which it starts to be held
        bool held = GameInput.GetButtonHeld(button);
        bool down = held && !wasHeld;
        wasHeld = held;
        switch (phase)
        {
            case Phase.Idle:
                if (down)
                {
                    OnKeyDown();
                }
                break;
            case Phase.Wheel:
                TickWheel(held);
                break;
            case Phase.Playing:
                TickPlaying(down);
                break;
        }
    }

    private static bool TryGetButton(out GameInput.Button button)
    {
        button = GameInput.Button.None;
        if (buttonBroken || !AvatarInputHandler.main || !AvatarInputHandler.main.IsEnabled())
        {
            return false;
        }
        try
        {
            emoteButton ??= (GameInput.Button)(KeyBindingManager.NITROX_BASE_ID + KeyBindingManager.KeyBindings.FindIndex(binding => binding is EmoteKeyBindingAction));
            button = emoteButton.Value;
            return true;
        }
        catch (Exception exception)
        {
            buttonBroken = true;
            Log.Error(exception, "[PlayerModels] the emote key could not be read; the key is off for this session");
            return false;
        }
    }

    private void OnKeyDown()
    {
        WarmUpModel();
        string saved = PlayerModelSelection.SavedId;
        IReadOnlyList<EmoteEntry> list = saved.Length > 0 ? PlayerModelLibrary.EmotesOf(saved) : Array.Empty<EmoteEntry>();
        if (list == null)
        {
            Notify("reading the emotes of your model, try again in a moment");
            return;
        }
        string refusal = PlayerEmoteRules.Refusal(list.Count > 0, InVehicle(), Player.main.sitting, Dead(), MenuOpen());
        if (refusal != null)
        {
            Notify(refusal);
            Log.Info($"[PlayerModels] no emote wheel: {refusal}");
            return;
        }
        OpenWheel(list);
    }

    private void OpenWheel(IReadOnlyList<EmoteEntry> list)
    {
        entries = list;
        phase = Phase.Wheel;
        pointer = Vector2.zero;
        peakPointer = 0f;
#if DEBUG
        if (DebugPresetSector >= 0 && DebugPresetSector < list.Count)
        {
            PlayerEmoteRules.SectorCenter(DebugPresetSector, list.Count, 0.7f, out float presetX, out float presetY);
            pointer = new Vector2(presetX, presetY);
        }
        DebugPresetSector = -1;
#endif
        highlighted = PlayerEmoteRules.SectorAt(pointer.x, pointer.y, entries.Count);
        if (FPSInputModule.current)
        {
            FPSInputModule.current.lockRotation = true;
            rotationLocked = true;
        }
        Log.Info($"[PlayerModels] the emote wheel is open with {entries.Count} emotes");
    }

    private void TickWheel(bool held)
    {
        string refusal = PlayerEmoteRules.Refusal(true, InVehicle(), Player.main.sitting, Dead(), MenuOpen());
        if (refusal != null)
        {
            CloseWheel();
            Notify(refusal);
            return;
        }
        pointer = Vector2.ClampMagnitude(pointer + GameInput.GetLookDelta() * POINTER_GAIN, 1f);
        peakPointer = Mathf.Max(peakPointer, pointer.magnitude);
        highlighted = PlayerEmoteRules.SectorAt(pointer.x, pointer.y, entries.Count);
        if (held)
        {
            return;
        }
        Log.Info($"[PlayerModels] the emote wheel was let go with the pointer at ({pointer.x:F2}, {pointer.y:F2}), at most {peakPointer:F2} from the middle, on the sector {highlighted}");
        EmoteEntry chosen = highlighted >= 0 && highlighted < entries.Count ? entries[highlighted] : null;
        CloseWheel();
        if (chosen != null)
        {
            StartEmote(chosen);
        }
        else
        {
            Log.Info("[PlayerModels] the emote wheel was closed without a choice");
        }
    }

    private void CloseWheel()
    {
        ReleaseRotation();
        phase = Phase.Idle;
        highlighted = -1;
    }

    private void StartEmote(EmoteEntry entry)
    {
        float now = Time.unscaledTime;
        if (now - lastStartAt < (float)PlayerEmoteRules.CLIENT_START_INTERVAL_SECONDS)
        {
            Notify("not so fast");
            return;
        }
        lastStartAt = now;
        SendEmote(entry.Id, true);
        playing = entry;
        playStart = Time.time;
        lastHealth = HealthNow();
        phase = Phase.Playing;
        Log.Info($"[PlayerModels] this player starts the emote '{entry.Id}' ({entry.Duration:F2} s{(entry.Loop ? ", repeated" : "")})");
#if DEBUG
        if (DebugForeverNext)
        {
            DebugForever = true;
            DebugForeverNext = false;
        }
#endif
        StartView(entry);
    }

    /// <summary>
    /// The third-person view of the emote that just started (<see cref="PlayerEmoteCamera" />). When it cannot be taken out (no model, no camera) the emote goes on in first person, as it did before the camera
    /// existed, and the reason is in the log.
    /// </summary>
    private void StartView(EmoteEntry entry)
    {
        string saved = PlayerModelSelection.SavedId;
        if (saved.Length == 0)
        {
            return;
        }
        if (!view.Begin(saved, entry.Id, out string failure))
        {
            Log.Warn($"[PlayerModels] no emote camera for '{entry.Id}': {failure}; the emote plays without it");
        }
    }

    private void TickPlaying(bool down)
    {
        float elapsed = Time.time - playStart;
        bool holdOn = false;
#if DEBUG
        holdOn = DebugForever;
#endif
        if (!holdOn && PlayerEmoteRules.IsOver(elapsed, playing.Duration))
        {
            EndOwn(false, "it is over");
            return;
        }
        // the orbit follows the mouse; a view that has to go (the camera failed, the watchdog ran out) goes here and the emote goes on in first person
        view.Tick();
        float health = HealthNow();
        float lost = Math.Max(0f, lastHealth - health);
        lastHealth = health;
        string reason = null;
        if (down)
        {
            reason = "he pressed the key again";
        }
        else
        {
            reason = PlayerEmoteRules.Refusal(true, InVehicle(), Player.main.sitting, Dead(), MenuOpen());
            if (reason == null)
            {
                if (GameInput.GetButtonDown(GameInput.Button.Exit))
                {
                    reason = "he pressed the exit key";
                }
                else if (GameInput.GetButtonDown(GameInput.Button.UIMenu))
                {
                    reason = "he pressed the menu key";
                }
            }
            if (reason == null)
            {
                float moved = elapsed < MOVE_GRACE_SECONDS ? 0f : GameInput.GetMoveDirection().magnitude;
                reason = PlayerEmoteRules.CancelReason(moved, lost);
            }
        }
        if (reason != null)
        {
            EndOwn(true, reason);
        }
    }

    private void EndOwn(bool sendStop, string reason)
    {
        Log.Info($"[PlayerModels] this player's emote '{(playing != null ? playing.Id : "?")}' ends: {reason}");
        if (sendStop)
        {
            SendEmote(string.Empty, false);
        }
        view.End(reason);
#if DEBUG
        DebugForever = false;
#endif
        playing = null;
        phase = Phase.Idle;
    }

    /// <summary>Something is wrong (no session, no player, the key cannot be read, an error): everything is put back and nobody is left waiting for a stop.</summary>
    private void Abort()
    {
        bool wasPlaying = phase == Phase.Playing;
        view.End("the emote was aborted");
#if DEBUG
        DebugForever = false;
#endif
        ReleaseRotation();
        phase = Phase.Idle;
        highlighted = -1;
        playing = null;
        if (wasPlaying && Multiplayer.Active)
        {
            try
            {
                SendEmote(string.Empty, false);
            }
            catch (Exception exception)
            {
                Log.Warn($"[PlayerModels] the stop of an emote could not be sent: {exception.Message}");
            }
        }
    }

    private void ReleaseRotation()
    {
        if (rotationLocked && FPSInputModule.current)
        {
            FPSInputModule.current.lockRotation = false;
        }
        rotationLocked = false;
    }

    private void SendEmote(string id, bool start)
    {
        SessionId session = localPlayer.SessionId ?? default;
        packetSender.Send(new PlayerEmote(session, id, start));
    }

    private void Notify(string text)
    {
        notice = text;
        noticeUntil = Time.unscaledTime + NOTICE_SECONDS;
    }

    private static float HealthNow()
    {
        Player player = Player.main;
        return player && player.liveMixin ? player.liveMixin.health : 0f;
    }

    private static bool InVehicle()
    {
        Player player = Player.main;
        return player.currentMountedVehicle || player.inSeamoth || player.inExosuit || player.isPiloting;
    }

    private static bool Dead()
    {
        Player player = Player.main;
        return !player.liveMixin || player.liveMixin.health <= 0f;
    }

    private static bool MenuOpen()
    {
        Player player = Player.main;
        PDA pda = player.GetPDA();
        return player.cinematicModeActive || (pda && pda.isInUse);
    }

    /// <summary>Called by <see cref="PlayerEmoteGui" /> in <c>OnGUI</c>: the wheel, or what the player is doing, or why he cannot.</summary>
    internal void Draw()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }
        float scale = Screen.height / 1080f;
        if (phase == Phase.Wheel && entries.Count > 0)
        {
            DrawWheel(scale);
        }
        else if (phase == Phase.Playing && playing != null)
        {
            DrawBottomText(view.Active
                ? $"Emote: {playing.Label}   (mouse: look round   wheel: closer or further   G, E, Esc or moving: stop)"
                : $"Emote: {playing.Label}   (move, or press the key again, to stop)", scale);
        }
        else if (Time.unscaledTime < noticeUntil)
        {
            DrawBottomText(notice, scale);
        }
    }

    private void DrawWheel(float scale)
    {
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        float radius = 240f * scale;
        int count = entries.Count;

        Rect box = new(centerX - radius, centerY - radius, radius * 2f, radius * 2f);
        GUI.DrawTexture(box, EmoteWheelArt.Ring(count));
        if (highlighted >= 0)
        {
            GUI.DrawTexture(box, EmoteWheelArt.Highlight(count, highlighted));
        }

        GUIStyle style = LabelStyle(Mathf.Max(10, Mathf.RoundToInt(17f * scale)));
        for (int i = 0; i < count; i++)
        {
            PlayerEmoteRules.SectorCenter(i, count, radius * 0.66f, out float x, out float y);
            Rect labelBox = new(centerX + x - 75f * scale, centerY - y - 22f * scale, 150f * scale, 44f * scale);
            Outlined(labelBox, entries[i].Label, style, i == highlighted ? Color.white : new Color(0.82f, 0.9f, 0.95f));
        }

        GUIStyle big = LabelStyle(Mathf.Max(12, Mathf.RoundToInt(22f * scale)));
        Rect middle = new(centerX - radius * 0.3f, centerY - 30f * scale, radius * 0.6f, 60f * scale);
        Outlined(middle, highlighted >= 0 ? string.Empty : "Cancel", big, new Color(1f, 0.85f, 0.4f));

        float dotX = centerX + pointer.x * radius;
        float dotY = centerY - pointer.y * radius;
        float dot = 12f * scale;
        GUI.DrawTexture(new Rect(dotX - dot * 0.5f, dotY - dot * 0.5f, dot, dot), EmoteWheelArt.Dot());

        Rect hint = new(centerX - 300f * scale, centerY + radius + 14f * scale, 600f * scale, 34f * scale);
        Outlined(hint, "Move the mouse to an emote and let go of the key; let go in the middle to cancel", LabelStyle(Mathf.Max(10, Mathf.RoundToInt(15f * scale))), new Color(0.82f, 0.9f, 0.95f));
    }

    private void DrawBottomText(string text, float scale)
    {
        Rect box = new(Screen.width * 0.5f - 400f * scale, Screen.height - 190f * scale, 800f * scale, 40f * scale);
        Outlined(box, text, LabelStyle(Mathf.Max(10, Mathf.RoundToInt(18f * scale))), Color.white);
    }

    private GUIStyle LabelStyle(int size)
    {
        labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            richText = false
        };
        labelStyle.fontSize = size;
        return labelStyle;
    }

    /// <summary>Text with a dark edge, so that it can be read over the sea as well as over a white wall.</summary>
    private static void Outlined(Rect box, string text, GUIStyle style, Color color)
    {
        style.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        foreach (Vector2 offset in new[] { new Vector2(-1.5f, 0f), new Vector2(1.5f, 0f), new Vector2(0f, -1.5f), new Vector2(0f, 1.5f) })
        {
            GUI.Label(new Rect(box.x + offset.x, box.y + offset.y, box.width, box.height), text, style);
        }
        style.normal.textColor = color;
        GUI.Label(box, text, style);
    }
}

/// <summary>Carries <c>OnGUI</c> for <see cref="PlayerEmoteDriver" />; it is enabled only while there is something to draw, so that the legacy GUI costs nothing the rest of the time.</summary>
internal sealed class PlayerEmoteGui : MonoBehaviour
{
    internal PlayerEmoteDriver Driver;

    private void OnGUI()
    {
        if (Driver)
        {
            Driver.Draw();
        }
    }
}

/// <summary>The pictures of the wheel, made from numbers the first time they are needed: the ring of sectors, the one sector that is chosen, and the dot of the pointer.</summary>
internal static class EmoteWheelArt
{
    private const int SIZE = 256;

    private static readonly Dictionary<int, Texture2D> rings = new();
    private static readonly Dictionary<int, Texture2D> highlights = new();
    private static Texture2D dot;

    public static Texture2D Ring(int count)
    {
        if (!rings.TryGetValue(count, out Texture2D texture) || !texture)
        {
            texture = Build(count, -1);
            rings[count] = texture;
        }
        return texture;
    }

    public static Texture2D Highlight(int count, int index)
    {
        int key = count * 100 + index;
        if (!highlights.TryGetValue(key, out Texture2D texture) || !texture)
        {
            texture = Build(count, index);
            highlights[key] = texture;
        }
        return texture;
    }

    public static Texture2D Dot()
    {
        if (!dot)
        {
            const int SMALL = 24;
            Color32[] pixels = new Color32[SMALL * SMALL];
            float half = SMALL * 0.5f;
            for (int py = 0; py < SMALL; py++)
            {
                for (int px = 0; px < SMALL; px++)
                {
                    float x = px + 0.5f - half;
                    float y = py + 0.5f - half;
                    float alpha = Mathf.Clamp01(half - 1f - Mathf.Sqrt(x * x + y * y));
                    pixels[py * SMALL + px] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            dot = new Texture2D(SMALL, SMALL, TextureFormat.RGBA32, false) { name = "EmoteWheel_dot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            dot.SetPixels32(pixels);
            dot.Apply(false, true);
        }
        return dot;
    }

    /// <summary>The ring of <paramref name="count" /> sectors (dark), or only the sector <paramref name="onlyIndex" /> of it (bright). Row 0 of a texture is its bottom, so y goes up as on the wheel.</summary>
    private static Texture2D Build(int count, int onlyIndex)
    {
        Color32[] pixels = new Color32[SIZE * SIZE];
        float half = SIZE * 0.5f;
        double width = 360.0 / Math.Max(count, 1);
        const double GAP_DEGREES = 1.5;
        float inner = PlayerEmoteRules.DEAD_ZONE;
        Color32 color = onlyIndex < 0 ? new Color32(14, 30, 46, 255) : new Color32(40, 190, 215, 255);
        float maxAlpha = onlyIndex < 0 ? 0.72f : 0.9f;
        for (int py = 0; py < SIZE; py++)
        {
            for (int px = 0; px < SIZE; px++)
            {
                float x = (px + 0.5f - half) / half;
                float y = (py + 0.5f - half) / half;
                float r = Mathf.Sqrt(x * x + y * y);
                float alpha = 0f;
                if (r > inner && r < 1f)
                {
                    double degrees = Math.Atan2(x, y) * 180.0 / Math.PI;
                    if (degrees < 0.0)
                    {
                        degrees += 360.0;
                    }
                    double shifted = degrees + width / 2.0;
                    int sector = (int)Math.Floor(shifted / width) % count;
                    double inside = shifted % width;
                    bool gap = inside < GAP_DEGREES || inside > width - GAP_DEGREES;
                    if (!gap && (onlyIndex < 0 || sector == onlyIndex))
                    {
                        // one pixel of soft edge on the rim and on the inner circle
                        alpha = Mathf.Clamp01(Mathf.Min(1f - r, r - inner) * half) * maxAlpha;
                    }
                }
                Color32 pixel = color;
                pixel.a = (byte)(alpha * 255f);
                pixels[py * SIZE + px] = pixel;
            }
        }
        Texture2D texture = new(SIZE, SIZE, TextureFormat.RGBA32, false) { name = $"EmoteWheel_{count}_{onlyIndex}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
