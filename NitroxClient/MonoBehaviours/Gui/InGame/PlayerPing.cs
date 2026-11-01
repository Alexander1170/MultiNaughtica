using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Gui.InGame;

/// <summary>
/// One quick ping on the HUD (docs/coop/QUICK_PING.md): the game's own signal marker, in the colour of the player who made it, with their name and what they looked at, and the distance that the game adds.
/// It removes itself after its lifetime, the server's <c>QuickPingSeconds</c>. <see cref="PlayerPingManager" /> keeps one per player.
/// </summary>
public class PlayerPing : MonoBehaviour
{
    private float spawnTime;

    /// <summary>The player who pinged.</summary>
    public SessionId Owner { get; private set; }

    /// <summary>The name of the player who pinged, as the server stamped it.</summary>
    public string OwnerName { get; private set; }

    /// <summary>What the player looked at, without their name.</summary>
    public string Target { get; private set; }

    /// <summary>The line on the HUD ("Bob: Reaper Leviathan").</summary>
    public string Label { get; private set; }

    public Color Color { get; private set; }

    /// <summary>Seconds that the ping lives.</summary>
    public float Lifetime { get; private set; }

    public PingInstance Signal { get; private set; }

    /// <summary>Seconds that are left of the lifetime.</summary>
    public float Remaining => Lifetime - (Time.time - spawnTime);

    private void Start()
    {
        spawnTime = Time.time;
    }

    private void Update()
    {
        if (Remaining <= 0f)
        {
            Destroy(gameObject);
        }
    }

    public static PlayerPing SpawnPlayerPing(SessionId owner, string ownerName, string target, string label, NitroxVector3 location, NitroxId pingId, Color playerColor, float lifetime)
    {
        GameObject pingObject = new($"Nitrox_PlayerPing_{owner}_{pingId}");
        pingObject.transform.position = location.ToUnity();

        PlayerPing playerPing = pingObject.AddComponent<PlayerPing>();
        playerPing.spawnTime = Time.time;
        playerPing.Owner = owner;
        playerPing.OwnerName = ownerName;
        playerPing.Target = target;
        playerPing.Label = label;
        playerPing.Color = playerColor;
        playerPing.Lifetime = lifetime;

        PingInstance signal = pingObject.AddComponent<PingInstance>();
        signal.pingType = PingType.Signal;
        signal.origin = pingObject.transform;
        signal.minDist = 5f;
        signal.range = 5f;
        signal._label = label;
        signal.displayPingInManager = false;
        signal.visible = true;
        signal._id = $"nitrox_playerping_{pingId}";
        signal.Initialize();
        playerPing.Signal = signal;

        uGUI_Pings pings = FindObjectOfType<uGUI_Pings>();
        if (pings)
        {
            pings.OnColor(signal.Id, playerColor);
        }

        FMODEmitterController.PlayEventOneShot(PlayerPingManager.PING_OK_SOUND, 3, Player.main.transform.position);

        return playerPing;
    }
}
