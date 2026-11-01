using System.Collections.Generic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Engineer;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The panel of the engineer console that everybody sees on the engine in the engine room (<see cref="EngineerConsole" />): a dark plate with a lit edge, the name of the station, the route the engineer has set and who
/// sits at the console. Built at run time from primitives (the game's meshes cannot be read offline; the rider bar of the Seamoth is made the same way), facing the walkway on the port side. It is only a sign: the
/// hand target in front of it is what the player uses. The words are on a world-space canvas, not on <c>TextMesh</c> objects: the font material of a text mesh is drawn without a depth test, which would show the words
/// through the hull and the walls from the helm or from outside.
/// </summary>
public class EngineerPanelProp : MonoBehaviour
{
    private const float PIXELS_PER_METRE = 1000f;

    private static readonly Color PlateColor = new(0.02f, 0.08f, 0.10f, 0.92f);
    private static readonly Color EdgeColor = new(0.3f, 0.9f, 1f, 1f);
    private static readonly Color TitleColor = new(0.6f, 1f, 0.9f, 1f);

    private readonly List<Material> ownMaterials = [];
    private Text routeLine;
    private Text seatLine;
    private EngineerRoute? shownRoute;
    private string shownOccupant;

    /// <summary>
    /// Builds the panel on the Cyclops (a child of <paramref name="cyclops" />, at the place of <see cref="EngineerConsoleLayout.PanelCenter" />, facing the walkway). Null when it could not be built (nothing is left
    /// behind then).
    /// </summary>
    public static EngineerPanelProp Build(Transform cyclops)
    {
        GameObject root = new("NitroxEngineerPanel");
        root.transform.SetParent(cyclops, false);
        (float px, float py, float pz) = EngineerConsoleLayout.PanelCenter;
        root.transform.localPosition = new Vector3(px, py, pz);
        // A quad and a canvas are seen from the side that their back (+z) points away from: +z points to starboard (-x), at the engine, so that a player in the walkway on the port side sees the front
        root.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        EngineerPanelProp prop = root.AddComponent<EngineerPanelProp>();
        try
        {
            prop.Construct();
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] engineer console: the panel on the engine could not be built");
            Destroy(root);
            return null;
        }
        return prop;
    }

    private void OnDestroy()
    {
        foreach (Material material in ownMaterials)
        {
            if (material)
            {
                Destroy(material);
            }
        }
        ownMaterials.Clear();
    }

    private void Construct()
    {
        (float width, float height) = EngineerConsoleLayout.PanelSize;
        Material material = RouteLineMaterial.Get();
        AddQuad("Plate", Vector3.zero, new Vector3(width, height, 1f), PlateColor, material);
        const float edge = 0.02f;
        AddQuad("EdgeTop", new Vector3(0f, (height - edge) / 2f, -0.003f), new Vector3(width, edge, 1f), EdgeColor, material);
        AddQuad("EdgeBottom", new Vector3(0f, -(height - edge) / 2f, -0.003f), new Vector3(width, edge, 1f), EdgeColor, material);
        AddQuad("EdgeLeft", new Vector3(-(width - edge) / 2f, 0f, -0.003f), new Vector3(edge, height, 1f), EdgeColor, material);
        AddQuad("EdgeRight", new Vector3((width - edge) / 2f, 0f, -0.003f), new Vector3(edge, height, 1f), EdgeColor, material);

        // One canvas in front of the plate; a pixel of it is a millimetre of the Cyclops
        GameObject canvasObject = new("Text", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // After the render mode: a new canvas is an overlay one and drives its own rect (scale, position and size of the screen), and the switch to world space keeps what it set
        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.localPosition = new Vector3(0f, 0f, -0.008f);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one / PIXELS_PER_METRE;
        canvasRect.sizeDelta = new Vector2(width * PIXELS_PER_METRE, height * PIXELS_PER_METRE);

        AddText(canvasRect, "Title", height * 0.28f, 64, TitleColor).text = "ENGINEER";
        routeLine = AddText(canvasRect, "Route", 0f, 46, Color.white);
        seatLine = AddText(canvasRect, "Seat", -height * 0.27f, 38, new Color(0.8f, 0.95f, 1f));
    }

    /// <summary>Shows the route and who is at the console (only when they changed).</summary>
    public void Show(EngineerRoute route, string occupant)
    {
        if (route == shownRoute && occupant == shownOccupant)
        {
            return;
        }
        shownRoute = route;
        shownOccupant = occupant;
        if (routeLine)
        {
            routeLine.text = $"Power: {EngineerText.RouteName(route)}";
        }
        if (seatLine)
        {
            seatLine.text = string.IsNullOrEmpty(occupant) ? "free" : occupant;
        }
    }

    private void AddQuad(string partName, Vector3 localPosition, Vector3 scale, Color color, Material shared)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Quad);
        part.name = partName;
        Collider collider = part.GetComponent<Collider>();
        if (collider)
        {
            Destroy(collider);
        }
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        if (shared)
        {
            Material own = new(shared) { color = color };
            ownMaterials.Add(own);
            renderer.sharedMaterial = own;
        }
    }

    /// <param name="yMetres">Where the line is on the panel, up from its middle, in metres.</param>
    private static Text AddText(RectTransform canvasRect, string partName, float yMetres, int fontPixels, Color color)
    {
        GameObject part = new(partName, typeof(RectTransform), typeof(Text));
        part.transform.SetParent(canvasRect, false);
        RectTransform rect = (RectTransform)part.transform;
        rect.sizeDelta = new Vector2(canvasRect.sizeDelta.x, fontPixels * 1.5f);
        rect.anchoredPosition = new Vector2(0f, yMetres * PIXELS_PER_METRE);
        Text text = part.GetComponent<Text>();
        text.font = FindFont();
        text.fontSize = fontPixels;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
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
