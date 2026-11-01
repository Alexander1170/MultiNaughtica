using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// The cable of a tow, on every client (<see cref="VehicleTows" />): a line between the rear of the tower and the nose of the towed vehicle, drawn from the two vehicles as the client has them (the
/// tower's client has the real ones, the others the copies that the movement replicator draws), so it follows them without any data of its own. It hangs a little while it is slack. It finds the
/// two vehicles itself, because a client that joins hears of a tow before it has spawned the vehicles.
/// </summary>
public class TowCable : MonoBehaviour
{
    private const int POINTS = 9;
    private const float MAX_SAG = 1.5f;
    private const float WIDTH = 0.07f;

    /// <summary>Seconds a cable waits for its vehicles to exist before the tow is dropped from this client (they may still be spawning).</summary>
    private const float GIVE_UP_SECONDS = 60f;

    private VehicleTows.Tow tow;
    private LineRenderer line;
    private Transform towerRoot;
    private Component tower;
    private Transform towedRoot;
    private float nextResolve;
    private float createdAt;

    /// <summary>True while the cable is drawn (both vehicles are known and a material was found).</summary>
    public bool Visible => line && line.enabled;

    /// <summary>The material and shader of the cable, for the live check.</summary>
    public string MaterialName => line && line.sharedMaterial ? $"{line.sharedMaterial.name}/{line.sharedMaterial.shader.name}" : "none";

    public static TowCable Create(VehicleTows.Tow tow)
    {
        GameObject cable = new($"TowCable {tow.TowerId}");
        TowCable component = cable.AddComponent<TowCable>();
        component.tow = tow;
        component.createdAt = Time.time;
        component.line = cable.AddComponent<LineRenderer>();
        component.ConfigureLine();
        return component;
    }

    private void ConfigureLine()
    {
        line.useWorldSpace = true;
        line.positionCount = POINTS;
        line.widthMultiplier = WIDTH;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startColor = new Color(0.82f, 0.78f, 0.62f, 1f);
        line.endColor = new Color(0.82f, 0.78f, 0.62f, 1f);
        Material material = TowCableMaterial.Get();
        if (material)
        {
            line.sharedMaterial = material;
        }
        line.enabled = false;
    }

    private void LateUpdate()
    {
        if (tow == null)
        {
            return;
        }
        if (!towerRoot || !towedRoot)
        {
            if (Time.time >= nextResolve)
            {
                nextResolve = Time.time + 0.5f;
                Resolve();
            }
            if (!towerRoot || !towedRoot)
            {
                line.enabled = false;
                if (Time.time - createdAt > GIVE_UP_SECONDS)
                {
                    Log.Warn($"[VehicleUpgrades] tow: the vehicles of a tow never appeared, dropping it ({tow.TowerId} -> {tow.TowedId})");
                    VehicleTows.Release(tow, TowReleaseReason.Destroyed);
                }
                return;
            }
        }

        if (!line.sharedMaterial)
        {
            // The first try may have come before the game had a material to lend (it limits its own attempts)
            Material material = TowCableMaterial.Get();
            if (material)
            {
                line.sharedMaterial = material;
            }
        }

        Vector3 towerForward = tower is SubRoot cyclops && cyclops.subAxis ? cyclops.subAxis.forward : towerRoot.forward;
        if (!HullBounds.TryGetAnchor(towerRoot, towerForward, false, out Vector3 start))
        {
            start = towerRoot.position;
        }
        if (!HullBounds.TryGetAnchor(towedRoot, towedRoot.forward, true, out Vector3 end))
        {
            end = towedRoot.position;
        }

        float length = Vector3.Distance(start, end);
        float rest = TowRules.RestLength(VehicleUpgradeSettings.TowMaxDistance);
        for (int i = 0; i < POINTS; i++)
        {
            float t = i / (float)(POINTS - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y -= HullGeometry.Sag(length, rest, t, MAX_SAG);
            line.SetPosition(i, point);
        }
        line.enabled = line.sharedMaterial;
    }

    private void Resolve()
    {
        if (!towerRoot && NitroxEntity.TryGetObjectFrom(tow.TowerId, out GameObject towerObject))
        {
            towerRoot = towerObject.transform;
            tower = towerObject.TryGetComponent(out SubRoot subRoot) ? subRoot : towerObject.GetComponent<Vehicle>();
        }
        if (!towedRoot && NitroxEntity.TryGetObjectFrom(tow.TowedId, out GameObject towedObject))
        {
            towedRoot = towedObject.transform;
        }
    }
}
