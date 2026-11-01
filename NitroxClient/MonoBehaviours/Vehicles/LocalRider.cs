using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// On the local player while they hold on to a bar (<see cref="RiderBars" />): lets go on the use key or the exit key, shows the exit prompt like a pilot's, sends the look to the others, and watches
/// over the ride. The player is a child of the Seamoth while it lasts: if the bar or the Seamoth disappears, or something took the player out of the locked mode (death, a console command), the
/// ride ends here, before the Seamoth's destruction can take the player with it.
/// </summary>
public class LocalRider : MonoBehaviour
{
    private void Update()
    {
        if (!RiderBars.IsLocalRiding)
        {
            Destroy(this);
            return;
        }

        SeamothRiderBar bar = RiderBars.LocalBar;
        Player player = Player.main;
        if (!player || !bar || !bar.IsBuilt || !bar.Vehicle)
        {
            RiderBars.ReleaseLocal(RiderReleaseReason.Destroyed, true);
            return;
        }
        if (player.GetMode() != Player.Mode.LockedPiloting)
        {
            // Something else took the player out of the locked mode: nothing to undo there, the ride is simply over
            RiderBars.ReleaseLocal(RiderReleaseReason.ModeChanged, true);
            return;
        }
        if (bar.Vehicle.docked)
        {
            RiderBars.ReleaseLocal(RiderReleaseReason.Docked, true);
            return;
        }

        // The prompt of a pilot: "Press X to exit" (the game's own text), nothing below it
        HandReticle.main.SetTextRaw(HandReticle.TextType.Use, LanguageCache.GetButtonFormat("PressToExit", GameInput.Button.Exit));
        HandReticle.main.SetTextRaw(HandReticle.TextType.UseSubscript, string.Empty);

        bool inputEnabled = AvatarInputHandler.main && AvatarInputHandler.main.IsEnabled();
        if (RiderBarRules.ShouldLetGo(Time.time - RiderBars.GrabbedAt, GameInput.GetButtonDown(GameInput.Button.LeftHand), GameInput.GetButtonDown(GameInput.Button.Exit), player.GetPDA().isInUse, inputEnabled))
        {
            RiderBars.ReleaseLocal(RiderReleaseReason.Hand, true);
            return;
        }

        RiderBars.SendLookIfNeeded();
    }
}
