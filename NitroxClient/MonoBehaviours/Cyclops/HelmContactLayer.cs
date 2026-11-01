using System;
using System.Collections.Generic;
using NitroxClient.GameLogic.Navigator;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The contacts of the navigator's map on the helm's copy of it (<see cref="CyclopsHelmMap" />): the Cyclops itself at the middle (its outline, bow up), the teammates, the vehicles, the creatures that the
/// map warns about, the beacons, the bases and the lifepods, each with the shape and colour that the console's overlay gives it (<see cref="NavigatorIcons" />, <see cref="NavigatorContacts" />), standing where the
/// hologram puts that place (<see cref="TerrainHologramAligner.WorldToMap" />), turned to face the player, and kept on the rim, smaller and fainter, when beyond the map's range. Like the marks
/// (<see cref="HelmMarkLayer" />) they are world-space uGUI images with no art asset of our own. The list is collected twice a second, only while the map is on screen.
/// </summary>
public class HelmContactLayer : MonoBehaviour
{
    private const float REFRESH_SECONDS = 0.5f;
    private const float REFERENCE_PIXELS = 100f;

    /// <summary>The world size of one canvas pixel of an icon, as a share of the map's rim: a 26 pixel icon is a twentieth of the radius.</summary>
    private const float PIXEL_FRACTION_OF_RIM = 0.0019f;

    private readonly List<Contact> contacts = [];
    private readonly Dictionary<string, Image> icons = [];
    private readonly HashSet<string> used = [];
    private readonly List<string> gone = [];
    private SubRoot subRoot;
    private Func<TerrainHologram> source;
    private Canvas canvas;
    private float nextRefresh;

    /// <summary>Makes this layer draw the contacts of <paramref name="cyclops" /> on the hologram that <paramref name="hologramSource" /> gives.</summary>
    public void Bind(SubRoot cyclops, Func<TerrainHologram> hologramSource)
    {
        subRoot = cyclops;
        source = hologramSource;
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
        Camera camera = MainCamera.camera;
        if (hologram == null || !hologram.Exists || !hologram.Shown || !subRoot || !camera || !hologram.Aligner || !hologram.Aligner.Anchor)
        {
            HideAll();
            return;
        }
        TerrainHologramAligner aligner = hologram.Aligner;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + REFRESH_SECONDS;
            NavigatorContacts.Collect(subRoot, hologram.Range, false, contacts);
        }
        EnsureCanvas(aligner.Anchor);

        float rim = Mathf.Max(0.0001f, Mathf.Min(hologram.Range * aligner.WorldUnitsPerMetreNow, hologram.DiscRadiusWorld));
        float pixelWorld = rim * PIXEL_FRACTION_OF_RIM;
        canvas.transform.localScale = Vector3.one * (pixelWorld / Mathf.Max(0.0001f, aligner.Anchor.lossyScale.x));
        Vector3 middle = aligner.Anchor.position;
        used.Clear();
        foreach (Contact contact in contacts)
        {
            if (!contact.Alive || contact.Kind == ContactKind.Mark)
            {
                // (the marks are the pins of HelmMarkLayer)
                continue;
            }
            Vector3 point = contact.Kind == ContactKind.Cyclops ? middle : aligner.WorldToMap(contact.Position);
            Vector3 offset = point - middle;
            bool beyond = offset.magnitude > rim;
            if (beyond)
            {
                point = middle + offset.normalized * rim;
            }
            Image icon = GetIcon(contact);
            used.Add(contact.Key);
            icon.rectTransform.position = point;
            icon.rectTransform.rotation = camera.transform.rotation;
            icon.rectTransform.localScale = Vector3.one * (beyond ? 0.6f : 1f);
            Color color = contact.Color;
            color.a = beyond ? 0.55f : 0.95f;
            icon.color = color;
        }
        RemoveUnused();
    }

    private void EnsureCanvas(Transform anchor)
    {
        if (canvas)
        {
            return;
        }
        GameObject canvasObject = new("NitroxHelmContacts", typeof(RectTransform));
        canvasObject.transform.SetParent(anchor, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(REFERENCE_PIXELS, REFERENCE_PIXELS);
    }

    private Image GetIcon(Contact contact)
    {
        if (icons.TryGetValue(contact.Key, out Image existing) && existing)
        {
            return existing;
        }
        GameObject iconObject = new($"Contact {contact.Key}", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(canvas.transform, false);
        ((RectTransform)iconObject.transform).sizeDelta = NavigatorIcons.SizeOf(contact.Kind, contact.Danger);
        Image image = iconObject.GetComponent<Image>();
        image.sprite = NavigatorIcons.Get(NavigatorIcons.ShapeOf(contact.Kind, contact.Danger));
        image.raycastTarget = false;
        icons[contact.Key] = image;
        return image;
    }

    private void RemoveUnused()
    {
        gone.Clear();
        foreach (KeyValuePair<string, Image> pair in icons)
        {
            if (!used.Contains(pair.Key))
            {
                gone.Add(pair.Key);
            }
        }
        foreach (string key in gone)
        {
            if (icons[key])
            {
                Destroy(icons[key].gameObject);
            }
            icons.Remove(key);
        }
    }

    private void HideAll()
    {
        if (icons.Count == 0)
        {
            return;
        }
        used.Clear();
        RemoveUnused();
    }
}
