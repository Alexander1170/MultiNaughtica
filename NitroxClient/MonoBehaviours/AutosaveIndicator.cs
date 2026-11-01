using NitroxClient.GameLogic;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// The small "Saving..." indicator in the top left corner of every player's screen while the server saves (item a): a turning ring and a text, for
/// <see cref="AutosaveIndicatorRules.VISIBLE_SECONDS" />, fading in and out (<see cref="ServerSaving" /> packet, <c>ServerSavingProcessor</c>). No art of its own: the ring is drawn into a
/// texture at run time, the text uses the font of the engine and the language file of the game ("Nitrox_Saving").
/// </summary>
public class AutosaveIndicator : MonoBehaviour
{
    private const int RING_TEXTURE_SIZE = 64;
    private const float SPIN_DEGREES_PER_SECOND = 220f;

    private static AutosaveIndicator instance;

    private CanvasGroup group;
    private RectTransform spinner;
    private Text label;
    private float elapsed = AutosaveIndicatorRules.IDLE;

    /// <summary>Notices received by this client since the indicator was made (for the harness).</summary>
    public static int ShownCount { get; private set; }

    /// <summary>The <c>Number</c> of the latest notice (for the harness).</summary>
    public static int LastNumber { get; private set; }

    public static AutosaveIndicator Instance => instance;

    public float Alpha => group ? group.alpha : 0f;

    public bool IsShowing => !AutosaveIndicatorRules.IsDone(elapsed);

    public float SecondsSinceShown => elapsed;

    public string Text => label ? label.text : string.Empty;

    public Vector2 ScreenPosition => spinner ? (Vector2)spinner.position : Vector2.zero;

    /// <summary>
    /// The server announced a save: (re)start the show.
    /// </summary>
    public static void Show(int number)
    {
        if (!instance)
        {
            Create();
        }

        ShownCount++;
        LastNumber = number;
        instance.elapsed = AutosaveIndicatorRules.ElapsedForNewNotice(instance.elapsed);
        instance.label.text = Language.main ? Language.main.Get("Nitrox_Saving") : "Saving…";
    }

    private static void Create()
    {
        GameObject root = new("NitroxAutosaveIndicator");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<AutosaveIndicator>();
        instance.Build();
        Multiplayer.OnAfterMultiplayerEnd += Dispose;
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

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject spinnerObject = new("Ring", typeof(RectTransform), typeof(Image));
        spinnerObject.transform.SetParent(transform, false);
        spinner = (RectTransform)spinnerObject.transform;
        AnchorTopLeft(spinner, new Vector2(0.5f, 0.5f), new Vector2(40f, -40f), new Vector2(26f, 26f));
        Image ring = spinnerObject.GetComponent<Image>();
        ring.sprite = MakeRingSprite();
        ring.color = new Color(1f, 1f, 1f, 0.95f);
        ring.raycastTarget = false;

        GameObject labelObject = new("Label", typeof(RectTransform), typeof(Text), typeof(Shadow));
        labelObject.transform.SetParent(transform, false);
        AnchorTopLeft((RectTransform)labelObject.transform, new Vector2(0f, 0.5f), new Vector2(62f, -40f), new Vector2(240f, 30f));
        label = labelObject.GetComponent<Text>();
        label.font = FindFont();
        label.fontSize = 20;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.text = "Saving…";
        Shadow shadow = labelObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private static void AnchorTopLeft(RectTransform rect, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
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
        catch (System.Exception)
        {
            // fall through to the font of the operating system
        }

        return Font.CreateDynamicFontFromOSFont("Arial", 20);
    }

    /// <summary>
    /// A ring with a quarter missing, white, drawn into a small texture (no art file).
    /// </summary>
    private static Sprite MakeRingSprite()
    {
        Texture2D texture = new(RING_TEXTURE_SIZE, RING_TEXTURE_SIZE, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        float center = (RING_TEXTURE_SIZE - 1) / 2f;
        float inner = RING_TEXTURE_SIZE * 0.31f;
        float outer = RING_TEXTURE_SIZE * 0.45f;
        for (int y = 0; y < RING_TEXTURE_SIZE; y++)
        {
            for (int x = 0; x < RING_TEXTURE_SIZE; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                // soft edges of one pixel on both sides of the band
                float alpha = Mathf.Clamp01(distance - inner + 0.5f) * Mathf.Clamp01(outer - distance + 0.5f);
                float angle = Mathf.Atan2(dy, dx);
                if (angle > 0f && angle < Mathf.PI / 2f)
                {
                    alpha = 0f;
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, RING_TEXTURE_SIZE, RING_TEXTURE_SIZE), new Vector2(0.5f, 0.5f), RING_TEXTURE_SIZE);
    }

    public void Update()
    {
        if (!group)
        {
            return;
        }

        if (AutosaveIndicatorRules.IsDone(elapsed))
        {
            group.alpha = 0f;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        group.alpha = AutosaveIndicatorRules.Alpha(elapsed);
        spinner.Rotate(0f, 0f, -SPIN_DEGREES_PER_SECOND * Time.unscaledDeltaTime);
    }
}
