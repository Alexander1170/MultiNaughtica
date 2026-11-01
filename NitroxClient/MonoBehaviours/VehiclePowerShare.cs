using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// On every Seamoth and Prawn Suit: once a second asks a Cyclops with the power share module in range for energy, while this client simulates the vehicle and it is not full
/// (<see cref="PowerShare.TickVehicle" />).
/// </summary>
public class VehiclePowerShare : MonoBehaviour
{
    private Vehicle vehicle;
    private float elapsed;
    private float retryAt;

    private void Awake()
    {
        vehicle = GetComponent<Vehicle>();
        PowerShare.Subscribe();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed < PowerTransferRules.REQUEST_INTERVAL_SECONDS || Time.time < retryAt)
        {
            return;
        }
        // While the last request is still on its way the time keeps adding up (the request asks for the most of one and a half seconds), so a slow answer does not slow the share down
        if (PowerShare.TickVehicle(vehicle, elapsed))
        {
            elapsed = 0f;
        }
        else
        {
            retryAt = Time.time + 0.25f;
        }
    }
}
