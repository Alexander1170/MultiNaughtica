using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Gui.HUD;

public class DenyOwnershipHand : MonoBehaviour
{
    private bool onlyWhileTargeted;
    private float until;

    /// <summary>
    /// Puts the refusal on the player's hand for a few seconds (a second refusal in that time only restarts the clock). With <paramref name="onlyWhileTargeted" /> the text shows only while the hand points at
    /// <paramref name="target" /> itself (see <see cref="DenyOwnershipRules" />): the Rider Bar on a refused Seamoth keeps its own text when the player looks at it, instead of the refusal of the hull.
    /// </summary>
    public static DenyOwnershipHand Show(GameObject target, bool onlyWhileTargeted)
    {
        DenyOwnershipHand deny = target.GetComponent<DenyOwnershipHand>();
        if (!deny)
        {
            deny = target.AddComponent<DenyOwnershipHand>();
        }
        deny.onlyWhileTargeted = onlyWhileTargeted;
        deny.until = Time.time + DenyOwnershipRules.SHOW_SECONDS;
        return deny;
    }

    private void Awake()
    {
        // (also the start of a component that was added without Show)
        until = Time.time + DenyOwnershipRules.SHOW_SECONDS;
    }

    private void Update()
    {
        // Forces the message to go away after a few seconds.
        if (DenyOwnershipRules.IsOver(Time.time, until))
        {
            Destroy(this);
            return;
        }

        if (onlyWhileTargeted)
        {
            GUIHand hand = Player.main ? Player.main.guiHand : null;
            if (!DenyOwnershipRules.ShowsText(true, hand ? hand.GetActiveTarget() : null, gameObject))
            {
                return;
            }
        }

        //TODO: Check if this should be Hand
        HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_DenyOwnershipHand", true);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, string.Empty, false);
        HandReticle.main.SetIcon(HandReticle.IconType.HandDeny);
    }
}
