using System;
using NitroxClient.GameLogic.Navigator;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The numbers of the route for the players aboard a Cyclops that has one (docs/coop/VEHICLE_UPGRADES.md, station 1): an arrow that points to the next waypoint (it turns with the angle between the Cyclops' bow and
/// the waypoint), "Route 2/4  340 m  ETA 1:12" and how many degrees to turn, to port or to starboard, and "OFF COURSE" in orange when the Cyclops is. A small overlay canvas at the top of the screen, made on first use,
/// that hides itself when the route is finished, the player leaves, the PDA is open or the navigator console is (which has the route on its map). Fed by <see cref="RouteGuide" />.
/// </summary>
public class RouteHud : MonoBehaviour
{
    private const float HIDE_AFTER_SECONDS = 0.6f;

    private static RouteHud instance;

    private CanvasGroup group;
    private RectTransform arrow;
    private Image arrowImage;
    private Text title;
    private Text detail;
    private Text warning;
    private CanvasGroup warningGroup;
    private float lastFed = float.NegativeInfinity;
    private float lastWarning = float.NegativeInfinity;
    private int shownRevision = -1;

    /// <summary>The guide of the Cyclops that the local player is aboard tells what to show; not heard from for a moment, the widget hides itself.</summary>
    public static void Feed(RouteGuidance guidance)
    {
        if (!instance)
        {
            GameObject root = new("NitroxRouteHud");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<RouteHud>();
            instance.Build();
            Multiplayer.OnAfterMultiplayerEnd += Dispose;
        }
        instance.Show(guidance);
    }

    /// <summary>The clearance warning of the Cyclops that the local player is aboard ("Clearance 4 m, port"): shown at the top of the screen for as long as it is fed; orange, red when it is dangerous.</summary>
    public static void FeedWarning(string text, ClearanceLevel level)
    {
        if (!instance)
        {
            GameObject root = new("NitroxRouteHud");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<RouteHud>();
            instance.Build();
            Multiplayer.OnAfterMultiplayerEnd += Dispose;
        }
        instance.ShowWarning(text, level);
    }

    private void ShowWarning(string text, ClearanceLevel level)
    {
        lastWarning = Time.unscaledTime;
        bool pdaOpen = false;
        try
        {
            pdaOpen = Player.main && Player.main.GetPDA() != null && Player.main.GetPDA().isInUse;
        }
        catch (Exception)
        {
            // no PDA to ask
        }
        warning.text = text;
        warning.color = level == ClearanceLevel.Danger ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.65f, 0.15f);
        // the console shows the same line under its title
        warningGroup.alpha = pdaOpen || GunnerConsole.AnyConsoleOpen || string.IsNullOrEmpty(text) ? 0f : 1f;
    }

    private static void Dispose()
    {
        Multiplayer.OnAfterMultiplayerEnd -= Dispose;
        if (instance)
        {
            Destroy(instance.gameObject);
        }
        instance = null;
    }

    private void Update()
    {
        if (group && group.alpha > 0f && Time.unscaledTime - lastFed > HIDE_AFTER_SECONDS)
        {
            group.alpha = 0f;
            shownRevision = -1;
        }
        if (warningGroup && warningGroup.alpha > 0f && Time.unscaledTime - lastWarning > HIDE_AFTER_SECONDS)
        {
            warningGroup.alpha = 0f;
        }
    }

    private void Show(RouteGuidance guidance)
    {
        lastFed = Time.unscaledTime;
        bool pdaOpen = false;
        try
        {
            pdaOpen = Player.main && Player.main.GetPDA() != null && Player.main.GetPDA().isInUse;
        }
        catch (Exception)
        {
            // no PDA to ask
        }
        if (!guidance.Active || pdaOpen || GunnerConsole.AnyConsoleOpen)
        {
            group.alpha = 0f;
            shownRevision = -1;
            return;
        }
        group.alpha = 1f;
        // the guide works out its numbers ten times a second and is fed here every frame: the texts are made again only for new numbers
        if (guidance.Revision == shownRevision)
        {
            return;
        }
        shownRevision = guidance.Revision;
        Color color = guidance.OffCourse ? new Color(1f, 0.55f, 0.1f) : new Color(0.55f, 0.95f, 1f);
        arrowImage.color = color;
        arrow.localEulerAngles = new Vector3(0f, 0f, -guidance.Yaw);
        string eta = float.IsNaN(guidance.Eta) ? string.Empty : $"   ETA {(int)(guidance.Eta / 60f)}:{(int)(guidance.Eta % 60f):00}";
        title.text = $"Route {guidance.Index + 1}/{guidance.Count}   {ContactRules.Distance(guidance.HullDistance)}{eta}";
        title.color = color;
        string turn = Mathf.Abs(guidance.Yaw) < 3f ? "straight ahead" : $"{Mathf.Abs(guidance.Yaw):0} deg to {(guidance.Yaw >= 0f ? "starboard" : "port")}";
        string vertical = Mathf.Abs(guidance.Pitch) < 8f ? string.Empty : guidance.Pitch > 0f ? ", up" : ", down";
        detail.text = guidance.OffCourse ? $"OFF COURSE   {turn}{vertical}" : $"{turn}{vertical}";
        detail.color = guidance.OffCourse ? new Color(1f, 0.55f, 0.1f) : Color.white;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30500;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // The route's arrow and texts hide with a group of their own: a group on the canvas itself would hide the clearance warning too (an alpha of 0 above it multiplies every child's)
        GameObject groupObject = new("Route", typeof(RectTransform), typeof(CanvasGroup));
        groupObject.transform.SetParent(transform, false);
        RectTransform groupRect = (RectTransform)groupObject.transform;
        groupRect.anchorMin = Vector2.zero;
        groupRect.anchorMax = Vector2.one;
        groupRect.offsetMin = Vector2.zero;
        groupRect.offsetMax = Vector2.zero;
        group = groupObject.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject arrowObject = new("Arrow", typeof(RectTransform), typeof(Image));
        arrowObject.transform.SetParent(groupObject.transform, false);
        arrow = (RectTransform)arrowObject.transform;
        Anchor(arrow, new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(60f, 60f));
        arrowImage = arrowObject.GetComponent<Image>();
        arrowImage.sprite = NavigatorIcons.Get(NavigatorIcons.Shape.Triangle);
        arrowImage.raycastTarget = false;

        title = MakeText("Title", 28, -140f, groupObject.transform);
        detail = MakeText("Detail", 22, -176f, groupObject.transform);
        warning = MakeText("Warning", 30, -214f, transform);
        warningGroup = warning.gameObject.AddComponent<CanvasGroup>();
        warningGroup.alpha = 0f;
        warningGroup.interactable = false;
        warningGroup.blocksRaycasts = false;
    }

    private Text MakeText(string name, int size, float y, Transform parent)
    {
        GameObject textObject = new(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        Anchor(rect, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 34f));
        Text text = textObject.GetComponent<Text>();
        text.font = FindFont();
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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
        catch (Exception)
        {
            // fall through to the font of the operating system
        }
        return Font.CreateDynamicFontFromOSFont("Arial", 20);
    }
}
