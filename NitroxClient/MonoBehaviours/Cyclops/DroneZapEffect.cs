using System.Collections.Generic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// What a zap of the Scout Drone looks like (docs/coop/VEHICLE_UPGRADES.md, station 4): a flash of blue-white light and a few jagged arcs of lightning from the drone out to the radius of the zap, for about half a
/// second. Built from a light and line renderers at run time (no prefab of the game is needed, so it cannot fail to load); it only shows, the effect on the creatures is <c>DroneZap</c>'s. Every client plays it.
/// </summary>
public class DroneZapEffect : MonoBehaviour
{
    private const float LIFETIME = 0.55f;
    private const int ARC_COUNT = 9;
    private const int POINTS_PER_ARC = 9;

    private readonly List<LineRenderer> arcs = [];
    private readonly List<Vector3> arcEnds = [];
    private Light flash;
    private Material material;
    private float radius;
    private float born;

    /// <summary>Plays the effect at <paramref name="centre" />, reaching out to <paramref name="radius" />.</summary>
    public static void Play(Vector3 centre, float radius)
    {
        GameObject effect = new("NitroxDroneZapEffect");
        effect.transform.position = centre;
        effect.AddComponent<DroneZapEffect>().Begin(radius);
    }

    private void Begin(float zapRadius)
    {
        radius = zapRadius;
        born = Time.time;
        try
        {
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(0.55f, 0.8f, 1f);
            flash.range = Mathf.Clamp(radius, 8f, 60f);
            flash.intensity = 6f;
            flash.shadows = LightShadows.None;

            Shader shader = Shader.Find("Sprites/Default");
            material = shader ? new Material(shader) : null;
            for (int i = 0; i < ARC_COUNT; i++)
            {
                GameObject arcObject = new($"Arc{i}");
                arcObject.transform.SetParent(transform, false);
                LineRenderer line = arcObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = POINTS_PER_ARC;
                if (material)
                {
                    line.material = material;
                }
                line.startWidth = 0.18f;
                line.endWidth = 0.05f;
                arcs.Add(line);
                arcEnds.Add(transform.position + Random.onUnitSphere * Random.Range(0.6f, 1f) * radius);
            }
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] drone zap: could not build the effect");
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        float age = Time.time - born;
        if (age >= LIFETIME)
        {
            Destroy(gameObject);
            return;
        }
        float fade = 1f - age / LIFETIME;
        if (flash)
        {
            flash.intensity = 6f * fade * fade;
        }
        Vector3 centre = transform.position;
        for (int i = 0; i < arcs.Count; i++)
        {
            LineRenderer line = arcs[i];
            Vector3 end = arcEnds[i];
            for (int p = 0; p < POINTS_PER_ARC; p++)
            {
                float t = p / (float)(POINTS_PER_ARC - 1);
                Vector3 point = Vector3.Lerp(centre, end, t);
                // the arcs jump about while they last; the ends of an arc stay put
                point += Random.insideUnitSphere * (radius * 0.04f * Mathf.Sin(t * Mathf.PI));
                line.SetPosition(p, point);
            }
            Color color = new(0.7f, 0.9f, 1f, fade);
            line.startColor = color;
            line.endColor = new Color(0.4f, 0.6f, 1f, fade * 0.4f);
            line.widthMultiplier = 0.4f + 0.6f * fade;
        }
    }

    private void OnDestroy()
    {
        if (material)
        {
            Destroy(material);
        }
    }
}
