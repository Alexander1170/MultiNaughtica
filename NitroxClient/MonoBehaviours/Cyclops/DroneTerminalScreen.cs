using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The Scout Drone's place on the Cyclops: the LEFT screen of the vehicle bay terminal (the terminal in the docking bay that shows the docked Seamoth or Prawn Suit, <c>CyclopsVehicleStorageTerminalManager</c>),
/// the one with the wave graphic, which the game uses for nothing (the play-test of coop.12 asked for the drone to be used from there, and the panel on the engine is gone). It is drawn on the canvas of the
/// terminal's centre screen, which is wide enough for the centre screen only: the left screen is 712 canvas units to its left (read from the Cyclops scene, the canvas is 760 units wide and a unit is 0.9 mm),
/// so it works with the game's own pointer pipeline (the canvas's <c>uGUI_GraphicRaycaster</c> takes any graphic of it, inside its rectangle or not). It shows what the bay holds and who flies it, a small round
/// bubble in the look of the one on the centre screen that loads a Scout Drone from the inventory into the bay (empty while the bay is, the drone's icon while it holds one), and a second bubble that flies it. The
/// bubbles are copies of the centre screen's module bubble with the game's scripts removed (<see cref="CyclopsPanelButtons.PanelButton" />), the text lines copies of its health text. Everything is made defensively:
/// when the terminal does not look as expected nothing is added and the log says so once.
/// </summary>
public sealed class DroneTerminalScreen
{
    private const string GROUP_NAME = "NitroxDroneTerminal";

    /// <summary>The middle of the left screen on the terminal's canvas, in the canvas's own units.</summary>
    private static readonly Vector2 LeftCenter = new(-712f, 0f);

    private const float BUBBLE_SCALE = 1.5f;
    private const float BUBBLE_SPACING = 175f;

    private readonly RectTransform group;
    private readonly CyclopsPanelButtons.PanelButton loadBubble;
    private readonly CyclopsPanelButtons.PanelButton flyBubble;
    private readonly TMP_Text stateLine;
    private readonly TMP_Text pilotLine;
    private Sprite droneSprite;
    private static Sprite playSprite;

    private DroneTerminalScreen(RectTransform group, CyclopsPanelButtons.PanelButton loadBubble, CyclopsPanelButtons.PanelButton flyBubble, TMP_Text stateLine, TMP_Text pilotLine)
    {
        this.group = group;
        this.loadBubble = loadBubble;
        this.flyBubble = flyBubble;
        this.stateLine = stateLine;
        this.pilotLine = pilotLine;
    }

    public bool Exists => group;

    /// <summary>Builds the left screen on the terminal of this Cyclops; null (with the reason in the log) when the terminal is not as expected.</summary>
    public static DroneTerminalScreen TryBuild(SubRoot cyclops, string cyclopsName, Action onLoad, Action onFly)
    {
        try
        {
            CyclopsVehicleStorageTerminalManager manager = cyclops.GetComponentInChildren<CyclopsVehicleStorageTerminalManager>(true);
            Transform moduleBubble = manager && manager.seamothVehicleScreen ? manager.seamothVehicleScreen.transform.Find("Modules") : null;
            Canvas canvas = manager && manager.noVehicleDockedScreen ? manager.noVehicleDockedScreen.GetComponentInParent<Canvas>(true) : null;
            if (!manager || !moduleBubble || !canvas || !manager.healthText)
            {
                Log.Warn($"[VehicleUpgrades] drone terminal: {cyclopsName}: the vehicle bay terminal is not as expected (manager {(manager ? "yes" : "no")}, module bubble {(moduleBubble ? "yes" : "no")}, canvas {(canvas ? "yes" : "no")}, text {(manager && manager.healthText ? "yes" : "no")}): the drone has no screen");
                return null;
            }

            RectTransform canvasRect = (RectTransform)canvas.transform;
            GameObject groupObject = new(GROUP_NAME, typeof(RectTransform));
            RectTransform groupRect = (RectTransform)groupObject.transform;
            groupRect.SetParent(canvasRect, false);
            groupRect.anchorMin = groupRect.anchorMax = new Vector2(0.5f, 0.5f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.sizeDelta = new Vector2(100f, 100f);
            groupRect.anchoredPosition3D = new Vector3(LeftCenter.x, LeftCenter.y, 0f);
            groupRect.localScale = Vector3.one;
            groupRect.localRotation = Quaternion.identity;

            // (the panel's top edge slopes up to the right, so the heading and the state line sit lower and to the right of the bubbles' middle, where the glass is; the pilot goes under the labels)
            TMP_Text heading = MakeText(manager.healthText, groupRect, "Heading", "SCOUT DRONE", 30f, new Vector2(65f, 112f), 360f);
            TMP_Text stateLine = MakeText(manager.healthText, groupRect, "State", string.Empty, 25f, new Vector2(65f, 75f), 380f);
            TMP_Text pilotLine = MakeText(manager.healthText, groupRect, "Pilot", string.Empty, 24f, new Vector2(0f, -190f), 420f);
            TMP_Text loadLabel = MakeText(manager.healthText, groupRect, "LoadLabel", "LOAD", 26f, new Vector2(-BUBBLE_SPACING / 2f, -135f), 200f);
            TMP_Text flyLabel = MakeText(manager.healthText, groupRect, "FlyLabel", "FLY", 26f, new Vector2(BUBBLE_SPACING / 2f, -135f), 200f);
            if (!heading || !stateLine || !pilotLine || !loadLabel || !flyLabel)
            {
                UnityEngine.Object.Destroy(groupObject);
                Log.Warn($"[VehicleUpgrades] drone terminal: {cyclopsName}: the terminal's text could not be copied: the drone has no screen");
                return null;
            }

            CyclopsPanelButtons.PanelButton loadBubble = MakeBubble((RectTransform)moduleBubble, groupRect, "LoadBubble", null, new Vector2(-BUBBLE_SPACING / 2f, -25f), onLoad);
            CyclopsPanelButtons.PanelButton flyBubble = MakeBubble((RectTransform)moduleBubble, groupRect, "FlyBubble", PlaySprite(), new Vector2(BUBBLE_SPACING / 2f, -25f), onFly);
            if (loadBubble == null || flyBubble == null)
            {
                UnityEngine.Object.Destroy(groupObject);
                Log.Warn($"[VehicleUpgrades] drone terminal: {cyclopsName}: the terminal's bubble could not be copied: the drone has no screen");
                return null;
            }

            Log.Info($"[VehicleUpgrades] drone terminal: {cyclopsName}: the Scout Drone's screen is on the left of the vehicle bay terminal (canvas {canvasRect.rect.width:F0} x {canvasRect.rect.height:F0}, scale {canvasRect.lossyScale.x:F5}, at {LeftCenter})");
            return new DroneTerminalScreen(groupRect, loadBubble, flyBubble, stateLine, pilotLine);
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] drone terminal: {cyclopsName}: the screen could not be built");
            return null;
        }
    }

    /// <summary>A text line: a copy of the terminal's own text, with its scripts removed, set in the group.</summary>
    private static TMP_Text MakeText(TMP_Text template, RectTransform parent, string name, string value, float size, Vector2 position, float width)
    {
        GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        copy.name = name;
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour && !IsUiLibrary(behaviour.GetType()))
            {
                UnityEngine.Object.Destroy(behaviour);
            }
        }
        TMP_Text text = copy.GetComponent<TMP_Text>();
        if (!text)
        {
            UnityEngine.Object.Destroy(copy);
            return null;
        }
        RectTransform rect = (RectTransform)copy.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, 60f);
        rect.anchoredPosition3D = new Vector3(position.x, position.y, 0f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = size;
        text.raycastTarget = false;
        text.text = value;
        copy.SetActive(true);
        return text;
    }

    private static CyclopsPanelButtons.PanelButton MakeBubble(RectTransform template, RectTransform parent, string name, Sprite sprite, Vector2 position, Action onClick)
    {
        CyclopsPanelButtons.PanelButton bubble = CyclopsPanelButtons.PanelButton.Create(template, "Icon", name, sprite, () => onClick());
        if (bubble == null)
        {
            return null;
        }
        RectTransform rect = bubble.Root;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(100f, 100f);
        rect.anchoredPosition3D = new Vector3(position.x, position.y, 0f);
        rect.localScale = Vector3.one * BUBBLE_SCALE;
        rect.localRotation = Quaternion.identity;
        bubble.SetVisible(true);
        return bubble;
    }

    private static bool IsUiLibrary(Type type)
    {
        string space = type.Namespace;
        return space != null && (space.StartsWith("UnityEngine.UI", StringComparison.Ordinal) || space.StartsWith("UnityEngine.EventSystems", StringComparison.Ordinal) || space.StartsWith("TMPro", StringComparison.Ordinal));
    }

    /// <summary>A small white triangle that points right (the picture of the fly bubble), made once.</summary>
    private static Sprite PlaySprite()
    {
        if (playSprite)
        {
            return playSprite;
        }
        const int SIZE = 96;
        Texture2D texture = new(SIZE, SIZE, TextureFormat.RGBA32, false) { name = "NitroxDroneFlyIcon" };
        Color32[] pixels = new Color32[SIZE * SIZE];
        for (int y = 0; y < SIZE; y++)
        {
            // the triangle: from x = 0.28 to 0.78 of the width, as high as the distance to its point allows
            float half = 0.30f;
            for (int x = 0; x < SIZE; x++)
            {
                float u = (x + 0.5f) / SIZE;
                float v = (y + 0.5f) / SIZE - 0.5f;
                float reach = Mathf.Clamp01((0.78f - u) / 0.50f) * half;
                bool inside = u >= 0.28f && u <= 0.78f && Mathf.Abs(v) <= reach;
                pixels[y * SIZE + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        texture.filterMode = FilterMode.Bilinear;
        playSprite = Sprite.Create(texture, new Rect(0f, 0f, SIZE, SIZE), new Vector2(0.5f, 0.5f), 100f);
        return playSprite;
    }

    public void SetVisible(bool visible)
    {
        if (group && group.gameObject.activeSelf != visible)
        {
            group.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// What the screen shows now: the line about the bay ("docked, 96 percent", "drone out", "bay empty"), who flies it, whether the bay holds a drone (the load bubble shows its icon, or is empty), whether it can be
    /// flown now, and the texts of the pointer over each bubble.
    /// </summary>
    public void Show(string state, string pilot, bool loaded, bool canFly, string loadHover, string loadHint, string flyHover, string flyHint)
    {
        if (!group)
        {
            return;
        }
        stateLine.text = state.ToUpperInvariant();
        pilotLine.text = string.IsNullOrEmpty(pilot) ? string.Empty : $"PILOT: {pilot.ToUpperInvariant()}";
        if (!droneSprite)
        {
            droneSprite = SpriteManager.Get(NitroxClient.GameLogic.VehicleUpgrades.DroneStation.DroneTech);
        }
        loadBubble.SetIcon(droneSprite);
        loadBubble.SetIconShown(loaded && droneSprite);
        loadBubble.SetLit(loaded);
        loadBubble.HoverText = loadHover;
        loadBubble.HoverHint = loadHint;
        flyBubble.SetLit(canFly);
        flyBubble.HoverText = flyHover;
        flyBubble.HoverHint = flyHint;
    }

    public void Destroy()
    {
        if (group)
        {
            UnityEngine.Object.Destroy(group.gameObject);
        }
    }
}
