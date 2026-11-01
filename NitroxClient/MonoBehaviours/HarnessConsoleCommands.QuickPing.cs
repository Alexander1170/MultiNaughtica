#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Gui.InGame;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug command of the quick ping (docs/coop/QUICK_PING.md). Logs <c>[SyncHarness] vuping: key=value</c> lines.
///     <c>vuping press [aim|creature|ground|base|cyclops|vehicle|player|none]</c> does what the ping key does, with a ray of its own instead of the camera's (a live check cannot hold the view at a creature): <c>aim</c> is
///     the camera's own ray, <c>creature</c>, <c>base</c>, <c>cyclops</c> and <c>vehicle</c> aim at the nearest one that the ray reaches first, <c>ground</c> straight down, <c>none</c> at the empty sky. The
///     cooldown of the key is reset before each press (a live check presses more often than a player); <c>vuping press</c> logs the outcome, the label and the distance.
///     <c>vuping list</c> lists the pings on this client's HUD (owner, label, distance, time left). <c>vuping flood [n]</c> sends n pings at once without the client's cooldown (the server's rate limit),
///     <c>vuping spoof [name]</c> sends a ping that claims to be another player's, with markup in the text (the server's stamp and the cleaning).
/// </summary>
public partial class HarnessConsoleCommands
{
    private void LiveCommand_vuping(NotificationCenter.Notification n)
    {
        switch (LiveArgString(n, 0, "list"))
        {
            case "press":
                QuickPingPress(LiveArgString(n, 1, "aim"));
                break;
            case "flood":
                QuickPingFlood(Mathf.Clamp(Mathf.RoundToInt(LiveArgFloat(n, 1, 5f)), 1, 20));
                break;
            case "spoof":
                QuickPingSpoof(LiveArgString(n, 1, ""));
                break;
            default:
                QuickPingList();
                break;
        }
    }

    private void QuickPingPress(string kind)
    {
        Camera camera = Camera.main;
        if (!camera || !Player.main)
        {
            Log.Info($"{VU}vuping: press kind={kind} outcome=NoCamera");
            return;
        }

        Ray? ray = null;
        switch (kind)
        {
            case "creature":
                ray = QuickPingRayAtNearest<Creature>(camera, _ => true);
                break;
            case "player":
                ray = QuickPingRayAtNearest<RemotePlayerIdentifier>(camera, _ => true, true);
                break;
            case "base":
                ray = QuickPingRayAtNearest<Base>(camera, _ => true);
                break;
            case "cyclops":
                ray = QuickPingRayAtNearest<SubRoot>(camera, subRoot => subRoot.isCyclops);
                break;
            case "vehicle":
                ray = QuickPingRayAtNearest<Vehicle>(camera, vehicle => Player.main.GetVehicle() != vehicle);
                break;
            case "ground":
                ray = new Ray(camera.transform.position, Vector3.down);
                break;
            case "none":
                ray = new Ray(camera.transform.position + Vector3.up * 2000f, Vector3.up);
                break;
        }
        if (kind != "aim" && kind != "ground" && kind != "none" && !ray.HasValue)
        {
            Log.Info($"{VU}vuping: press kind={kind} outcome=NoTarget");
            return;
        }

        PlayerPingManager.ResetCooldown();
        PlayerPingManager.Outcome outcome = PlayerPingManager.Request(ray, out QuickPingAim.Target target);
        if (outcome == PlayerPingManager.Outcome.Sent)
        {
            Log.Info($"{VU}vuping: press kind={kind} outcome={outcome} label=\"{target.Label}\" targetKind={target.Kind} distance={target.Distance:F1} hit={(target.HitObject ? target.HitObject.name : "none")} point={target.Point.ToString("F1")} inVehicle={QuickPingVehicleName()}");
        }
        else
        {
            Log.Info($"{VU}vuping: press kind={kind} outcome={outcome} inVehicle={QuickPingVehicleName()}");
        }
    }

    /// <summary>
    ///     A ray from the camera at the nearest <typeparamref name="T" /> that the ray reaches first (no terrain in between): null when there is none within 150 m.
    /// </summary>
    private static Ray? QuickPingRayAtNearest<T>(Camera camera, Func<T, bool> accept, bool includeTriggers = false) where T : Component
    {
        Vector3 from = camera.transform.position;
        List<Vector3> points = [];
        foreach (T item in FindObjectsOfType<T>())
        {
            if (!item || !accept(item))
            {
                continue;
            }
            foreach (Collider collider in item.GetComponentsInChildren<Collider>())
            {
                if (collider && (includeTriggers || !collider.isTrigger))
                {
                    points.Add(collider.bounds.center);
                }
            }
        }
        foreach (Vector3 point in points.OrderBy(p => (p - from).sqrMagnitude).Take(60))
        {
            float distance = (point - from).magnitude;
            if (distance > 150f || distance < 1f)
            {
                continue;
            }
            Ray ray = new(from, point - from);
            if (QuickPingAim.TryAim(ray, out QuickPingAim.Target hit) && hit.HitObject && hit.HitObject.GetComponentInParent<T>())
            {
                return ray;
            }
        }
        return null;
    }

    /// <summary>The vehicle the local player sits in ("none" when on foot or swimming): the aim skips it, and the probe wants to know that Bob was in it.</summary>
    private static string QuickPingVehicleName()
    {
        Vehicle vehicle = Player.main ? Player.main.GetVehicle() : null;
        return vehicle ? vehicle.name.Replace(" ", "") : "none";
    }

    private static void QuickPingList()
    {
        Camera camera = Camera.main;
        List<PlayerPing> pings = PlayerPingManager.ActivePings();
        foreach (PlayerPing ping in pings)
        {
            Vector3 position = ping.transform.position;
            float distance = camera ? Vector3.Distance(camera.transform.position, position) : -1f;
            Log.Info($"{VU}vuping: ping owner={ping.OwnerName} session={ping.Owner} label=\"{ping.Label}\" target=\"{ping.Target}\" pos={position.ToString("F1")} distance={distance:F1} remaining={ping.Remaining:F1} lifetime={ping.Lifetime:F1} color={ColorUtility.ToHtmlStringRGB(ping.Color)} visible={(ping.Signal && ping.Signal.visible)}");
        }
        Log.Info($"{VU}vuping: pings={pings.Count} quickPingSeconds={VehicleUpgradeSettings.QuickPingSeconds:F1}");
    }

    private static void QuickPingFlood(int count)
    {
        LocalPlayer localPlayer = NitroxServiceLocator.LocateService<LocalPlayer>();
        if (!localPlayer.SessionId.HasValue || !Player.main)
        {
            Log.Info($"{VU}vuping: flood outcome=NotReady");
            return;
        }
        IPacketSender sender = NitroxServiceLocator.LocateService<IPacketSender>();
        Vector3 origin = Player.main.transform.position + Player.main.transform.forward * 5f;
        for (int i = 0; i < count; i++)
        {
            sender.Send(new PlayerPingCreated(localPlayer.SessionId.Value, $"Flood {i}", (origin + Vector3.up * i).ToDto(), new NitroxId()));
        }
        Log.Info($"{VU}vuping: flood sent={count}");
    }

    private static void QuickPingSpoof(string name)
    {
        LocalPlayer localPlayer = NitroxServiceLocator.LocateService<LocalPlayer>();
        PlayerManager playerManager = NitroxServiceLocator.LocateService<PlayerManager>();
        RemotePlayer victim = playerManager.GetAll().FirstOrDefault(player => string.IsNullOrEmpty(name) || string.Equals(player.PlayerName, name, StringComparison.OrdinalIgnoreCase));
        if (victim == null || !Player.main)
        {
            Log.Info($"{VU}vuping: spoof outcome=NoPlayer name={name}");
            return;
        }
        // The packet claims to come from the other player, with a name of theirs and markup in the text: the server has to put this client's own session and name in and clean the text
        IPacketSender sender = NitroxServiceLocator.LocateService<IPacketSender>();
        Vector3 position = Player.main.transform.position + Player.main.transform.forward * 5f;
        sender.Send(new PlayerPingCreated(victim.SessionId, "<size=99><color=red>Spoof</color></size>", position.ToDto(), new NitroxId(), victim.PlayerName));
        Log.Info($"{VU}vuping: spoof sent as={victim.PlayerName} session={victim.SessionId} me={localPlayer.PlayerName}");
    }
}
#endif
