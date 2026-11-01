using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Gui.InGame;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// The quick ping (docs/coop/QUICK_PING.md): the player looks at something and presses the ping key (the middle mouse button unless the options say otherwise), and every teammate sees a marker there. Stock
/// Nitrox pings like this; this build adds the rules that a shared server needs: the server stamps the sender and keeps a rate limit (a client keeps one ping per
/// <see cref="QuickPingRules.CLIENT_INTERVAL_SECONDS" /> seconds), a player has one ping at a time (a new one replaces the old one, here and on the other clients), a ping lives for the server's
/// <c>QuickPingSeconds</c>, and its text says what the player looked at (<see cref="QuickPingAim" />). A key press with nothing in range makes no ping and plays the error sound.
/// </summary>
internal sealed class PlayerPingManager : MonoBehaviour
{
    public const string PING_OK_SOUND = "event:/sub/cyclops/sonar";
    public const string PING_FAIL_SOUND = "event:/tools/divereel/breadcrum";

    private static PlayerPingManager instance = null!;

    private IPacketSender packetSender = null!;
    private LocalPlayer localPlayer = null!;
    private float lastPingTime = float.NegativeInfinity;
    private readonly Dictionary<SessionId, PlayerPing> pings = [];

    /// <summary>How a request for a ping ended (the harness logs it).</summary>
    internal enum Outcome
    {
        Sent,
        NotReady,
        TooSoon,
        NothingInRange
    }

    public void Awake()
    {
        if (instance)
        {
            Log.Error($"Tried to initialize a second instance of singleton {nameof(PlayerPingManager)}:{Environment.NewLine}{Environment.StackTrace}");
            Destroy(this);
            return;
        }
        instance = this;
        packetSender = this.Resolve<IPacketSender>();
        localPlayer = this.Resolve<LocalPlayer>();
    }

    /// <summary>The key was pressed.</summary>
    public static void RequestPing()
    {
        if (!instance)
        {
            return;
        }
        instance.RequestInternal(null, out _);
    }

    /// <summary>
    /// The same as the key, for the harness: with a ray of its own instead of the camera's (a live check cannot hold the view still at a creature), and with the answer. The cooldown and the checks of
    /// the key apply.
    /// </summary>
    internal static Outcome Request(Ray? ray, out QuickPingAim.Target target)
    {
        target = default;
        if (!instance)
        {
            return Outcome.NotReady;
        }
        return instance.RequestInternal(ray, out target);
    }

    private Outcome RequestInternal(Ray? ray, out QuickPingAim.Target target)
    {
        target = default;
        if (!Player.main || !AvatarInputHandler.main || !AvatarInputHandler.main.IsEnabled() || !localPlayer.SessionId.HasValue)
        {
            return Outcome.NotReady;
        }
        if (!QuickPingRules.ClientMayPing(Time.realtimeSinceStartup - lastPingTime))
        {
            return Outcome.TooSoon;
        }
        Ray aim;
        if (ray.HasValue)
        {
            aim = ray.Value;
        }
        else if (!QuickPingAim.TryGetCameraRay(out aim))
        {
            return Outcome.NotReady;
        }
        if (!QuickPingAim.TryAim(aim, out target))
        {
            // Nothing in range: no ping. The error sound tells the player so
            FMODEmitterController.PlayEventOneShot(PING_FAIL_SOUND, 3, Player.main.transform.position);
            return Outcome.NothingInRange;
        }

        lastPingTime = Time.realtimeSinceStartup;
        SessionId session = localPlayer.SessionId.Value;
        NitroxId pingId = new();
        NitroxVector3 position = target.MarkerPosition.ToDto();
        Place(session, localPlayer.PlayerName, target.Label, position, pingId, localPlayer.PlayerSettings.PlayerColor.ToUnity());
        packetSender.Send(new PlayerPingCreated(session, target.Label, position, pingId));
        return Outcome.Sent;
    }

    /// <summary>A ping of another player arrived (the server's stamped copy).</summary>
    public static void CreateRemotePing(PlayerPingCreated packet)
    {
        if (!instance)
        {
            return;
        }
        instance.CreateRemotePingInternal(packet);
    }

    private void CreateRemotePingInternal(PlayerPingCreated packet)
    {
        PlayerManager playerManager = this.Resolve<PlayerManager>();
        Optional<RemotePlayer> remotePlayer = playerManager.Find(packet.SessionId);
        Color playerColor = remotePlayer.HasValue ? remotePlayer.Value.PlayerSettings.PlayerColor.ToUnity() : Color.yellow;
        string name = !string.IsNullOrEmpty(packet.SenderName) ? packet.SenderName : remotePlayer.HasValue ? remotePlayer.Value.PlayerName : string.Empty;
        Place(packet.SessionId, name, packet.Text, packet.Position, packet.PingId, playerColor);
    }

    /// <summary>Makes the ping of a player and removes the one they had.</summary>
    private void Place(SessionId owner, string ownerName, string target, NitroxVector3 position, NitroxId pingId, Color color)
    {
        if (pings.TryGetValue(owner, out PlayerPing old) && old)
        {
            Destroy(old.gameObject);
        }
        pings.Remove(owner);

        string label = QuickPingRules.MarkerText(ownerName, target);
        PlayerPing ping = PlayerPing.SpawnPlayerPing(owner, ownerName, QuickPingRules.TargetLabel(target, null), label, position, pingId, color, QuickPingRules.Lifetime(VehicleUpgradeSettings.QuickPingSeconds));
        if (ping)
        {
            pings[owner] = ping;
            Log.Info($"[QuickPing] {(string.IsNullOrEmpty(ownerName) ? owner.ToString() : ownerName)} pinged '{ping.Target}' at {position} for {ping.Lifetime:0.#} s");
        }
    }

    /// <summary>The pings that are on the HUD of this client right now, one per player at most (for the harness).</summary>
    internal static List<PlayerPing> ActivePings()
    {
        List<PlayerPing> active = [];
        if (instance)
        {
            foreach (PlayerPing ping in instance.pings.Values)
            {
                if (ping)
                {
                    active.Add(ping);
                }
            }
        }
        return active;
    }

    /// <summary>The cooldown of the key is over (for the harness, which presses more often than a player).</summary>
    internal static void ResetCooldown()
    {
        if (instance)
        {
            instance.lastPingTime = float.NegativeInfinity;
        }
    }
}
