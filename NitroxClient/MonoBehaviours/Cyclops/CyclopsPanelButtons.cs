using System;
using System.Collections.Generic;
using System.Text;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// On the lighting panel of every Cyclops (the panel by the helm with the buttons for the interior lights and the floodlights): two more buttons of the same look, the <b>power share switch</b> (visible
/// while the Cyclops has the Power Share module) and the <b>tow hook</b> (while it has the Cyclops Tow Hook). They are copies of the panel's own floodlight button with their game scripts removed, their
/// click replaced and the icon changed, and they sit in the row after the panel's two. A button is lit while what it controls is on: the power share while the Cyclops' switch is on
/// (<see cref="PowerShareSwitch" />), the tow hook while the Cyclops tows (<see cref="VehicleTows" />, the real tow state of every client). Anybody aboard can press.
/// <para>
/// The panel is built by the game and its buttons are wired in the prefab, so the copy is made defensively: if the panel does not look as expected nothing is added and the hierarchy is written to the log
/// once (<see cref="Describe" /> is the same dump for the live check).
/// </para>
/// </summary>
public class CyclopsPanelButtons : MonoBehaviour
{
    private const float REFRESH_SECONDS = 0.25f;
    public const string SHARE_NAME = "NitroxPowerShareButton";
    public const string TOW_NAME = "NitroxTowHookButton";

    private CyclopsLightingPanel panel;
    private SubRoot cyclops;
    private PanelButton share;
    private PanelButton tow;
    private bool built;
    private bool failed;
    private float nextRefresh;
    private RectTransform second;
    private (float X, float Y) step;

    /// <summary>The buttons were copied from the panel's own (false while no module is installed yet, or when the panel did not look as expected).</summary>
    public bool Built => built;

    public bool BuildFailed => failed;

    public PanelButton PowerShareButton => share;
    public PanelButton TowButton => tow;

    private void Awake()
    {
        panel = GetComponent<CyclopsLightingPanel>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
        {
            return;
        }
        nextRefresh = Time.unscaledTime + REFRESH_SECONDS;
        Refresh();
    }

    private void Refresh()
    {
        if (!cyclops)
        {
            cyclops = panel && panel.cyclopsRoot ? panel.cyclopsRoot : GetComponentInParent<SubRoot>();
        }
        if (!cyclops)
        {
            return;
        }

        bool wantShare = PowerShare.IsAvailable(cyclops);
        bool wantTow = VehicleTows.IsButtonAvailable(cyclops);
        if (!built && !failed && (wantShare || wantTow))
        {
            Build();
        }
        if (!built)
        {
            return;
        }

        int[] slots = PanelButtonRules.AssignSlots(wantShare, wantTow);
        Place(share, slots[0]);
        Place(tow, slots[1]);

        if (share != null)
        {
            bool on = PowerShareSwitch.IsOn(cyclops);
            share.SetLit(on);
            share.HoverText = Language.main.Get(on ? "Nitrox_PowerShareHoverOn" : "Nitrox_PowerShareHoverOff");
            share.HoverHint = Language.main.Get(on ? "Nitrox_PowerShareHintOff" : "Nitrox_PowerShareHintOn");
        }
        if (tow != null)
        {
            string towed = VehicleTows.TowedName(cyclops);
            tow.SetLit(towed != null);
            tow.HoverText = towed == null ? Language.main.Get("Nitrox_TowHoverReady") : string.Format(Language.main.Get("Nitrox_TowHoverTowing"), towed);
            tow.HoverHint = Language.main.Get(towed == null ? "Nitrox_TowHintHook" : "Nitrox_TowHintLetGo");
        }
    }

    private void Place(PanelButton button, int slot)
    {
        if (button == null)
        {
            return;
        }
        button.SetVisible(slot > 0);
        if (slot > 0 && second)
        {
            (float x, float y) = PanelButtonRules.Slot(second.anchoredPosition.x, second.anchoredPosition.y, step, slot);
            button.Root.anchoredPosition = new Vector2(x, y);
        }
    }

    private void Build()
    {
        try
        {
            RectTransform first = ButtonRoot(panel.internalLightsImage);
            RectTransform floodlights = ButtonRoot(panel.externalLightsImage);
            RectTransform template = floodlights ? floodlights : first;
            if (!template || !template.parent)
            {
                Fail("the panel has no light button to copy");
                return;
            }

            bool sameRow = first && floodlights && first != floodlights && first.parent == floodlights.parent;
            second = template;
            float width = template.rect.width;
            step = sameRow
                ? PanelButtonRules.Step(first.anchoredPosition.x, first.anchoredPosition.y, floodlights.anchoredPosition.x, floodlights.anchoredPosition.y, width)
                : PanelButtonRules.Step(0f, 0f, 0f, 0f, width);

            Image templateIcon = panel.externalLightsImage ? panel.externalLightsImage : panel.internalLightsImage;
            string iconPath = RelativePath(template, templateIcon ? templateIcon.transform : template);

            share = PanelButton.Create(template, iconPath, SHARE_NAME, SpriteManager.Get(TechType.PowerCell, null), () => PowerShareSwitch.Press(panel));
            Sprite towIcon = SpriteManager.Get((TechType)CoopItems.CYCLOPS_TOW_HOOK, null);
            if (!towIcon)
            {
                towIcon = SpriteManager.Get(TechType.Cyclops, null);
            }
            tow = PanelButton.Create(template, iconPath, TOW_NAME, towIcon, () => VehicleTows.OnCyclopsButton(cyclops));
            built = share != null || tow != null;
            Log.Info($"[VehicleUpgrades] panel buttons built on {cyclops.name}: template={template.name} sameRow={sameRow} step=({step.X:F3},{step.Y:F3}) share={share != null} tow={tow != null}");
            if (!built)
            {
                Fail("the buttons could not be copied");
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            Log.Error(exception, "[VehicleUpgrades] panel buttons: could not be built");
        }
    }

    private void Fail(string reason)
    {
        failed = true;
        Log.Warn($"[VehicleUpgrades] panel buttons not added: {reason}. The panel is: {Describe(panel)}");
    }

    private void OnDestroy()
    {
        share?.Destroy();
        tow?.Destroy();
    }

    /// <summary>The root of the button an image belongs to: the nearest object at or above the image that has a button (or other selectable); the image itself when there is none.</summary>
    private static RectTransform ButtonRoot(Image image)
    {
        if (!image)
        {
            return null;
        }
        Selectable selectable = image.GetComponentInParent<Selectable>();
        Transform root = selectable ? selectable.transform : image.transform;
        return root as RectTransform;
    }

    /// <summary>The path from a copied root to the transform inside it that carries the icon ("" for the root itself).</summary>
    private static string RelativePath(Transform root, Transform child)
    {
        if (child == root)
        {
            return "";
        }
        List<string> parts = [];
        for (Transform t = child; t && t != root; t = t.parent)
        {
            parts.Add(t.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>For the live check and for a panel that does not look as expected: every object under the panel's canvas with its components and rectangle.</summary>
    public static string Describe(CyclopsLightingPanel panel)
    {
        if (!panel)
        {
            return "no panel";
        }
        StringBuilder text = new();
        Transform top = panel.transform;
        Canvas canvas = panel.GetComponentInChildren<Canvas>(true);
        if (canvas)
        {
            top = canvas.transform;
        }
        text.Append($"panel={panel.name} internalImage={Path(panel.internalLightsImage)} externalImage={Path(panel.externalLightsImage)} uiPanel={(panel.uiPanel ? panel.uiPanel.name : "none")}");
        Append(text, top, 0);
        return text.ToString();
    }

    private static string Path(Component component) => component ? component.transform.parent ? $"{component.transform.parent.name}/{component.name}" : component.name : "none";

    private static void Append(StringBuilder text, Transform transform, int depth)
    {
        if (depth > 6)
        {
            return;
        }
        text.Append("\n").Append(' ', depth * 2).Append(transform.name).Append(transform.gameObject.activeSelf ? "" : " (inactive)").Append(" [");
        bool first = true;
        foreach (Component component in transform.GetComponents<Component>())
        {
            text.Append(first ? "" : ", ").Append(component ? component.GetType().Name : "missing");
            first = false;
        }
        text.Append("]");
        if (transform is RectTransform rect)
        {
            text.Append($" pos=({rect.anchoredPosition.x:F1},{rect.anchoredPosition.y:F1}) size=({rect.rect.width:F1},{rect.rect.height:F1}) scale=({rect.localScale.x:F2},{rect.localScale.y:F2})");
        }
        foreach (Transform child in transform)
        {
            Append(text, child, depth + 1);
        }
    }

    /// <summary>One of the added buttons: the copy of the panel's button, its icon and its state.</summary>
    public sealed class PanelButton
    {
        private readonly Image icon;
        private readonly PanelButtonHover hover;

        private PanelButton(RectTransform root, Image icon, PanelButtonHover hover)
        {
            Root = root;
            this.icon = icon;
            this.hover = hover;
        }

        public RectTransform Root { get; }
        public bool Visible => Root && Root.gameObject.activeSelf;
        public bool Lit { get; private set; }

        public string HoverText
        {
            set => hover.Text = value;
        }

        public string HoverHint
        {
            set => hover.Hint = value;
        }

        /// <summary>Presses the button as a click on it would (for the live check, which cannot aim at a world-space button).</summary>
        public void Click()
        {
            Button button = Root ? Root.GetComponent<Button>() : null;
            if (button)
            {
                button.onClick.Invoke();
            }
        }

        public void SetVisible(bool visible)
        {
            if (Root && Root.gameObject.activeSelf != visible)
            {
                Root.gameObject.SetActive(visible);
            }
        }

        /// <summary>Puts another picture in the button (the drone's icon on the terminal's bubble).</summary>
        public void SetIcon(Sprite sprite)
        {
            if (icon && sprite && icon.sprite != sprite)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
            }
        }

        /// <summary>Shows the picture or leaves the button empty (the clicks still reach it).</summary>
        public void SetIconShown(bool shown)
        {
            if (icon && icon.enabled != shown)
            {
                icon.enabled = shown;
            }
        }

        public void SetLit(bool lit)
        {
            Lit = lit;
            if (icon)
            {
                float brightness = PanelButtonRules.IconBrightness(lit);
                icon.color = new Color(brightness, brightness, brightness, PanelButtonRules.IconOpacity(lit));
            }
        }

        public void Destroy()
        {
            if (Root)
            {
                UnityEngine.Object.Destroy(Root.gameObject);
            }
        }

        /// <summary>Copies a button of the panel: the copy keeps the look, loses the game's scripts and the wiring of the original, and gets this click and this icon.</summary>
        public static PanelButton Create(RectTransform template, string iconPath, string name, Sprite sprite, UnityEngine.Events.UnityAction onClick)
        {
            GameObject copy = UnityEngine.Object.Instantiate(template.gameObject, template.parent, false);
            copy.name = name;
            copy.SetActive(false);

            // The panel's own scripts (and what they wired up) must not run in the copy: it would toggle the lights or the like
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour && !IsUiLibrary(behaviour.GetType()))
                {
                    UnityEngine.Object.Destroy(behaviour);
                }
            }
            foreach (Animator animator in copy.GetComponentsInChildren<Animator>(true))
            {
                UnityEngine.Object.Destroy(animator);
            }

            RectTransform root = (RectTransform)copy.transform;
            Transform iconTransform = string.IsNullOrEmpty(iconPath) ? root : root.Find(iconPath);
            Image iconImage = iconTransform ? iconTransform.GetComponent<Image>() : copy.GetComponentInChildren<Image>(true);
            if (!iconImage)
            {
                UnityEngine.Object.Destroy(copy);
                return null;
            }
            if (sprite)
            {
                iconImage.sprite = sprite;
                iconImage.preserveAspect = true;
            }
            iconImage.raycastTarget = true;

            // A fresh click event drops the wiring that came with the copy (the UnityEvents of a prefab are serialized with their targets)
            Button button = root.GetComponent<Button>();
            if (!button)
            {
                foreach (Selectable other in copy.GetComponentsInChildren<Selectable>(true))
                {
                    UnityEngine.Object.Destroy(other);
                }
                button = copy.AddComponent<Button>();
                button.targetGraphic = iconImage;
            }
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            button.interactable = true;
            if (button.transition == Selectable.Transition.Animation)
            {
                // The copy has no animator any more
                button.transition = Selectable.Transition.ColorTint;
            }

            PanelButtonHover hover = copy.AddComponent<PanelButtonHover>();
            return new PanelButton(root, iconImage, hover);
        }

        private static bool IsUiLibrary(Type type)
        {
            string space = type.Namespace;
            return space != null && (space.StartsWith("UnityEngine.UI", StringComparison.Ordinal) || space.StartsWith("UnityEngine.EventSystems", StringComparison.Ordinal) || space.StartsWith("TMPro", StringComparison.Ordinal));
        }
    }

    /// <summary>While the pointer is on a button: its text under the pointer, like the game's other things to click.</summary>
    public sealed class PanelButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private bool hovering;

        public string Text { get; set; }
        public string Hint { get; set; }

        public void OnPointerEnter(PointerEventData eventData) => hovering = true;

        public void OnPointerExit(PointerEventData eventData) => hovering = false;

        private void OnDisable() => hovering = false;

        private void Update()
        {
            if (hovering && HandReticle.main && !string.IsNullOrEmpty(Text))
            {
                HandReticle.main.SetText(HandReticle.TextType.Hand, Text, false, GameInput.Button.LeftHand);
                HandReticle.main.SetText(HandReticle.TextType.HandSubscript, Hint ?? string.Empty, false, GameInput.Button.None);
                HandReticle.main.SetIcon(HandReticle.IconType.Hand, 1f);
            }
        }
    }
}
