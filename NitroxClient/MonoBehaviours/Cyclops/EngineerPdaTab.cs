using System;
using System.Collections.Generic;
using System.IO;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The page of the PDA that the engineer console opens (<see cref="EngineerConsole" />): a side-view picture of the Cyclops, a side view with the bow on the left, with round bubbles in the look of the game's
/// equipment slots at the spots chosen (the four routes of the power, the overdrive, the vent and the fire suppression), the engine's heat as a bar behind the stern, the fires and the breaches where
/// they are, and under the picture the words that say what each thing does. It is a <c>uGUI_PDATab</c> of our own, registered in the PDA's tabs under an id of
/// its own while the PDA initialises (<c>uGUI_PDA_Initialize_Patch</c>) and not in the toolbar, so that <c>PDA.Open</c> shows it as a page of its own without the tabs; the PDA owns the input, the exit key and the
/// distance from the console. The page is built the first time the console asks for it, from the PDA's own text and slot as templates, on a sheet of a fixed size that is scaled to the PDA's page; everything is done
/// defensively: when something is not as expected the log says so and the console does not open.
/// </summary>
public class EngineerPdaTab : uGUI_PDATab
{
    /// <summary>The id of the page in the PDA's tabs (0 to 7 are the game's, 8 is the player list).</summary>
    public static readonly PDATab TAB_ID = (PDATab)9;

    private const string PAGE_NAME = "NitroxEngineerTab";
    private const string SHIPPED_FOLDER = "EngineerUi";
    private const string PICTURE_FILE = "cyclops-side-view.png";

    // The page is laid out on a sheet of a fixed size that is scaled to fit the PDA's page, so that the look does not depend on the screen
    // (the PDA's page is 1195 x 970 units: the sheet is as wide as it is for the width and a little less high, so the width decides the scale, 0.747 on that page)
    private const float STAGE_WIDTH = 1600f;
    private const float STAGE_HEIGHT = 1040f;
    private const float MARGIN = 24f;
    private const float TITLE_HEIGHT = 70f;
    private const float PICTURE_AREA_HEIGHT = 586f;
    private const float COLUMN_WIDTH = 756f;
    private const float COLUMN_GAP = 40f;
    private const float ROWS_TOP = 676f;
    private const float ROW_HEIGHT = 38f;
    private const float TWO_ROWS_HEIGHT = 68f;

    // Text sizes on the sheet (units of the sheet: about 0.75 of a pixel at 1080p)
    private const float TITLE_SIZE = 38f;
    private const float POWER_SIZE = 28f;
    private const float ROW_SIZE = 26f;
    private const float MESSAGE_SIZE = 30f;
    private const float LEGEND_SIZE = 24f;
    private const float NAME_SIZE = 30f;
    private const float STATE_SIZE = 24f;
    private const float HEAT_LABEL_SIZE = 26f;

    private const float REFRESH_SECONDS = 0.2f;
    private const float PICTURE_SECONDS = 0.5f;
    private const float MESSAGE_SECONDS = 3f;

    // Sizes of the marks on the picture, as shares of its height
    private const float FLAME_SHARE = 0.075f;
    private const float BURST_SHARE = 0.075f;
    private const float YOU_SHARE = 0.03f;
    private const float BADGE_SHARE = 0.075f;

    // The dark plate behind a bubble's name, and behind its name and state; the plate of a bubble with a state is at least as wide as its longest state
    private const float PLATE_ONE_LINE = 42f;
    private const float PLATE_TWO_LINES = 74f;
    private const float PLATE_PADDING = 24f;
    private const float STATE_PLATE_WIDTH = 210f;

    private static readonly Color Cool = new(0.35f, 0.9f, 0.55f, 0.95f);
    private static readonly Color Warm = new(1f, 0.8f, 0.2f, 0.95f);
    private static readonly Color Hot = new(1f, 0.3f, 0.2f, 0.95f);
    private static readonly Color HeatBack = new(0.02f, 0.08f, 0.1f, 0.85f);
    private static readonly Color TextColor = new(0.85f, 1f, 0.95f, 1f);
    private static readonly Color Heading = new(1f, 0.85f, 0.6f, 1f);
    private static readonly Color BackdropColor = new(0.04f, 0.105f, 0.16f, 1f);
    private static readonly Color PlateColor = new(0f, 0f, 0f, 0.8f);

    // A bubble: a dark disc, a lit ring and the icon in it; the state shows in the ring, the glow and the dimming of the icon
    private static readonly Color DiscRest = new(0.04f, 0.13f, 0.19f, 0.97f);
    private static readonly Color DiscHovered = new(0.07f, 0.2f, 0.28f, 0.98f);
    private static readonly Color DiscChosen = new(0.05f, 0.24f, 0.25f, 0.98f);
    private static readonly Color DiscDenied = new(0.05f, 0.08f, 0.1f, 0.95f);
    private static readonly Color RingRest = new(0.3f, 0.82f, 1f, 1f);
    private static readonly Color RingHovered = new(0.72f, 0.97f, 1f, 1f);
    private static readonly Color RingChosen = new(0.5f, 1f, 0.85f, 1f);
    private static readonly Color RingDenied = new(0.5f, 0.55f, 0.58f, 1f);
    private static readonly Color HaloChosen = new(0.4f, 1f, 0.8f, 0.85f);
    private static readonly Color HaloHovered = new(0.7f, 0.95f, 1f, 0.5f);
    private static readonly Color IconDenied = new(0.85f, 0.88f, 0.9f, 0.6f);
    private static readonly Color NameRest = Color.white;
    private static readonly Color NameChosen = new(1f, 0.93f, 0.55f, 1f);
    private static readonly Color NameDenied = new(0.84f, 0.9f, 0.92f, 1f);
    private static readonly Color StateRest = new(0.8f, 0.95f, 1f, 1f);
    private static readonly Color StateDenied = new(0.78f, 0.84f, 0.87f, 1f);
    private static readonly Color FireTint = new(1f, 0.45f, 0.2f, 0.3f);
    private static readonly Color FlameColor = new(1f, 0.55f, 0.1f, 1f);
    private static readonly Color BurstColor = new(1f, 0.15f, 0.15f, 1f);
    private static readonly Color YouColor = new(0.3f, 1f, 0.5f, 1f);

    private sealed class BubbleView
    {
        public EngineerBubble Id;
        public string Name;
        public bool HasState;
        public float X;
        public float Y;
        public float Diameter;
        public RectTransform Root;
        public Image Back;
        public Image Ring;
        public Image Glow;
        public Image Icon;
        public TMP_Text Label;
        public TMP_Text Status;
        public RectTransform Plate;
        public float PlateWidth;
        public bool Chosen;
        public bool Denied;
        public bool Hovered;
    }

    private sealed class Badge
    {
        public Image Disc;
        public TMP_Text Count;
    }

    private static EngineerPdaTab instance;

    private EngineerConsole console;
    private bool built;
    private bool buildFailed;
    private bool refreshFailed;
    private bool fitted;
    private bool loggedFit;
    private RectTransform page;
    private RectTransform stage;
    private RectTransform pictureRect;
    private RectTransform markers;
    private RectTransform tintLayer;
    private Vector2 lastPageSize;
    private Vector2 pictureSize;
    private TextMeshProUGUI fontTemplate;
    private Sprite pictureSprite;
    private TMP_Text powerLine;
    private TMP_Text routeEffect;
    private TMP_Text heatLine;
    private TMP_Text overdriveLine;
    private TMP_Text ventLine;
    private TMP_Text fireLine;
    private TMP_Text hintLine;
    private TMP_Text suppressLine;
    private TMP_Text emergencyLine;
    private TMP_Text messageLine;
    private TMP_Text heatLabel;
    private RectTransform heatFill;
    private Image heatFillImage;
    private Image youMark;
    private readonly List<BubbleView> bubbles = [];
    private readonly List<Image> roomTints = [];
    private readonly List<Image> flames = [];
    private readonly List<Image> bursts = [];
    private readonly List<Badge> fireBadges = [];
    private readonly List<Badge> breachBadges = [];
    private readonly int[] fireCounts = new int[EngineerPictureLayout.RoomCount];
    private readonly int[] breachCounts = new int[EngineerPictureLayout.RoomCount];
    private readonly List<string> burningRooms = [];
    private readonly List<(float X, float Y, float Z)> breachPoints = [];
    private readonly EngineerDamage.Picture picture = new();
    private BubbleView hoveredBubble;
    private int flameCount;
    private float nextRefresh;
    private float nextPicture;
    private float messageUntil;

    /// <summary>The page in the PDA, once the PDA has initialised; null before.</summary>
    public static EngineerPdaTab Instance => instance;

    // ---- registration

    /// <summary>
    /// Adds the page to the PDA's tabs, while the PDA initialises (called by <c>uGUI_PDA_Initialize_Patch</c> before the PDA registers its tabs). Only the empty page is made here: nothing that can fail runs in the
    /// middle of the PDA's own initialisation.
    /// </summary>
    public static void InjectInto(uGUI_PDA pda)
    {
        try
        {
            if (!pda || !pda.content || pda.tabs == null || pda.tabs.ContainsKey(TAB_ID))
            {
                return;
            }
            GameObject pageObject = NewObject(PAGE_NAME, pda.content.transform, typeof(LayoutElement));
            pageObject.GetComponent<LayoutElement>().ignoreLayout = true;
            pageObject.SetActive(false);
            RectTransform rect = (RectTransform)pageObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            EngineerPdaTab tab = pageObject.AddComponent<EngineerPdaTab>();
            tab.page = rect;
            pda.tabs.Add(TAB_ID, tab);
            instance = tab;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] engineer PDA: the page could not be added to the PDA");
        }
    }

    // ---- what the console does with it

    /// <summary>Makes the page ready for the console (it is built the first time); false, with the reason, when it cannot be shown.</summary>
    public bool TryAttach(EngineerConsole owner, out string why)
    {
        why = null;
        uGUI_PDA pda = uGUI_PDA.main;
        if (!pda || !page)
        {
            why = "the PDA is not there";
            return false;
        }
        if (!built && !buildFailed)
        {
            // built while it is active: a text component sets itself up when it is added to an active object (the databank's button does the same)
            bool wasActive = page.gameObject.activeSelf;
            try
            {
                page.gameObject.SetActive(true);
                Build(pda);
                built = true;
            }
            catch (Exception exception)
            {
                buildFailed = true;
                Log.Error(exception, "[VehicleUpgrades] engineer PDA: the page could not be built");
            }
            finally
            {
                page.gameObject.SetActive(wasActive);
            }
        }
        if (!built)
        {
            why = "the engineer's page of the PDA could not be built";
            return false;
        }
        console = owner;
        refreshFailed = false;
        return true;
    }

    public void Detach(EngineerConsole owner)
    {
        if (console == owner)
        {
            console = null;
        }
    }

    public void ShowMessage(string text)
    {
        if (!messageLine)
        {
            return;
        }
        messageLine.text = text ?? string.Empty;
        messageUntil = Time.unscaledTime + MESSAGE_SECONDS;
    }

    // ---- what the PDA calls

    public override void Open()
    {
        if (!page)
        {
            return;
        }
        page.gameObject.SetActive(true);
        if (!built || !console)
        {
            return;
        }
        try
        {
            Fit();
            Refresh(true);
        }
        catch (Exception exception)
        {
            refreshFailed = true;
            Log.Error(exception, "[VehicleUpgrades] engineer PDA: the page could not be filled");
        }
    }

    public override void Close()
    {
        if (page)
        {
            page.gameObject.SetActive(false);
        }
        hoveredBubble = null;
    }

    private void OnDestroy()
    {
        // the PDA goes with the game's scene: the picture we loaded does not stay behind (the sprites the game owns are not ours to destroy)
        if (instance == this)
        {
            instance = null;
        }
        if (pictureSprite)
        {
            Destroy(pictureSprite.texture);
            Destroy(pictureSprite);
        }
    }

    public override void OnUpdate(bool isOpen)
    {
        if (!isOpen || !built || !console || refreshFailed)
        {
            return;
        }
        try
        {
            float now = Time.unscaledTime;
            if (messageLine && messageUntil > 0f && now > messageUntil)
            {
                messageLine.text = string.Empty;
                messageUntil = 0f;
            }
            if (!fitted || page.rect.size != lastPageSize)
            {
                Fit();
            }
            if (now >= nextRefresh)
            {
                nextRefresh = now + REFRESH_SECONDS;
                Refresh(false);
            }
            Flicker(now);
        }
        catch (Exception exception)
        {
            // once, not every frame
            refreshFailed = true;
            Log.Error(exception, "[VehicleUpgrades] engineer PDA: the page could not be updated");
        }
    }

    /// <summary>The page's own buttons: the tab keys would take the PDA to another page and leave the console without the server knowing; the cancel key closes the PDA (the base does it).</summary>
    public override bool OnButtonDown(GameInput.Button button)
    {
        if (button == GameInput.Button.UINextTab || button == GameInput.Button.UIPrevTab)
        {
            return true;
        }
        return base.OnButtonDown(button);
    }

    // ---- building

    private void Build(uGUI_PDA pda)
    {
        fontTemplate = pda.backButtonText ? pda.backButtonText : pda.GetComponentInChildren<TextMeshProUGUI>(true);
        if (!fontTemplate)
        {
            throw new InvalidOperationException("the PDA has no text to copy the font from");
        }
        Log.Info($"[VehicleUpgrades] engineer PDA: building the page: page {page.rect.size}, font {(fontTemplate.font ? fontTemplate.font.name : "none")}");

        // An opaque dark backdrop over the whole page, so that nothing behind the PDA (the engine room, the in-world panel) shows through it (the game's own tabs have one). It takes the clicks that miss a bubble, as the
        // PDA's own background does, so that they never count as a click outside the PDA.
        GameObject backdropObject = NewObject("Backdrop", page, typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)backdropObject.transform);
        Image backdrop = backdropObject.GetComponent<Image>();
        backdrop.color = BackdropColor;
        backdrop.raycastTarget = true;

        stage = (RectTransform)NewObject("Stage", page).transform;
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
        stage.sizeDelta = new Vector2(STAGE_WIDTH, STAGE_HEIGHT);
        stage.anchoredPosition = Vector2.zero;
        stage.localScale = Vector3.zero; // shown when Fit has worked out its scale

        TMP_Text title = MakeText(stage, "Title", TITLE_SIZE, TextAlignmentOptions.TopLeft, Heading, false);
        title.text = "ENGINEER CONSOLE";
        PlaceTopLeft(title.rectTransform, MARGIN, 10f, 800f, 50f);
        powerLine = MakeText(stage, "Power", POWER_SIZE, TextAlignmentOptions.TopRight, TextColor, false);
        PlaceTopLeft(powerLine.rectTransform, STAGE_WIDTH - MARGIN - 900f, 18f, 900f, 40f);

        // the picture, and over it (in this order, from the back): the marks of the damage, the bubbles, the names and states of the bubbles, the heat bar
        GameObject pictureObject = NewObject("Picture", stage, typeof(CanvasRenderer), typeof(Image));
        pictureRect = (RectTransform)pictureObject.transform;
        (float pictureWidth, float pictureHeight) = EngineerPictureLayout.FitPicture(STAGE_WIDTH - 2f * MARGIN, PICTURE_AREA_HEIGHT);
        pictureSize = new Vector2(pictureWidth, pictureHeight);
        pictureRect.anchorMin = pictureRect.anchorMax = pictureRect.pivot = new Vector2(0.5f, 1f);
        pictureRect.sizeDelta = pictureSize;
        pictureRect.anchoredPosition = new Vector2(0f, -TITLE_HEIGHT);
        Image pictureImage = pictureObject.GetComponent<Image>();
        pictureImage.raycastTarget = false;
        pictureSprite = LoadPicture();
        if (pictureSprite)
        {
            pictureImage.sprite = pictureSprite;
            pictureImage.preserveAspect = true;
        }
        else
        {
            pictureImage.color = new Color(0.1f, 0.2f, 0.25f, 0.6f);
        }

        // the tint of a burning room lies under the bubbles; the flames, the bursts, the counts and the dot of the engineer lie over them (made after them): in the first survival-world check the flame of a fire at the
        // middle of the helm was hidden behind the bubble that sits there
        tintLayer = (RectTransform)NewObject("Tints", pictureRect).transform;
        Stretch(tintLayer);

        foreach ((EngineerBubble bubble, string name, float fx, float fy) in EngineerPictureLayout.Bubbles)
        {
            bubbles.Add(MakeBubble(bubble, name, fx, fy));
        }

        // the names and states are made after all the bubbles, so that no bubble is drawn over a name that its neighbour's plate carries
        foreach (BubbleView view in bubbles)
        {
            MakeCaption(view);
        }
        MakeHeatBar();

        markers = (RectTransform)NewObject("Marks", pictureRect).transform;
        Stretch(markers);
        youMark = MakeMarkImage("You", EngineerGlyphs.Disc, YouColor);

        // the words under the picture, in two columns
        routeEffect = MakeRow("RouteEffect", 0, ROWS_TOP, TWO_ROWS_HEIGHT, TextColor);
        heatLine = MakeRow("HeatLine", 0, ROWS_TOP + 72f, ROW_HEIGHT, TextColor);
        ventLine = MakeRow("VentLine", 0, ROWS_TOP + 114f, ROW_HEIGHT, TextColor);
        overdriveLine = MakeRow("OverdriveLine", 0, ROWS_TOP + 156f, ROW_HEIGHT, TextColor);
        fireLine = MakeRow("Fires", 1, ROWS_TOP, ROW_HEIGHT, new Color(1f, 0.7f, 0.3f));
        hintLine = MakeRow("Hint", 1, ROWS_TOP + 42f, TWO_ROWS_HEIGHT, new Color(1f, 0.55f, 0.5f));
        suppressLine = MakeRow("SuppressLine", 1, ROWS_TOP + 114f, ROW_HEIGHT, TextColor);
        emergencyLine = MakeRow("Emergency", 1, ROWS_TOP + 156f, TWO_ROWS_HEIGHT, new Color(0.7f, 1f, 0.8f));
        messageLine = MakeText(stage, "Message", MESSAGE_SIZE, TextAlignmentOptions.Top, new Color(1f, 0.8f, 0.6f), false);
        PlaceTopLeft(messageLine.rectTransform, MARGIN, 930f, STAGE_WIDTH - 2f * MARGIN, 42f);
        TMP_Text legend = MakeText(stage, "Legend", LEGEND_SIZE, TextAlignmentOptions.Top, new Color(0.7f, 0.9f, 1f), false);
        PlaceTopLeft(legend.rectTransform, MARGIN, 984f, STAGE_WIDTH - 2f * MARGIN, 36f);
        legend.text = $"Click a bubble to use it   Vent: -{CyclopsEngineerRules.VENT_HEAT} heat, {CyclopsEngineerRules.VENT_ENERGY:F0} energy   Overdrive: flank speed, on the engines route   {ExitKeyName()} or Esc: leave";
    }

    private static Sprite LoadPicture()
    {
        try
        {
            string path = Nitrox.Model.Helper.NitroxUser.LauncherPath is { } launcher ? Path.Combine(launcher, "Resources", SHIPPED_FOLDER, PICTURE_FILE) : null;
            if (path == null || !File.Exists(path))
            {
                Log.Warn($"[VehicleUpgrades] engineer PDA: the picture of the Cyclops is not there ({path ?? "the launcher's folder is not known"}); the page has none");
                return null;
            }
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false) { name = "NitroxEngineerCyclops", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            if (!texture.LoadImage(File.ReadAllBytes(path), true))
            {
                Log.Warn($"[VehicleUpgrades] engineer PDA: the picture {path} could not be read");
                return null;
            }
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] engineer PDA: the picture of the Cyclops could not be loaded");
            return null;
        }
    }

    /// <summary>
    /// One bubble, drawn in code so that it always shows (the game's equipment slot, tried first, has a sprite 2 by 19 pixels that is no bubble): a halo behind it for the glow, a dark disc that takes the clicks, a lit ring
    /// on the disc and the icon in the middle. The name and the state under it are made by <see cref="MakeCaption" /> once all the bubbles are there.
    /// </summary>
    private BubbleView MakeBubble(EngineerBubble id, string name, float fx, float fy)
    {
        float diameter = EngineerPictureLayout.BUBBLE_DIAMETER * pictureSize.y;
        (float x, float y) = EngineerPictureLayout.PanelPoint(fx, fy, pictureSize.x, pictureSize.y);

        // the glow behind the bubble (it lights when the bubble is chosen or under the pointer), made first so that it is drawn behind
        GameObject glowObject = NewObject($"Glow-{id}", pictureRect, typeof(CanvasRenderer), typeof(Image));
        RectTransform glowRect = (RectTransform)glowObject.transform;
        glowRect.anchorMin = glowRect.anchorMax = glowRect.pivot = new Vector2(0.5f, 0.5f);
        glowRect.sizeDelta = new Vector2(diameter * 1.5f, diameter * 1.5f);
        glowRect.anchoredPosition = new Vector2(x, y);
        Image glow = glowObject.GetComponent<Image>();
        glow.sprite = EngineerGlyphs.Halo;
        glow.color = Color.clear;
        glow.raycastTarget = false;

        GameObject root = NewObject($"Bubble-{id}", pictureRect, typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Image back = root.GetComponent<Image>();
        back.sprite = EngineerGlyphs.Disc;
        back.color = DiscRest;
        back.raycastTarget = true;
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(diameter, diameter);
        rect.anchoredPosition = new Vector2(x, y);

        GameObject ringObject = NewObject("Ring", rect, typeof(CanvasRenderer), typeof(Image));
        Stretch((RectTransform)ringObject.transform);
        Image ring = ringObject.GetComponent<Image>();
        ring.sprite = EngineerGlyphs.Ring;
        ring.color = RingRest;
        ring.raycastTarget = false;

        GameObject iconObject = NewObject("Icon", rect, typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.anchorMin = new Vector2(0.2f, 0.2f);
        iconRect.anchorMax = new Vector2(0.8f, 0.8f);
        iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = IconOf(id);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        BubbleView view = new()
        {
            Id = id,
            Name = name,
            HasState = id is EngineerBubble.Overdrive or EngineerBubble.Vent or EngineerBubble.FireSuppression,
            X = x,
            Y = y,
            Diameter = diameter,
            Root = rect,
            Back = back,
            Ring = ring,
            Glow = glow,
            Icon = icon
        };
        Button button = root.GetComponent<Button>();
        button.targetGraphic = back;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => OnBubbleClicked(view));
        BubbleHover hover = root.AddComponent<BubbleHover>();
        hover.Entered = () => SetHover(view, true);
        hover.Left = () => SetHover(view, false);
        return view;
    }

    /// <summary>The name of a bubble under it, bold, on a dark plate (the plate also carries the state under the name for the actions), so that they read over the bright picture.</summary>
    private void MakeCaption(BubbleView view)
    {
        GameObject plateObject = NewObject($"Plate-{view.Id}", pictureRect, typeof(CanvasRenderer), typeof(Image));
        RectTransform plateRect = (RectTransform)plateObject.transform;
        plateRect.anchorMin = plateRect.anchorMax = new Vector2(0.5f, 0.5f);
        plateRect.pivot = new Vector2(0.5f, 1f);
        plateRect.anchoredPosition = new Vector2(view.X, view.Y - view.Diameter / 2f - 2f);
        Image plate = plateObject.GetComponent<Image>();
        plate.color = PlateColor;
        plate.raycastTarget = false;

        TMP_Text label = MakeText(pictureRect, $"Label-{view.Id}", NAME_SIZE, TextAlignmentOptions.Top, NameRest, false);
        label.text = view.Name;
        label.fontStyle = FontStyles.Bold;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.sizeDelta = new Vector2(400f, 40f);
        labelRect.anchoredPosition = new Vector2(view.X, view.Y - view.Diameter / 2f - 5f);

        // the state under the name: ready, cooling and running for the actions (a route shows that it is the one that is set by its lit ring, its glow and its gold name)
        TMP_Text status = MakeText(pictureRect, $"Status-{view.Id}", STATE_SIZE, TextAlignmentOptions.Top, StateRest, false);
        RectTransform statusRect = status.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.sizeDelta = new Vector2(400f, 32f);
        statusRect.anchoredPosition = new Vector2(view.X, view.Y - view.Diameter / 2f - 42f);

        float plateWidth = Mathf.Max(label.preferredWidth, view.Name.Length * 18f, view.HasState ? STATE_PLATE_WIDTH : 0f) + PLATE_PADDING;
        plateRect.sizeDelta = new Vector2(plateWidth, PLATE_ONE_LINE);
        view.Plate = plateRect;
        view.PlateWidth = plateWidth;
        view.Label = label;
        view.Status = status;
        Tint(view);
    }

    private static Sprite IconOf(EngineerBubble id)
    {
        // the game's own icons of the Cyclops modules where there is one, our own glyphs for the rest
        Sprite sprite = id switch
        {
            EngineerBubble.Shields => SpriteManager.Get(TechType.CyclopsShieldModule),
            EngineerBubble.Systems => SpriteManager.Get(TechType.CyclopsSonarModule),
            EngineerBubble.FireSuppression => SpriteManager.Get(TechType.CyclopsFireSuppressionModule),
            _ => null
        };
        if (sprite)
        {
            return sprite;
        }
        return id switch
        {
            EngineerBubble.Shields => EngineerGlyphs.Shield,
            EngineerBubble.Systems => EngineerGlyphs.Sonar,
            EngineerBubble.Engines => EngineerGlyphs.Engines,
            EngineerBubble.Balanced => EngineerGlyphs.Balanced,
            EngineerBubble.Overdrive => EngineerGlyphs.Overdrive,
            EngineerBubble.Vent => EngineerGlyphs.Vent,
            _ => EngineerGlyphs.Drop
        };
    }

    private void MakeHeatBar()
    {
        (float x0, float x1, float y0, float y1) = EngineerPictureLayout.HeatBar;
        (float cx, float cy) = EngineerPictureLayout.PanelPoint((x0 + x1) / 2f, (y0 + y1) / 2f, pictureSize.x, pictureSize.y);

        // a light frame, so that the empty bar can be seen on the dark backdrop (a light outline)
        GameObject frameObject = NewObject("HeatFrame", pictureRect, typeof(CanvasRenderer), typeof(Image));
        RectTransform frame = (RectTransform)frameObject.transform;
        frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2((x1 - x0) * pictureSize.x + 8f, (y1 - y0) * pictureSize.y + 8f);
        frame.anchoredPosition = new Vector2(cx, cy);
        Image frameImage = frameObject.GetComponent<Image>();
        frameImage.color = new Color(0.55f, 0.75f, 0.85f, 0.95f);
        frameImage.raycastTarget = false;

        GameObject backObject = NewObject("HeatBar", pictureRect, typeof(CanvasRenderer), typeof(Image));
        RectTransform back = (RectTransform)backObject.transform;
        back.anchorMin = back.anchorMax = back.pivot = new Vector2(0.5f, 0.5f);
        back.sizeDelta = new Vector2((x1 - x0) * pictureSize.x, (y1 - y0) * pictureSize.y);
        back.anchoredPosition = new Vector2(cx, cy);
        Image backImage = backObject.GetComponent<Image>();
        backImage.color = HeatBack;
        backImage.raycastTarget = false;

        GameObject fillObject = NewObject("Fill", back, typeof(CanvasRenderer), typeof(Image));
        heatFill = (RectTransform)fillObject.transform;
        heatFill.anchorMin = Vector2.zero;
        heatFill.anchorMax = new Vector2(1f, 0f);
        heatFill.offsetMin = heatFill.offsetMax = Vector2.zero;
        heatFillImage = fillObject.GetComponent<Image>();
        heatFillImage.color = Cool;
        heatFillImage.raycastTarget = false;

        // a tick where the engine turns warm and where it turns hot
        foreach (int heat in new[] { CyclopsEngineerRules.WARM_HEAT, CyclopsEngineerRules.HOT_HEAT })
        {
            GameObject tickObject = NewObject($"Tick{heat}", back, typeof(CanvasRenderer), typeof(Image));
            RectTransform tick = (RectTransform)tickObject.transform;
            float at = EngineerPictureLayout.HeatFraction(heat, CyclopsEngineerRules.MAX_HEAT);
            tick.anchorMin = new Vector2(-0.4f, at);
            tick.anchorMax = new Vector2(1.4f, at);
            tick.offsetMin = new Vector2(0f, -1f);
            tick.offsetMax = new Vector2(0f, 1f);
            Image tickImage = tickObject.GetComponent<Image>();
            tickImage.color = new Color(1f, 1f, 1f, 0.7f);
            tickImage.raycastTarget = false;
        }

        heatLabel = MakeText(pictureRect, "HeatLabel", HEAT_LABEL_SIZE, TextAlignmentOptions.Top, new Color(1f, 0.8f, 0.4f), false);
        heatLabel.fontStyle = FontStyles.Bold;
        heatLabel.text = "HEAT";
        RectTransform labelRect = heatLabel.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.sizeDelta = new Vector2(240f, 34f);
        labelRect.anchoredPosition = new Vector2(cx, cy - back.sizeDelta.y / 2f - 4f);
    }

    private TMP_Text MakeRow(string name, int column, float top, float height, Color color)
    {
        TMP_Text text = MakeText(stage, name, ROW_SIZE, TextAlignmentOptions.TopLeft, color, true);
        PlaceTopLeft(text.rectTransform, MARGIN + column * (COLUMN_WIDTH + COLUMN_GAP), top, COLUMN_WIDTH, height);
        return text;
    }

    /// <summary>A text made like the databank button's (a fresh component with the PDA's font), in the layer of its parent.</summary>
    private TextMeshProUGUI MakeText(Transform parent, string name, float size, TextAlignmentOptions alignment, Color color, bool wrap)
    {
        GameObject textObject = NewObject(name, parent, typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.richText = false; // a vehicle's name is typed by a player: it is shown as it is, not read as markup
        text.alignment = alignment;
        text.enableWordWrapping = wrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        text.font = fontTemplate.font;
        text.fontSharedMaterial = fontTemplate.fontSharedMaterial;
        text.fontSize = size;
        text.color = color;
        text.text = string.Empty;
        return text;
    }

    private Image MakeMarkImage(string name, Sprite sprite, Color color, Transform parent = null)
    {
        GameObject created = NewObject(name, parent ? parent : markers, typeof(CanvasRenderer), typeof(Image));
        Image image = created.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject NewObject(string name, Transform parent, params Type[] extra)
    {
        Type[] types = new Type[extra.Length + 1];
        types[0] = typeof(RectTransform);
        Array.Copy(extra, 0, types, 1, extra.Length);
        GameObject created = new(name, types) { layer = parent.gameObject.layer };
        created.transform.SetParent(parent, false);
        return created;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    /// <summary>Places a rectangle by its top left corner, measured from the top left corner of the sheet.</summary>
    private static void PlaceTopLeft(RectTransform rect, float x, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -top);
    }

    private static string ExitKeyName()
    {
        try
        {
            return NavigatorConsoleText.PlainKeyName(GameInput.FormatButton(GameInput.Button.Exit), "Esc");
        }
        catch (Exception)
        {
            return "Esc";
        }
    }

    // ---- laying out

    /// <summary>The sheet is scaled to the biggest size that fits the PDA's page.</summary>
    private void Fit()
    {
        Rect rect = page.rect;
        if (rect.width < 10f || rect.height < 10f)
        {
            // not laid out yet: nothing is shown until it is, and the next frame tries again
            stage.localScale = Vector3.zero;
            fitted = false;
            return;
        }
        lastPageSize = rect.size;
        fitted = true;
        float scale = Mathf.Min(rect.width / STAGE_WIDTH, rect.height / STAGE_HEIGHT);
        stage.localScale = new Vector3(scale, scale, 1f);
        if (!loggedFit)
        {
            loggedFit = true;
            Log.Info($"[VehicleUpgrades] engineer PDA: the page is {rect.size} in the PDA, the sheet {STAGE_WIDTH} x {STAGE_HEIGHT} is scaled by {scale:F3}");
        }
    }

    // ---- what is shown

    private void Refresh(bool force)
    {
        if (!console || !built)
        {
            return;
        }
        SubRoot cyclops = console.SubRoot;
        NitroxId id = console.CyclopsId;
        if (!cyclops || id == null)
        {
            return;
        }
        EngineReading engine = console.ReadEngine();
        EngineerRoute route = EngineerState.RouteOf(id);

        powerLine.text = $"Cyclops power: {(engine.PowerPercent >= 0f ? $"{engine.PowerPercent:F0} %" : "-")}      Engine: {(!engine.Known ? "no report from the helm yet" : engine.EngineOn ? "on" : "off")}";
        routeEffect.text = hoveredBubble != null ? Describe(hoveredBubble.Id) : $"{EngineerText.RouteName(route)}: {EngineerText.RouteEffect(route)}";

        // the heat: a bar behind the stern, green, amber and red
        if (engine.Known)
        {
            heatFill.anchorMax = new Vector2(1f, EngineerPictureLayout.HeatFraction(engine.Heat, CyclopsEngineerRules.MAX_HEAT));
            EngineerHeatBand band = CyclopsEngineerRules.BandOf(engine.Heat);
            heatFillImage.color = band == EngineerHeatBand.Hot ? Hot : band == EngineerHeatBand.Warm ? Warm : Cool;
            heatLine.text = $"Heat {engine.Heat} of {CyclopsEngineerRules.MAX_HEAT}: {EngineerText.HeatBand(engine.Heat)}{(engine.Heating ? "   (rising)" : string.Empty)}";
            heatLabel.text = $"HEAT {engine.Heat}/{CyclopsEngineerRules.MAX_HEAT}";
        }
        else
        {
            heatFill.anchorMax = new Vector2(1f, 0f);
            heatLine.text = "Heat: no report from the helm yet";
            heatLabel.text = "HEAT";
        }

        float ventLeft = EngineerState.VentReadyRemaining(id);
        float overdriveLeft = EngineerState.OverdriveRemaining(id);
        float overdriveReady = EngineerState.OverdriveReadyRemaining(id);
        bool nothingToVent = engine.Known && engine.Heat <= 0;
        ventLine.text = ventLeft > 0f ? $"Vent: cooling, {EngineerText.Seconds(ventLeft)}" : nothingToVent ? "Vent: nothing to vent (the engine is cool)" : "Vent: ready";
        string ventStatus = ventLeft > 0f ? $"cooling {EngineerText.Seconds(ventLeft)}" : nothingToVent ? "engine is cool" : "ready";
        bool overdriveChosen = false;
        bool overdriveDenied = true;
        string overdriveStatus;
        if (overdriveLeft > 0f && route == EngineerRoute.Engines)
        {
            overdriveLine.text = $"Overdrive: RUNNING, {EngineerText.Seconds(overdriveLeft)} left";
            overdriveStatus = $"RUNNING {EngineerText.Seconds(overdriveLeft)}";
            overdriveChosen = true;
            overdriveDenied = false;
        }
        else if (overdriveLeft > 0f)
        {
            // the server keeps the clock running while the power is elsewhere: the boost is off until it comes back, and it does not get more time
            overdriveLine.text = $"Overdrive: paused, {EngineerText.Seconds(overdriveLeft)} left (send the power back to the engines)";
            overdriveStatus = $"paused {EngineerText.Seconds(overdriveLeft)}";
        }
        else if (route != EngineerRoute.Engines)
        {
            overdriveLine.text = "Overdrive: send the power to the engines first";
            overdriveStatus = "needs engines";
        }
        else if (overdriveReady > 0f)
        {
            overdriveLine.text = $"Overdrive: cooling, {EngineerText.Seconds(overdriveReady)}";
            overdriveStatus = $"cooling {EngineerText.Seconds(overdriveReady)}";
        }
        else
        {
            overdriveLine.text = $"Overdrive: ready, {VehicleUpgradeSettings.OverdriveSeconds:F0} s past flank speed";
            overdriveStatus = "ready";
            overdriveDenied = false;
        }

        EngineerDamage.Suppression suppression = EngineerDamage.SuppressionOf(cyclops);
        bool suppressChosen = false;
        bool suppressDenied = true;
        string suppressStatus;
        if (!suppression.Installed)
        {
            suppressLine.text = "Fire suppression: none installed";
            suppressStatus = "none installed";
        }
        else if (suppression.Running)
        {
            suppressLine.text = "Fire suppression: RUNNING, dousing the fires";
            suppressStatus = "RUNNING";
            suppressChosen = true;
            suppressDenied = false;
        }
        else if (!suppression.Ready)
        {
            suppressLine.text = suppression.CooldownRemaining > 0f ? $"Fire suppression: recharging, {EngineerText.Seconds(suppression.CooldownRemaining)}" : "Fire suppression: no power";
            suppressStatus = suppression.CooldownRemaining > 0f ? $"recharging {EngineerText.Seconds(suppression.CooldownRemaining)}" : "no power";
        }
        else
        {
            suppressLine.text = picture.Fires.Count > 0 ? "Fire suppression: ready, douses every fire in the Cyclops" : "Fire suppression: ready (no fires)";
            suppressStatus = "ready";
            suppressDenied = false;
        }
        emergencyLine.text = EmergencyText(cyclops, id);

        foreach (BubbleView bubble in bubbles)
        {
            switch (bubble.Id)
            {
                case EngineerBubble.Shields:
                    bubble.Chosen = route == EngineerRoute.Shields;
                    break;
                case EngineerBubble.Systems:
                    bubble.Chosen = route == EngineerRoute.Systems;
                    break;
                case EngineerBubble.Engines:
                    bubble.Chosen = route == EngineerRoute.Engines;
                    break;
                case EngineerBubble.Balanced:
                    bubble.Chosen = route == EngineerRoute.Balanced;
                    break;
                case EngineerBubble.Overdrive:
                    bubble.Chosen = overdriveChosen;
                    bubble.Denied = overdriveDenied;
                    break;
                case EngineerBubble.Vent:
                    bubble.Chosen = false;
                    bubble.Denied = ventLeft > 0f || nothingToVent;
                    break;
                case EngineerBubble.FireSuppression:
                    bubble.Chosen = suppressChosen;
                    bubble.Denied = suppressDenied;
                    break;
            }
            string statusText = bubble.Id switch
            {
                EngineerBubble.Overdrive => overdriveStatus,
                EngineerBubble.Vent => ventStatus,
                EngineerBubble.FireSuppression => suppressStatus,
                _ => string.Empty
            };
            bubble.Status.text = statusText;
            bubble.Plate.sizeDelta = new Vector2(bubble.PlateWidth, statusText.Length > 0 ? PLATE_TWO_LINES : PLATE_ONE_LINE);
            Tint(bubble);
        }

        // the damage on the picture, twice a second
        if (force || Time.unscaledTime >= nextPicture)
        {
            nextPicture = Time.unscaledTime + PICTURE_SECONDS;
            EngineerDamage.Collect(cyclops, picture);
            DrawDamage(cyclops);
        }
    }

    private static string EmergencyText(SubRoot cyclops, NitroxId id)
    {
        if (!PowerShare.HasModule(cyclops))
        {
            return "Emergency power: no Power Share module in the Cyclops";
        }
        return PowerShare.TryGetEmergencyMark(id, out string vehicle) ? $"Emergency power: drawing from {vehicle}" : "Emergency power: standing by (a docked vehicle gives it when the Cyclops runs low)";
    }

    /// <summary>What a bubble does, for the line under the picture while the pointer is over it.</summary>
    private static string Describe(EngineerBubble id)
    {
        return id switch
        {
            EngineerBubble.Shields => $"{EngineerText.RouteName(EngineerRoute.Shields)}: {EngineerText.RouteEffect(EngineerRoute.Shields)}",
            EngineerBubble.Systems => $"{EngineerText.RouteName(EngineerRoute.Systems)}: {EngineerText.RouteEffect(EngineerRoute.Systems)}",
            EngineerBubble.Engines => $"{EngineerText.RouteName(EngineerRoute.Engines)}: {EngineerText.RouteEffect(EngineerRoute.Engines)}",
            EngineerBubble.Balanced => $"{EngineerText.RouteName(EngineerRoute.Balanced)}: {EngineerText.RouteEffect(EngineerRoute.Balanced)}",
            EngineerBubble.Overdrive => $"Overdrive: {VehicleUpgradeSettings.OverdriveSeconds:F0} s past flank speed, and the engine heats up (the power must be on the engines)",
            EngineerBubble.Vent => $"Vent: lets {CyclopsEngineerRules.VENT_HEAT} heat off the engine for {CyclopsEngineerRules.VENT_ENERGY:F0} energy",
            _ => "Fire suppression: douses every fire in the Cyclops"
        };
    }

    // ---- the damage

#if DEBUG
    /// <summary>Debug: the layer of the flames, the bursts, the counts and the engineer's dot is the last thing drawn on the picture (over the bubbles, the names and the heat bar).</summary>
    private bool MarksAreOnTop()
    {
        return markers && pictureRect && markers.parent == pictureRect && markers.GetSiblingIndex() == pictureRect.childCount - 1;
    }

    /// <summary>Debug: the tint of a burning room is the first thing drawn on the picture, under every bubble.</summary>
    private bool TintsAreUnder()
    {
        return tintLayer && pictureRect && tintLayer.parent == pictureRect && tintLayer.GetSiblingIndex() == 0;
    }
#endif

    /// <summary>The fires and the breaches on the picture: a flame for each fire, a burst for each breach, the rooms that burn tinted, a count where a room has several, and a dot where the engineer stands.</summary>
    private void DrawDamage(SubRoot cyclops)
    {
        Transform hull = cyclops.transform;
        Player player = Player.main;
        Vector3 engineerLocal = player ? hull.InverseTransformPoint(player.transform.position) : Vector3.zero;
        float height = pictureSize.y;
        Array.Clear(fireCounts, 0, fireCounts.Length);
        Array.Clear(breachCounts, 0, breachCounts.Length);
        burningRooms.Clear();
        breachPoints.Clear();

        int tinted = 0;
        foreach (EngineerDamage.RoomBox room in picture.Rooms)
        {
            if (room.FireValue <= 0)
            {
                continue;
            }
            burningRooms.Add(CutawayRules.RoomName(room.Room));
            (float x0, float x1, float y0, float y1) = EngineerPictureLayout.RoomRect(room.Room);
            Image tint = Pooled(roomTints, tinted, null, "Tint", FireTint, tintLayer);
            tint.rectTransform.anchorMin = new Vector2(x0, 1f - y1);
            tint.rectTransform.anchorMax = new Vector2(x1, 1f - y0);
            tint.rectTransform.offsetMin = tint.rectTransform.offsetMax = Vector2.zero;
            tinted++;
        }
        Hide(roomTints, tinted);

        for (int i = 0; i < picture.Fires.Count; i++)
        {
            (float _, float y, float z) = picture.Fires[i];
            (float fx, float fy) = EngineerPictureLayout.Project(z, y);
            PlaceAt(Pooled(flames, i, EngineerGlyphs.Flame, "Flame", FlameColor).rectTransform, fx, fy, FLAME_SHARE * height);
            fireCounts[EngineerPictureLayout.RoomOf(z, y)]++;
        }
        flameCount = picture.Fires.Count;
        Hide(flames, flameCount);

        for (int i = 0; i < picture.Breaches.Count; i++)
        {
            (float _, float y, float z) = picture.Breaches[i];
            (float fx, float fy) = EngineerPictureLayout.Project(z, y);
            PlaceAt(Pooled(bursts, i, EngineerGlyphs.Burst, "Breach", BurstColor).rectTransform, fx, fy, BURST_SHARE * height);
            breachCounts[EngineerPictureLayout.RoomOf(z, y)]++;
            breachPoints.Add(picture.Breaches[i]);
        }
        Hide(bursts, picture.Breaches.Count);

        ShowBadges(fireBadges, fireCounts, true, FlameColor);
        ShowBadges(breachBadges, breachCounts, false, BurstColor);

        (float youX, float youY) = EngineerPictureLayout.Project(engineerLocal.z, engineerLocal.y);
        PlaceAt(youMark.rectTransform, youX, youY, YOU_SHARE * height);

        fireLine.text = picture.Fires.Count == 0 ? "No fires" : $"Fires: {picture.Fires.Count}   ({(burningRooms.Count == 0 ? "burning" : string.Join(", ", burningRooms))})";
        hintLine.text = CutawayRules.RepairHint(breachPoints, (engineerLocal.x, engineerLocal.y, engineerLocal.z));
    }

    /// <summary>A count in the corner of a room that has a fire or a breach (a breach sits on the hull, outside the room's rectangle, so the number says which room it belongs to); the fires at its top left, the breaches at its top right.</summary>
    private void ShowBadges(List<Badge> pool, int[] counts, bool left, Color color)
    {
        for (int room = 0; room < counts.Length; room++)
        {
            while (pool.Count <= room)
            {
                Image disc = MakeMarkImage($"Count{pool.Count}", EngineerGlyphs.Disc, color);
                TMP_Text count = MakeText(disc.transform, "Number", 26f, TextAlignmentOptions.Center, Color.white, false);
                Stretch(count.rectTransform);
                pool.Add(new Badge { Disc = disc, Count = count });
            }
            Badge badge = pool[room];
            bool shown = counts[room] >= 1;
            badge.Disc.gameObject.SetActive(shown);
            if (!shown)
            {
                continue;
            }
            (float x0, float x1, float y0, float _) = EngineerPictureLayout.RoomRect(room);
            PlaceAt(badge.Disc.rectTransform, left ? x0 + 0.016f : x1 - 0.016f, y0 + 0.032f, BADGE_SHARE * pictureSize.y);
            badge.Count.text = counts[room].ToString();
        }
    }

    private Image Pooled(List<Image> pool, int index, Sprite sprite, string name, Color color, Transform parent = null)
    {
        while (pool.Count <= index)
        {
            pool.Add(MakeMarkImage($"{name}{pool.Count}", sprite, color, parent));
        }
        Image item = pool[index];
        item.gameObject.SetActive(true);
        return item;
    }

    private static void Hide(List<Image> pool, int used)
    {
        for (int i = used; i < pool.Count; i++)
        {
            pool[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Puts a mark's middle at a spot of the picture (fractions from the left and from the top), kept on the picture.</summary>
    private static void PlaceAt(RectTransform rect, float fx, float fy, float size)
    {
        // (a spot that is not a number goes to the middle rather than into the layout)
        fx = float.IsNaN(fx) ? 0.5f : fx;
        fy = float.IsNaN(fy) ? 0.5f : fy;
        rect.anchorMin = rect.anchorMax = new Vector2(Mathf.Clamp(fx, 0.01f, 0.99f), 1f - Mathf.Clamp(fy, 0.01f, 0.99f));
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;
    }

    /// <summary>The flames flicker a little.</summary>
    private void Flicker(float now)
    {
        for (int i = 0; i < flameCount && i < flames.Count; i++)
        {
            float scale = 1f + 0.12f * Mathf.Sin(now * 9f + i * 1.7f);
            flames[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ---- the bubbles

    private void Tint(BubbleView bubble)
    {
        if (!bubble.Back)
        {
            return;
        }
        // A bubble that cannot be used now is dimmed (a grey ring, the icon at 60 percent) but its name and state stay light on their plate; the one that is set (a route) or running is lit: a brighter ring and a glow
        bool lit = bubble.Chosen && !bubble.Denied;
        bubble.Back.color = bubble.Denied ? DiscDenied : lit ? DiscChosen : bubble.Hovered ? DiscHovered : DiscRest;
        bubble.Ring.color = bubble.Denied ? RingDenied : lit ? RingChosen : bubble.Hovered ? RingHovered : RingRest;
        bubble.Glow.color = lit ? HaloChosen : bubble.Hovered && !bubble.Denied ? HaloHovered : Color.clear;
        bubble.Icon.color = bubble.Denied ? IconDenied : Color.white;
        if (bubble.Label)
        {
            bubble.Label.color = bubble.Denied ? NameDenied : lit ? NameChosen : NameRest;
        }
        if (bubble.Status)
        {
            bubble.Status.color = bubble.Denied ? StateDenied : lit ? NameChosen : StateRest;
        }
    }

    private void SetHover(BubbleView bubble, bool hovered)
    {
        bubble.Hovered = hovered;
        if (hovered)
        {
            hoveredBubble = bubble;
        }
        else if (hoveredBubble == bubble)
        {
            hoveredBubble = null;
        }
        Tint(bubble);
    }

    private void OnBubbleClicked(BubbleView bubble)
    {
        if (!console)
        {
            return;
        }
        switch (bubble.Id)
        {
            case EngineerBubble.Shields:
                console.RequestRoute(EngineerRoute.Shields);
                break;
            case EngineerBubble.Systems:
                console.RequestRoute(EngineerRoute.Systems);
                break;
            case EngineerBubble.Engines:
                console.RequestRoute(EngineerRoute.Engines);
                break;
            case EngineerBubble.Balanced:
                console.RequestRoute(EngineerRoute.Balanced);
                break;
            case EngineerBubble.Overdrive:
                console.PressOverdrive();
                break;
            case EngineerBubble.Vent:
                console.PressVent();
                break;
            case EngineerBubble.FireSuppression:
                console.PressSuppress();
                break;
        }
    }

    /// <summary>The pointer over a bubble (the game's own slots do the same with their tints).</summary>
    private sealed class BubbleHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Entered;
        public Action Left;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Left?.Invoke();

        private void OnDisable() => Left?.Invoke();
    }

#if DEBUG
    /// <summary>Debug (the live check): the state of the page as plain tokens for the probe's line.</summary>
    public static string DebugSummary()
    {
        EngineerPdaTab tab = instance;
        if (!tab)
        {
            return "tab=none";
        }
        int chosen = 0;
        int denied = 0;
        foreach (BubbleView bubble in tab.bubbles)
        {
            chosen += bubble.Chosen ? 1 : 0;
            denied += bubble.Denied ? 1 : 0;
        }
        return $"tab=registered tabBuilt={tab.built} tabFailed={tab.buildFailed || tab.refreshFailed} tabShown={(tab.page && tab.page.gameObject.activeSelf)} tabBubbles={tab.bubbles.Count} tabChosen={chosen} tabDenied={denied} tabFlames={tab.flameCount} tabBursts={tab.picture.Breaches.Count} tabTints={tab.burningRooms.Count} tabFireBadges={BadgesShown(tab.fireBadges)} tabBreachBadges={BadgesShown(tab.breachBadges)} tabFireLine=\"{Plain(tab.fireLine)}\" tabHint=\"{Plain(tab.hintLine)}\" tabScale={(tab.stage ? tab.stage.localScale.x.ToString("F3") : "none")} tabMarksOnTop={tab.MarksAreOnTop()} tabTintsUnder={tab.TintsAreUnder()}";
    }

    private static int BadgesShown(List<Badge> badges)
    {
        int shown = 0;
        foreach (Badge badge in badges)
        {
            shown += badge.Disc && badge.Disc.gameObject.activeSelf ? 1 : 0;
        }
        return shown;
    }

    private static string Plain(TMP_Text text)
    {
        return text ? text.text.Replace('"', '\x27') : string.Empty;
    }

    /// <summary>Debug (the live check): a click on a bubble, by its name, through the page's own handler; false when there is no such bubble or the page is not attached.</summary>
    public bool DebugClick(string name)
    {
        foreach (BubbleView bubble in bubbles)
        {
            if (string.Equals(bubble.Id.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                OnBubbleClicked(bubble);
                return console;
            }
        }
        return false;
    }
#endif
}
