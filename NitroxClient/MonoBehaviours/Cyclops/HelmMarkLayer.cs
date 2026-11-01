using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.Extensions;
using NitroxClient.GameLogic.Navigator;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The marks of the navigators on the terrain hologram in the build room (<see cref="CyclopsTerrainSonar" />): a pin for every mark of this Cyclops, standing where the hologram puts that place
/// (<see cref="TerrainHologramAligner.WorldToMap" />), turned to face the player, and kept on the rim, smaller, when the mark is beyond the hologram's range. The pins are world-space uGUI images (a plain
/// canvas with the game's own UI shader, no art asset and no shader of our own), sized from the hologram's own radius so that they fit whatever size the display has. One component on every real Cyclops, which
/// does nothing while the hologram is not shown (nobody aboard, no module) or the Cyclops has no marks.
/// </summary>
public class HelmMarkLayer : MonoBehaviour
{
    private const float PIN_PIXELS = 30f;
    private const float PIN_FRACTION_OF_RIM = 0.08f;
    private const float REFERENCE_PIXELS = 100f;

    private readonly Dictionary<string, Image> pins = [];
    private readonly List<string> gone = [];
    private CyclopsTerrainSonar sonar;
    private SubRoot subRoot;
    private NitroxId cyclopsId;
    private Canvas canvas;
    private Func<TerrainHologram> source;
    private bool hideWhileNavigating = true;
    private string canvasName = "NitroxHelmMarks";

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        sonar = GetComponent<CyclopsTerrainSonar>();
        if (sonar)
        {
            source = () => sonar.Hologram;
        }
    }

    /// <summary>
    /// Makes this layer draw on another hologram of the same Cyclops (the helm's copy of the map, <see cref="CyclopsHelmMap" />) instead of the build room's. The navigator's console draws the marks on its own
    /// overlay over the build room's hologram, which is why that layer steps aside while it is open; the helm's copy has no console over it, so its layer does not.
    /// </summary>
    public void Bind(SubRoot cyclops, Func<TerrainHologram> hologramSource, bool hideWhileConsoleOpen, string name)
    {
        subRoot = cyclops;
        source = hologramSource;
        hideWhileNavigating = hideWhileConsoleOpen;
        canvasName = name;
    }

    private void OnDestroy()
    {
        if (canvas)
        {
            Destroy(canvas.gameObject);
        }
    }

    private void LateUpdate()
    {
        TerrainHologram hologram = source != null ? source() : null;
        if (hologram == null || !hologram.Exists || !hologram.Shown || !subRoot || cyclopsId == null && !subRoot.TryGetNitroxId(out cyclopsId))
        {
            HidePins();
            return;
        }
        if (hideWhileNavigating && NavigatorConsole.Current && NavigatorConsole.Current.SubRoot == subRoot)
        {
            // the console draws the marks and the route on its own overlay, over this same hologram: not twice
            HidePins();
            return;
        }
        IReadOnlyList<NavMark> marks = NavMarks.MarksOf(cyclopsId);
        IReadOnlyList<NavWaypoint> route = NavRoute.PointsOf(cyclopsId);
        if (marks.Count == 0 && pins.Count == 0 && route.Count == 0 && !routeLine)
        {
            return;
        }
        Camera camera = MainCamera.camera;
        TerrainHologramAligner aligner = hologram.Aligner;
        if (!camera || !aligner || !aligner.Anchor)
        {
            HidePins();
            return;
        }
        EnsureCanvas(aligner.Anchor);

        // the rim of the disc: what the hologram was built for, and no farther than its terrain is drawn (the zoom of a navigator makes a metre bigger, so the range reaches beyond the disc)
        float rim = Mathf.Max(0.0001f, Mathf.Min(hologram.Range * aligner.WorldUnitsPerMetreNow, hologram.DiscRadiusWorld));
        float pinWorldSize = rim * PIN_FRACTION_OF_RIM;
        // the canvas is a child of the display, whose own scale is not necessarily 1: the pins are sized in world units
        canvas.transform.localScale = Vector3.one * (pinWorldSize / PIN_PIXELS / Mathf.Max(0.0001f, aligner.Anchor.lossyScale.x));
        pinsUsed.Clear();
        Vector3 middle = aligner.Anchor.position;
        foreach (NavMark mark in marks)
        {
            Vector3 point = aligner.WorldToMap(mark.Position);
            Vector3 offset = point - middle;
            bool beyond = offset.magnitude > rim;
            if (beyond)
            {
                point = middle + offset.normalized * rim;
            }
            Image pin = GetPin(mark);
            pinsUsed.Add(mark.Key);
            pin.rectTransform.position = point;
            pin.rectTransform.rotation = camera.transform.rotation;
            pin.rectTransform.localScale = Vector3.one * (beyond ? 0.6f : 1f);
            Color color = ColorOf(mark.Kind);
            color.a = beyond ? 0.55f : 1f;
            pin.color = color;
        }
        UpdateRoute(aligner, route, middle, rim, pinWorldSize);
        gone.Clear();
        foreach (KeyValuePair<string, Image> pair in pins)
        {
            if (!pinsUsed.Contains(pair.Key))
            {
                gone.Add(pair.Key);
            }
        }
        foreach (string key in gone)
        {
            if (pins[key])
            {
                Destroy(pins[key].gameObject);
            }
            pins.Remove(key);
        }
    }

    private readonly HashSet<string> pinsUsed = [];
    private LineRenderer routeLine;

    /// <summary>The route on the hologram: a line from the middle (the Cyclops) through the waypoints still to go to, kept on the rim.</summary>
    private void UpdateRoute(TerrainHologramAligner aligner, IReadOnlyList<NavWaypoint> route, Vector3 middle, float rim, float pinWorldSize)
    {
        int next = NavRoute.NextOf(cyclopsId);
        int remaining = Mathf.Max(0, route.Count - next);
        Material material = remaining > 0 ? RouteLineMaterial.Get() : null;
        if (remaining == 0 || !material)
        {
            if (routeLine)
            {
                routeLine.enabled = false;
            }
            return;
        }
        if (!routeLine)
        {
            GameObject lineObject = new("NitroxHelmRoute");
            lineObject.transform.SetParent(aligner.Anchor, false);
            routeLine = lineObject.AddComponent<LineRenderer>();
            routeLine.useWorldSpace = true;
            routeLine.numCapVertices = 2;
            routeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            routeLine.receiveShadows = false;
            routeLine.sharedMaterial = material;
        }
        routeLine.enabled = true;
        routeLine.positionCount = remaining + 1;
        routeLine.SetPosition(0, middle);
        for (int i = 0; i < remaining; i++)
        {
            Vector3 point = aligner.WorldToMap(route[next + i].Position);
            Vector3 offset = point - middle;
            if (offset.magnitude > rim)
            {
                point = middle + offset.normalized * rim;
            }
            routeLine.SetPosition(i + 1, point);
        }
        routeLine.widthMultiplier = pinWorldSize * 0.18f;
        Color color = new(0.4f, 1f, 1f, 0.9f);
        routeLine.startColor = color;
        routeLine.endColor = color;
    }

    private void EnsureCanvas(Transform anchor)
    {
        if (canvas)
        {
            return;
        }
        GameObject canvasObject = new(canvasName, typeof(RectTransform));
        canvasObject.transform.SetParent(anchor, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = (RectTransform)canvasObject.transform;
        rect.sizeDelta = new Vector2(REFERENCE_PIXELS, REFERENCE_PIXELS);
    }

    private Image GetPin(NavMark mark)
    {
        if (pins.TryGetValue(mark.Key, out Image existing) && existing)
        {
            return existing;
        }
        GameObject pinObject = new($"Pin {mark.Key}", typeof(RectTransform), typeof(Image));
        pinObject.transform.SetParent(canvas.transform, false);
        RectTransform rect = (RectTransform)pinObject.transform;
        rect.sizeDelta = new Vector2(PIN_PIXELS * 0.8f, PIN_PIXELS);
        Image image = pinObject.GetComponent<Image>();
        image.sprite = NavigatorIcons.Get(NavigatorIcons.Shape.Pin);
        image.raycastTarget = false;
        pins[mark.Key] = image;
        return image;
    }

    private void HidePins()
    {
        if (routeLine)
        {
            routeLine.enabled = false;
        }
        if (canvas && canvas.gameObject.activeSelf && pins.Count > 0)
        {
            foreach (Image pin in pins.Values)
            {
                if (pin)
                {
                    Destroy(pin.gameObject);
                }
            }
            pins.Clear();
        }
    }

    private static Color ColorOf(byte kind)
    {
        return kind switch
        {
            CyclopsNavRules.MARK_HAZARD => new Color(1f, 0.55f, 0.1f),
            CyclopsNavRules.MARK_TARGET => new Color(1f, 0.85f, 0.2f),
            CyclopsNavRules.MARK_THREAT => new Color(1f, 0.25f, 0.2f),
            CyclopsNavRules.MARK_RESOURCE => new Color(0.25f, 0.9f, 0.7f),
            _ => new Color(0.3f, 0.85f, 1f)
        };
    }
}
