using NitroxClient.GameLogic.Gunner;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The screen of the gunner console (<see cref="GunnerConsole" />): the game's own input group, the way the PDA and the chat are, so that the walking and the looking of the player are locked while it is open, and
/// a canvas laid over the turret's view that draws the crosshair, the charge and the readouts (the power of the Cyclops, the cooldown, the turret's yaw and pitch, the range to what the shot would hit). The mouse
/// is read here: its movement turns the turret, a button held charges and released fires. The click that opened the console is ignored until the button has been released once.
/// </summary>
public class GunnerConsoleScreen : ConsoleInputGroup
{
    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const float MESSAGE_SECONDS = 3f;
    private const int IGNORE_FRAMES = 3;
    private const float BAR_WIDTH = 360f;

    private static readonly Color Clear = new(0.75f, 1f, 0.85f, 0.95f);
    private static readonly Color Busy = new(1f, 0.85f, 0.3f, 0.95f);

    private GunnerConsole console;
    private Text title;
    private Text powerLine;
    private Text aimLine;
    private Text rangeLine;
    private Text cooldownLine;
    private Text hint;
    private Text message;
    private Image chargeFill;
    private Image[] crosshair;
    private float messageUntil;
    private int openFrame = int.MaxValue;
    private bool shut;
    private bool armed;
    private bool charging;
    private float charge;
    private int loggedLookFrames;
    private int powerKey = int.MinValue;
    private int aimKey = int.MinValue;
    private int cooldownKey = int.MinValue;

    protected override string StationLogName => "gunner";

    /// <summary>The input group has the player: it was selected and nothing took it away.</summary>
    public bool IsFocused => focused;

    public float Charge => charge;
    public bool IsCharging => charging;

    public void Setup(GunnerConsole owner)
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

        title = MakeText("Title", 30, TextAnchor.UpperLeft, new Color(1f, 0.85f, 0.6f));
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(900f, 44f));
        title.text = "GUNNER STATION";
        powerLine = MakeText("Power", 24, TextAnchor.UpperLeft, Color.white);
        Anchor(powerLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -76f), new Vector2(900f, 34f));
        aimLine = MakeText("Aim", 24, TextAnchor.UpperLeft, Color.white);
        Anchor(aimLine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -108f), new Vector2(900f, 34f));

        // the crosshair: four bars round the middle and a dot, the middle of the screen is the aim
        crosshair = new Image[5];
        Vector2[] positions = [new(0f, 26f), new(0f, -26f), new(26f, 0f), new(-26f, 0f), Vector2.zero];
        Vector2[] sizes = [new(3f, 22f), new(3f, 22f), new(22f, 3f), new(22f, 3f), new(5f, 5f)];
        for (int i = 0; i < crosshair.Length; i++)
        {
            GameObject bar = new($"Cross{i}", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(transform, false);
            Anchor((RectTransform)bar.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), positions[i], sizes[i]);
            crosshair[i] = bar.GetComponent<Image>();
            crosshair[i].raycastTarget = false;
            crosshair[i].color = Clear;
        }
        rangeLine = MakeText("Range", 24, TextAnchor.MiddleLeft, Color.white);
        Anchor(rangeLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(520f, 34f));

        // the charge bar under the crosshair
        GameObject barBack = new("ChargeBack", typeof(RectTransform), typeof(Image));
        barBack.transform.SetParent(transform, false);
        Anchor((RectTransform)barBack.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(BAR_WIDTH, 14f));
        barBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        barBack.GetComponent<Image>().raycastTarget = false;
        GameObject barFill = new("ChargeFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barBack.transform, false);
        RectTransform fillRect = (RectTransform)barFill.transform;
        Anchor(fillRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 0f));
        chargeFill = barFill.GetComponent<Image>();
        chargeFill.color = Clear;
        chargeFill.raycastTarget = false;
        cooldownLine = MakeText("Cooldown", 22, TextAnchor.UpperCenter, Color.white);
        Anchor(cooldownLine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(600f, 30f));

        hint = MakeText("Hint", 22, TextAnchor.LowerCenter, new Color(0.85f, 0.95f, 1f));
        Anchor(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1500f, 70f));
        hint.text = $"Mouse: aim   Hold a mouse button: charge   Release: fire   R: look to the bow   {ExitKeyName()} or Esc: leave";
        message = MakeText("Message", 28, TextAnchor.LowerCenter, new Color(1f, 0.8f, 0.6f));
        Anchor(message.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1200f, 40f));
        message.text = string.Empty;

        GameObject panel = new("Buttons", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        RectTransform buttonPanel = (RectTransform)panel.transform;
        Anchor(buttonPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(170f, 54f));
        MakeButton(buttonPanel, "Leave", Vector2.zero, new Vector2(170f, 54f), () => console.Close(true, "the Leave button"));
    }

    /// <summary>Takes the input: the cursor is hidden and locked, and walking and looking are locked.</summary>
    public void Open()
    {
        openFrame = Time.frameCount;
        base.Select(true);
        // The group frees the cursor; a turret is aimed with the mouse's movement, so it is locked again
        UWE.Utils.lockCursor = true;
        armed = false;
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

    public void ShowMessage(string text)
    {
        if (!message || string.IsNullOrEmpty(text))
        {
            return;
        }
        message.text = text;
        messageUntil = Time.unscaledTime + MESSAGE_SECONDS;
    }

    public override void Update()
    {
        base.Update(); // Escape deselects the group
        if (!shut && console && RetakeIfAsked())
        {
            return;
        }
        if (shut || !focused || !console)
        {
            return;
        }
        LogPresses();
        if (GameInput.GetButtonDown(GameInput.Button.Exit))
        {
            console.Close(true, "the exit key");
            return;
        }
        if (UnityEngine.Input.GetKeyDown(KeyCode.R) && console.Rig && console.Rig.Active)
        {
            console.Rig.SetAim(0f, 0f);
        }
        if (message && messageUntil > 0f && Time.unscaledTime > messageUntil)
        {
            message.text = string.Empty;
            messageUntil = 0f;
        }
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // focus lost and back: the lock is asked for again
            UWE.Utils.lockCursor = true;
        }
        if (Time.frameCount <= openFrame + IGNORE_FRAMES)
        {
            // the click that opened the console is still the mouse state of the first frames, and the cursor lock can spike the look
            return;
        }
        Aim();
        Fire();
        UpdateTexts();
    }

    private void Aim()
    {
        if (!console.Rig || !console.Rig.Active)
        {
            return;
        }
        Vector2 look = GameInput.GetLookDelta();
        if (loggedLookFrames < 6 && look != Vector2.zero)
        {
            // for the live check: does the look reach us under the input group
            loggedLookFrames++;
            Log.Info($"[VehicleUpgrades] gunner: look delta {look.x:F2}, {look.y:F2} (cursor {Cursor.lockState})");
        }
        console.Rig.Look(look.x, look.y);
    }

    private void Fire()
    {
        bool held = UnityEngine.Input.GetMouseButton(0) || UnityEngine.Input.GetMouseButton(1);
        if (!armed)
        {
            // the button that opened the console has to be let go once
            if (!held)
            {
                armed = true;
            }
            return;
        }
        bool ready = console.CooldownRemaining <= 0f && !console.IsWaitingForShot;
        if (held)
        {
            if (!ready && !charging)
            {
                return;
            }
            charging = true;
            charge = GunnerRules.ChargeAfter(charge, Time.unscaledDeltaTime);
            if (charge >= 1f)
            {
                // like the rifle: a full charge fires by itself, and the button has to be let go before the next
                Release();
                armed = false;
            }
            return;
        }
        if (charging)
        {
            Release();
        }
    }

    private void Release()
    {
        float released = charge;
        charging = false;
        charge = 0f;
        if (!console.TryFire(released, out string why) && !string.IsNullOrEmpty(why))
        {
            ShowMessage(why);
        }
    }

    private void UpdateTexts()
    {
        float power = console.PowerPercent;
        float shotEnergy = VehicleUpgradeSettings.GunnerShotEnergy * Mathf.Max(charge, GunnerRules.MIN_CHARGE);
        int powerNow = power < 0f ? -1 : Mathf.RoundToInt(power) * 100000 + Mathf.RoundToInt(shotEnergy * 10f);
        if (powerNow != powerKey)
        {
            powerKey = powerNow;
            powerLine.text = power < 0f ? "Cyclops power: -" : $"Cyclops power: {power:F0} %    shot: {shotEnergy:F1} energy";
        }
        if (console.Rig && console.Rig.Active)
        {
            int aimNow = (Mathf.RoundToInt(console.Rig.Yaw) + 200) * 1000 + Mathf.RoundToInt(console.Rig.Pitch) + 100;
            if (aimNow != aimKey)
            {
                aimKey = aimNow;
                aimLine.text = $"Turret: {console.Rig.Yaw:F0} deg from the bow ({(console.Rig.Yaw >= 0f ? "starboard" : "port")}), {console.Rig.Pitch:F0} deg {(console.Rig.Pitch >= 0f ? "up" : "down")}";
            }
        }
        rangeLine.text = console.TargetText;

        float cooldown = console.CooldownRemaining;
        bool waiting = console.IsWaitingForShot;
        int cooldownNow = waiting ? -2 : cooldown > 0f ? Mathf.RoundToInt(cooldown * 10f) : charging ? -3 : -1;
        if (cooldownNow != cooldownKey)
        {
            cooldownKey = cooldownNow;
            cooldownLine.text = waiting ? "Waiting for the Cyclops..." : cooldown > 0f ? $"Cooling: {cooldown:F1} s" : charging ? string.Empty : "Ready";
        }
        Color color = cooldown > 0f || waiting ? Busy : Clear;
        foreach (Image part in crosshair)
        {
            part.color = color;
        }
        chargeFill.color = color;
        chargeFill.rectTransform.sizeDelta = new Vector2(BAR_WIDTH * charge, 0f);
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
