using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>Runs the buddy oxygen of the giver every frame (<see cref="BuddyOxygenShare.Tick" />) while a game is joined; added to the multiplayer object next to the ping.</summary>
internal sealed class BuddyOxygenDriver : MonoBehaviour
{
    private void Awake()
    {
        BuddyOxygenShare.Initialize(this.Resolve<PlayerManager>(), this.Resolve<IPacketSender>());
    }

    private void Update()
    {
        BuddyOxygenShare.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        BuddyOxygenShare.Reset();
    }
}
