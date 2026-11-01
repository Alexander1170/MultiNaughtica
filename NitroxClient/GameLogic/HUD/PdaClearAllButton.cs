using System;
using NitroxClient.MonoBehaviours;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.GameLogic.HUD;

/// <summary>
/// The "Clear all" button on the header row of the PDA's databank tab (item b): one click clears every yellow "new" badge of this player (<see cref="PdaBadges.ClearAllEncyclopediaBadges" />,
/// the game's own <c>NotificationManager.Remove</c> per badge, local only). No art of its own: a plain <c>Button</c> with an <c>Image</c> and a <c>TextMeshProUGUI</c>, made at run time from the
/// sprites and the font of the databank's own rows, so that it looks like them and follows their fonts for other languages.
/// <para>
/// It is a child of the tab's <c>content</c> (the canvas group that the PDA fades and switches off for a tab that is not shown), never of the list (the list's rows are pooled and re-ordered).
/// It is made once, when the PDA opens (<c>uGUI_EncyclopediaTab_OnOpenPDA_Patch</c>; <c>OnUpdate</c> also runs from the PDA's warm-up in <c>Player.Awake</c>, when there is no button yet and
/// nothing may be made), placed behind the header text each time the tab is shown, and hidden when there is nothing to clear or when the primary device is a gamepad
/// (<see cref="PdaBadgeRules.ShouldShowClearAll" />).
/// </para>
/// <para>
/// Every entry point is exception safe: they run inside postfixes of PDA methods, and an exception there would break opening the PDA. A failure while the button is made destroys the half-made
/// object; a failure while it is placed or updated hides it and stops further attempts until the PDA opens again (one log line, not one per frame).
/// </para>
/// </summary>
public static class PdaClearAllButton
{
    private const string OBJECT_NAME = "NitroxClearAllBadges";
    private const string LABEL_KEY = "Nitrox_PdaClearAll";
    private const string LABEL_FALLBACK = "Clear all";
    private const float GAP = 24f;
    private const float PADDING = 16f;
    private const float MIN_WIDTH = 72f;
    private const float MIN_HEIGHT = 24f;

    private static GameObject buttonObject;
    private static RectTransform buttonRect;
    private static TextMeshProUGUI label;
    private static bool layoutPending;
    private static bool wasOpen;
    private static bool overhangLogged;
    private static bool failed;

    /// <summary>Whether the button has been made (it is gone with the session).</summary>
    public static bool Exists => buttonObject;

    /// <summary>Whether the button is switched on (visible when the databank tab is the one that is shown).</summary>
    public static bool IsActive => buttonObject && buttonObject.activeInHierarchy;

    public static Button ButtonComponent => buttonObject ? buttonObject.GetComponent<Button>() : null;

    public static RectTransform Rect => buttonRect;

    public static string LabelText => label ? label.text : string.Empty;

    /// <summary>
    /// The PDA opened: make the button if it does not exist yet, and have it placed anew the next time the tab is shown.
    /// </summary>
    public static void OnOpenPda(uGUI_EncyclopediaTab tab)
    {
        try
        {
            failed = false;
            if (!buttonObject)
            {
                Create(tab);
            }

            layoutPending = true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PDA] the Clear all button of the databank could not be made");
        }
    }

    /// <summary>
    /// Every frame of the PDA: show or hide the button by the number of badges, and place it the first time the tab is shown after the PDA opened.
    /// </summary>
    public static void OnUpdate(uGUI_EncyclopediaTab tab, bool isOpen)
    {
        if (!buttonObject || failed)
        {
            return;
        }

        try
        {
            if (!isOpen)
            {
                wasOpen = false;
                return;
            }

            bool show = PdaBadgeRules.ShouldShowClearAll(tab.notificationsCount, GameInput.IsPrimaryDeviceGamepad());
            if (buttonObject.activeSelf != show)
            {
                buttonObject.SetActive(show);
            }

            if (show && (layoutPending || !wasOpen))
            {
                // switched on first (the label's text component only measures once it is active), placed in the same frame before anything is drawn; a failure hides the button again below
                Layout(tab);
                layoutPending = false;
            }

            wasOpen = true;
        }
        catch (Exception exception)
        {
            failed = true;
            HideQuietly();
            Log.Error(exception, "[PDA] the Clear all button of the databank could not be placed; it stays hidden until the PDA is opened again");
        }
    }

    /// <summary>
    /// The language changed: the label follows, and the width with it.
    /// </summary>
    public static void OnLanguageChanged()
    {
        try
        {
            if (!buttonObject || !label)
            {
                return;
            }

            label.text = LabelOrFallback();
            layoutPending = true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PDA] the Clear all button of the databank could not follow the language");
        }
    }

    private static void HideQuietly()
    {
        try
        {
            if (buttonObject)
            {
                buttonObject.SetActive(false);
            }
        }
        catch (Exception)
        {
            // nothing more can be done about a button that cannot even be switched off
        }
    }

    private static string LabelOrFallback()
    {
        return Language.main && Language.main.TryGet(LABEL_KEY, out string text) && !string.IsNullOrEmpty(text) ? text : LABEL_FALLBACK;
    }

    private static void Create(uGUI_EncyclopediaTab tab)
    {
        if (!tab || !tab.content)
        {
            Log.Warn("[PDA] the databank tab has no content to put the Clear all button on");
            return;
        }

        RectTransform parent = (RectTransform)tab.content.transform;
        GameObject root = new(OBJECT_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        // built while it is active (the text component sets itself up when it is added to an active object); a failure destroys it again within the frame, before anything is drawn
        try
        {
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            buttonRect = (RectTransform)root.transform;
            buttonRect.SetAsLastSibling();
            // whatever layout component the prefab's content may have must not take the button's rectangle over
            root.GetComponent<LayoutElement>().ignoreLayout = true;

            UISpriteData sprites = tab.entryNodeSprites;
            Image image = root.GetComponent<Image>();
            image.raycastTarget = true;
            image.type = Image.Type.Sliced;
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (sprites != null && sprites.normal)
            {
                image.sprite = sprites.normal;
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    highlightedSprite = sprites.hovered ? sprites.hovered : sprites.normal,
                    pressedSprite = sprites.selected ? sprites.selected : sprites.normal,
                    selectedSprite = sprites.normal,
                    disabledSprite = sprites.normal
                };
            }
            else
            {
                // the rows' sprites are not there (they should be): a plain dark button that still shows the mouse is over it
                image.color = new Color(0.1f, 0.12f, 0.15f, 0.9f);
                button.transition = Selectable.Transition.ColorTint;
            }

            GameObject labelObject = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = root.layer;
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            CopyLook(tab, label);
            label.text = LabelOrFallback();

            button.onClick.AddListener(OnClicked);
            root.SetActive(false);
        }
        catch (Exception)
        {
            UnityEngine.Object.Destroy(root);
            buttonRect = null;
            label = null;
            throw;
        }

        buttonObject = root;
        Multiplayer.OnAfterMultiplayerEnd += Dispose;
        Log.Info("[PDA] the Clear all button of the databank was made");
    }

    /// <summary>
    /// The font, size and colour of the databank's own rows, taken from the row prefab so that no asset has to be named here.
    /// </summary>
    private static void CopyLook(uGUI_EncyclopediaTab tab, TextMeshProUGUI target)
    {
        uGUI_ListEntry row = tab.prefabEntry ? tab.prefabEntry.GetComponent<uGUI_ListEntry>() : null;
        TextMeshProUGUI source = row ? row.text : null;
        if (!source)
        {
            source = tab.encyclopediaLabel;
        }

        if (!source)
        {
            return;
        }

        target.font = source.font;
        target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontSize = source.fontSize;
        target.color = source.color;
    }

    /// <summary>
    /// Right behind the end of the header text ("Databank"), centred on its line, as wide as the label needs but not past the right edge of the list, and not taller than the room between the
    /// header line and the top of the list. The numbers come from the live rectangles of the tab (the prefab's layout cannot be read from the game's code), converted into the local space of the
    /// tab's content. A measure that is not a number leaves the button where it is (and hidden).
    /// </summary>
    private static void Layout(uGUI_EncyclopediaTab tab)
    {
        RectTransform parent = (RectTransform)buttonRect.parent;
        TextMeshProUGUI header = tab.encyclopediaLabel;

        float textLeft;
        float textRight;
        float centerY;
        if (header)
        {
            header.ForceMeshUpdate();
            Bounds bounds = header.textBounds;
            bool measured = bounds.size.x > 0.01f;
            Vector3 left = measured
                ? new Vector3(bounds.min.x, bounds.center.y, 0f)
                : new Vector3(header.rectTransform.rect.xMin, header.rectTransform.rect.center.y, 0f);
            Vector3 right = measured
                ? new Vector3(bounds.max.x, bounds.center.y, 0f)
                : new Vector3(header.rectTransform.rect.xMin + header.preferredWidth, header.rectTransform.rect.center.y, 0f);
            Vector3 localLeft = parent.InverseTransformPoint(header.rectTransform.TransformPoint(left));
            Vector3 localRight = parent.InverseTransformPoint(header.rectTransform.TransformPoint(right));
            textLeft = localLeft.x;
            textRight = localRight.x;
            centerY = localRight.y;
        }
        else
        {
            textLeft = parent.rect.center.x;
            textRight = parent.rect.center.x;
            centerY = parent.rect.yMax - 40f;
        }

        float listLeft = float.NaN;
        float listRight = float.NaN;
        float listTop = float.NegativeInfinity;
        if (tab.listScrollRect)
        {
            Vector3[] corners = new Vector3[4];
            ((RectTransform)tab.listScrollRect.transform).GetWorldCorners(corners);
            listLeft = parent.InverseTransformPoint(corners[0]).x;
            listRight = parent.InverseTransformPoint(corners[2]).x;
            listTop = parent.InverseTransformPoint(corners[1]).y;
        }

        float wantedWidth = label.GetPreferredValues(label.text).x + PADDING * 2f;
        if (!PdaBadgeRules.IsFinite(textLeft) || !PdaBadgeRules.IsFinite(textRight) || !PdaBadgeRules.IsFinite(centerY) || !PdaBadgeRules.IsFinite(wantedWidth))
        {
            throw new InvalidOperationException($"the measures of the databank are not numbers (text {textLeft} to {textRight}, line {centerY}, label {wantedWidth})");
        }

        // The header ("Databank") is centred on the tab and the list is a column on its left (first live run): the button goes on the header row at the left end of the list, clear of the header text.
        // Without such a layout it goes right behind the header text, as far as the right edge of the tab's content.
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.ShouldPlaceOverList(listLeft, listRight, textLeft, GAP, MIN_WIDTH)
            ? PdaBadgeRules.PlaceClearAllButtonOverList(listLeft, listRight, textLeft, GAP, wantedWidth, MIN_WIDTH)
            : PdaBadgeRules.PlaceClearAllButton(textRight, GAP, wantedWidth, parent.rect.xMax, MIN_WIDTH);
        if (!placement.Fits && !overhangLogged)
        {
            overhangLogged = true;
            Log.Warn($"[PDA] the Clear all button does not fit on the header row (text {textLeft:F0} to {textRight:F0}, list {listLeft:F0} to {listRight:F0}): it overhangs");
        }

        buttonRect.anchorMin = parent.pivot;
        buttonRect.anchorMax = parent.pivot;
        buttonRect.pivot = new Vector2(0f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(placement.Left, centerY);
        buttonRect.sizeDelta = new Vector2(placement.Width, PdaBadgeRules.ClampButtonHeight(ButtonHeight(tab), centerY, listTop, MIN_HEIGHT));
        buttonRect.SetAsLastSibling();
    }

    private static float ButtonHeight(uGUI_EncyclopediaTab tab)
    {
        uGUI_ListEntry row = tab.prefabEntry ? tab.prefabEntry.GetComponent<uGUI_ListEntry>() : null;
        float rowHeight = row && row.layoutElement ? row.layoutElement.preferredHeight : 0f;
        return rowHeight > 0f ? rowHeight : Mathf.Max(30f, label.fontSize * 1.8f);
    }

    private static void OnClicked()
    {
        try
        {
            PdaBadges.ClearAllEncyclopediaBadges();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PDA] the Clear all button of the databank failed");
        }
    }

    private static void Dispose()
    {
        Multiplayer.OnAfterMultiplayerEnd -= Dispose;
        if (buttonObject)
        {
            UnityEngine.Object.Destroy(buttonObject);
        }

        buttonObject = null;
        buttonRect = null;
        label = null;
        wasOpen = false;
        layoutPending = false;
        overhangLogged = false;
        failed = false;
    }
}
