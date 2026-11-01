using NitroxClient.GameLogic;
using Nitrox.Model.GameLogic.PlayerAnimation;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public class UnderwaterStateTracker : MonoBehaviour
{
    private LocalPlayer localPlayer;
    private AnimChangeState lastUnderwaterState = AnimChangeState.UNSET;
    private float lastSendTime = float.NegativeInfinity;

    public void Awake()
    {
        localPlayer = this.Resolve<LocalPlayer>();
        enabled = false;

        Multiplayer.OnLoadingComplete += Enable;
    }

    public void OnDestroy()
    {
        Multiplayer.OnLoadingComplete -= Enable;
    }

    private void Enable()
    {
        enabled = true;
    }

    public void Update()
    {
        AnimChangeState underwaterState = (AnimChangeState)(Player.main.IsUnderwaterForSwimming() ? 1 : 0);
        float now = Time.unscaledTime;

        // Sent when it changes and, unchanged, every few seconds (PoseRefreshRules): a state that never arrived, or came before the body of this player existed on another client, is healed by the
        // next one. Without it the copy kept the swimming pose in a lifepod, a base and a docked Cyclops until the player next swam (bug 6, PLAYERS.md PL9 (6))
        if (PoseRefreshRules.IsDue(lastUnderwaterState != underwaterState, now - lastSendTime))
        {
            localPlayer.AnimationChange(AnimChangeType.UNDERWATER, underwaterState);
            lastUnderwaterState = underwaterState;
            lastSendTime = now;
        }
    }
}
