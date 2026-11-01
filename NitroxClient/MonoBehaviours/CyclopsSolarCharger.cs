using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// Drives the Cyclops Solar Charger (<see cref="VehicleSolar.OnCyclopsTick" />). The game's own charge of the thermal reactor module runs from <c>SubRoot.Update</c>, which returns at once while the
/// Cyclops is in the minimal level of detail (far from the camera, or not looking at it); the client that simulates the Cyclops is often such a client, and nothing would charge. This component is
/// on the Cyclops itself, which is a global root entity and stays loaded on every client, and ticks every frame; the lock check and the settings are in <see cref="VehicleSolar" />.
/// </summary>
public class CyclopsSolarCharger : MonoBehaviour
{
    private SubRoot subRoot;

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
    }

    private void Update()
    {
        if (!subRoot)
        {
            return;
        }
        PatchProbe.Fired("CyclopsSolarCharger.Update");
        VehicleSolar.OnCyclopsTick(subRoot);
    }
}
