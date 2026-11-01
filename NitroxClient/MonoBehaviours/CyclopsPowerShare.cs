using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// On every Cyclops: asks for emergency power once a second while the Cyclops is low (<see cref="PowerShare.TickCyclops" />), is the list of Cyclops that a vehicle looks at for the forward share, and
/// measures the distance from a point to the hull (<see cref="HullBounds" />). It ticks here and not in the game's <c>SubRoot.Update</c>, which returns at once while the Cyclops is in the minimal level of detail, as it often is on
/// the client that simulates it (see <see cref="CyclopsSolarCharger" />).
/// </summary>
public class CyclopsPowerShare : MonoBehaviour
{
    private float elapsed;
    private float retryAt;
    private VehicleDockingBay dockingBay;

    public SubRoot SubRoot { get; private set; }

    private void Awake()
    {
        SubRoot = GetComponent<SubRoot>();
    }

    private void OnEnable()
    {
        PowerShare.Register(this);
    }

    private void OnDisable()
    {
        PowerShare.Unregister(this);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed < PowerTransferRules.REQUEST_INTERVAL_SECONDS || Time.time < retryAt)
        {
            return;
        }
        if (PowerShare.TickCyclops(this, elapsed))
        {
            elapsed = 0f;
        }
        else
        {
            retryAt = Time.time + 0.25f;
        }
    }

    /// <summary>The vehicle docked in this Cyclops' docking bay, if any.</summary>
    public Vehicle DockedVehicle()
    {
        if (!dockingBay && SubRoot)
        {
            dockingBay = SubRoot.GetComponentInChildren<VehicleDockingBay>(true);
        }
        return dockingBay ? dockingBay.GetDockedVehicle() : null;
    }

    /// <summary>The distance from a world position to the hull of this Cyclops: to the box around its colliders, or to its middle less the radius of a hull when the colliders are not there.</summary>
    public float DistanceToHull(Vector3 worldPoint)
    {
        if (HullBounds.TryDistanceTo(transform, worldPoint, out float distance))
        {
            return distance;
        }
        return PowerShareGeometry.DistanceToHullFromCenter(Vector3.Distance(worldPoint, SubRoot.GetWorldCenterOfMass()));
    }
}
