using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// The trigger on the bar that the player's hand finds (<see cref="SeamothRiderBar" />): "Hold on" when the player can, "Occupied" when somebody else holds on, nothing otherwise. The game's hand walks
/// up from the collider it looks at to the first object that has a hand target, which is this one, before it reaches the Seamoth's own.
/// </summary>
public class RiderBarHandTarget : MonoBehaviour, IHandTarget
{
    public SeamothRiderBar Bar { get; set; }

    public void OnHandHover(GUIHand hand)
    {
        switch (RiderBars.DecisionFor(Bar))
        {
            case RiderGrabDecision.Yes:
                HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_RiderBarHold", true, GameInput.Button.LeftHand);
                HandReticle.main.SetText(HandReticle.TextType.HandSubscript, string.Empty, false, GameInput.Button.None);
                HandReticle.main.SetIcon(HandReticle.IconType.Hand, 1f);
                break;
            case RiderGrabDecision.Occupied:
                HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_RiderBarOccupied", true, GameInput.Button.None);
                HandReticle.main.SetIcon(HandReticle.IconType.HandDeny, 1f);
                break;
        }
    }

    public void OnHandClick(GUIHand hand)
    {
        RiderBars.RequestHold(Bar);
    }
}
