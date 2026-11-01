#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using Nitrox.Model.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug command of the buddy oxygen (docs/coop/BUDDY_OXYGEN.md). Logs <c>[SyncHarness] vuoxy: key=value</c> lines.
///     <c>vuoxy state</c> lists the air of this player and what the sharing moved on this client (steps given and the air taken out for them, what the receivers took, what came back, steps received and the air added).
///     <c>vuoxy set seconds</c> puts this player's air at that many seconds (a live check drains and refills around the windows, so that nobody drowns). <c>vuoxy hold seconds</c> sets how long <c>give</c> holds the key
///     (6 s unless told); <c>vuoxy give name</c> holds the share key for that long, aimed at that teammate, with the client's range and view checks; <c>vuoxy force name</c> the same without them (the server's checks
///     are tested with it); <c>vuoxy stop</c> lets go of the key; <c>vuoxy reset</c> zeroes the counters; <c>vuoxy move x,y,z</c> moves this player; <c>vuoxy kill</c> kills this player; <c>vuoxy killif name</c> kills it only when it is that player (for <c>/consoleall</c>). <c>set</c> also zeroes the counters.
/// </summary>
public partial class HarnessConsoleCommands
{
    private static float buddyOxygenHoldSeconds = 6f;

    private void LiveCommand_vuoxy(NotificationCenter.Notification n)
    {
        switch (LiveArgString(n, 0, "state"))
        {
            case "set":
                BuddyOxygenSet(LiveArgFloat(n, 1, 45f));
                BuddyOxygenShare.ResetStats();
                break;
            case "hold":
                buddyOxygenHoldSeconds = Mathf.Clamp(LiveArgFloat(n, 1, 6f), 0.5f, 60f);
                Log.Info($"{VU}vuoxy: hold seconds={buddyOxygenHoldSeconds:F1}");
                break;
            case "give":
            case "force":
                {
                    bool force = LiveArgString(n, 0, "") == "force";
                    string name = LiveArgString(n, 1, "");
                    BuddyOxygenShare.HoldFor(name, buddyOxygenHoldSeconds, force);
                    Log.Info($"{VU}vuoxy: give started target={name} seconds={buddyOxygenHoldSeconds:F1} force={force}");
                    break;
                }
            case "giveshot":
                {
                    string shotTarget = LiveArgString(n, 1, "");
                    BuddyOxygenShare.HoldFor(shotTarget, buddyOxygenHoldSeconds, false);
                    Log.Info($"{VU}vuoxy: give started target={shotTarget} seconds={buddyOxygenHoldSeconds:F1} force=False");
                    StartCoroutine(BuddyOxygenShotAfter(2f, "bo-sharing"));
                    break;
                }
            case "shotwhen":
                StartCoroutine(BuddyOxygenShotWhenReceiving("bo-receiving"));
                Log.Info($"{VU}vuoxy: shotwhen armed");
                break;
            case "face":
                BuddyOxygenFace(LiveArgString(n, 1, ""));
                break;
            case "fill":
                BuddyOxygenSet(LiveArgFloat(n, 1, 45f));
                break;
            case "duel":
                BuddyOxygenDuel(LiveArgString(n, 1, ""));
                break;
            case "stop":
                BuddyOxygenShare.ReleaseOverride();
                Log.Info($"{VU}vuoxy: stopped");
                break;
            case "reset":
                BuddyOxygenShare.ResetStats();
                Log.Info($"{VU}vuoxy: counters reset");
                break;
            case "move":
                BuddyOxygenMove(LiveArgString(n, 1, ""));
                break;
            case "killif":
                if (string.Equals(LiveArgString(n, 1, ""), NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName, StringComparison.OrdinalIgnoreCase) && Player.main && Player.main.liveMixin)
                {
                    Player.main.liveMixin.Kill(DamageType.Normal);
                    Log.Info($"{VU}vuoxy: killif killed this player");
                }
                break;
            case "kill":
                if (Player.main && Player.main.liveMixin)
                {
                    Player.main.liveMixin.Kill(DamageType.Normal);
                }
                Log.Info($"{VU}vuoxy: kill sent");
                break;
            default:
                BuddyOxygenState();
                break;
        }
    }

    /// <summary>A picture of this client after a while (the giver's HUD while the air flows).</summary>
    private IEnumerator BuddyOxygenShotAfter(float seconds, string tag)
    {
        yield return new WaitForSeconds(seconds);
        if (!liveShotBusy)
        {
            StartCoroutine(LiveShotRoutine(false, 0f, tag));
        }
    }

    /// <summary>A picture of this client a second after the first air arrived in its tank (the receiver's HUD), waiting 40 s at most.</summary>
    private IEnumerator BuddyOxygenShotWhenReceiving(string tag)
    {
        float before = BuddyOxygenShare.Stats.Received;
        float until = Time.time + 40f;
        while (Time.time < until && BuddyOxygenShare.Stats.Received <= before)
        {
            yield return null;
        }
        yield return new WaitForSeconds(1f);
        if (!liveShotBusy)
        {
            StartCoroutine(LiveShotRoutine(false, 0f, tag));
        }
    }

    /// <summary>Turns the camera of this player at the teammate of that name, like the build harness aims at a spot (yaw and pitch of MainCameraControl), so that the real share key can be held at them.</summary>
    private void BuddyOxygenFace(string name)
    {
        PlayerManager players = NitroxServiceLocator.LocateService<PlayerManager>();
        RemotePlayer target = null;
        foreach (RemotePlayer remote in players.GetAll())
        {
            if (string.Equals(remote.PlayerName, name, StringComparison.OrdinalIgnoreCase))
            {
                target = remote;
            }
        }
        if (target == null || target.Body == null || !MainCamera.camera || !MainCameraControl.main)
        {
            Log.Info($"{VU}vuoxy: face outcome=NoTarget name={name}");
            return;
        }
        Vector3 toTarget = (target.Collider ? target.Collider.bounds.center : target.Body.transform.position) - MainCamera.camera.transform.position;
        MainCameraControl.main.rotationX = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
        MainCameraControl.main.rotationY = -Mathf.Asin(-toTarget.y / toTarget.magnitude) * Mathf.Rad2Deg;
        StartCoroutine(BuddyOxygenFaceReport(target, toTarget.magnitude));
    }

    private static IEnumerator BuddyOxygenFaceReport(RemotePlayer target, float distance)
    {
        // the camera takes the new angles in its next update
        yield return new WaitForSeconds(0.5f);
        Camera camera = Camera.main;
        if (camera && target.Body != null)
        {
            Vector3 center = target.Collider ? target.Collider.bounds.center : target.Body.transform.position;
            Log.Info($"{VU}vuoxy: facing {target.PlayerName} distance={distance:F2} angle={Vector3.Angle(camera.transform.forward, center - camera.transform.position):F1}");
        }
    }

    /// <summary>
    ///     For <c>/consoleall vuoxy duel victim</c>, which runs on every client at the same moment: the client of the player named <paramref name="victim" /> kills its player 1.5 s later, every other one starts holding the share key
    ///     at the victim (for the <c>hold</c> seconds). Giving and dying then begin together, which a live check cannot arrange with two typed commands.
    /// </summary>
    private void BuddyOxygenDuel(string victim)
    {
        string me = NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName;
        if (string.Equals(me, victim, StringComparison.OrdinalIgnoreCase))
        {
            StartCoroutine(BuddyOxygenKillAfter(1.5f));
            Log.Info($"{VU}vuoxy: duel this player dies in 1.5 s");
        }
        else
        {
            BuddyOxygenShare.HoldFor(victim, buddyOxygenHoldSeconds, false);
            Log.Info($"{VU}vuoxy: duel give started target={victim} seconds={buddyOxygenHoldSeconds:F1}");
        }
    }

    private static IEnumerator BuddyOxygenKillAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (Player.main && Player.main.liveMixin)
        {
            Player.main.liveMixin.Kill(DamageType.Normal);
            Log.Info($"{VU}vuoxy: duel killed this player");
        }
    }

    private static void BuddyOxygenSet(float seconds)
    {
        if (!Player.main || !Player.main.oxygenMgr)
        {
            Log.Info($"{VU}vuoxy: set outcome=NoPlayer");
            return;
        }
        float before = Player.main.oxygenMgr.GetOxygenAvailable();
        if (before > seconds)
        {
            Player.main.oxygenMgr.RemoveOxygen(before - seconds);
        }
        else if (before < seconds)
        {
            Player.main.oxygenMgr.AddOxygen(seconds - before);
        }
        Log.Info($"{VU}vuoxy: set wanted={seconds:F1} before={before:F2} after={Player.main.oxygenMgr.GetOxygenAvailable():F2} capacity={Player.main.oxygenMgr.GetOxygenCapacity():F1}");
    }

    private static void BuddyOxygenMove(string text)
    {
        string[] parts = text.Split(',');
        if (!Player.main || parts.Length != 3 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            Log.Info($"{VU}vuoxy: move needs x,y,z (no spaces)");
            return;
        }
        Player.main.SetPosition(new Vector3(x, y, z));
        Log.Info($"{VU}vuoxy: moved to ({x:F1}, {y:F1}, {z:F1})");
    }

    private static void BuddyOxygenState()
    {
        Player player = Player.main;
        if (!player || !player.oxygenMgr)
        {
            Log.Info($"{VU}vuoxy: state outcome=NoPlayer");
            return;
        }
        player.oxygenMgr.GetTotal(out float available, out float capacity);
        BuddyOxygenShare.Totals totals = BuddyOxygenShare.Stats;
        string sharing = BuddyOxygenShare.SharingWith.HasValue ? BuddyOxygenShare.SharingWith.Value.ToString() : "none";
        StringBuilder line = new();
        line.Append($"{VU}vuoxy: state available={available:F2} capacity={capacity:F1} alive={(player.liveMixin && player.liveMixin.IsAlive())} underwater={player.IsUnderwater()} inSub={player.IsInSub()} ");
        line.Append($" vehicle={(player.currentMountedVehicle ? player.currentMountedVehicle.name : "none")} active={BuddyOxygenShare.Active} rate={VehicleUpgradeSettings.BuddyOxygenRate:F1} range={VehicleUpgradeSettings.BuddyOxygenRange:F1} ");
        line.Append($" floor={VehicleUpgradeSettings.BuddyOxygenFloor:F1} sharing={sharing} gives={totals.Gives} taken={totals.Taken:F3} settles={totals.Settles} accepted={totals.Accepted:F3} refunded={totals.Refunded:F3} ");
        line.Append($" refundApplied={totals.RefundApplied:F3} refused={totals.Refused} deliveries={totals.Deliveries} received={totals.Received:F3} nothing={totals.DeliveriesWithNothing} pos={player.transform.position.ToString("F1")} giveFirstAt={totals.GiveFirstAt:F2} giveFirstBefore={totals.GiveFirstBefore:F2} giveLastAt={totals.GiveLastAt:F2} giveLastAfter={totals.GiveLastAfter:F2} deliverFirstAt={totals.DeliverFirstAt:F2} deliverFirstBefore={totals.DeliverFirstBefore:F2} deliverLastAt={totals.DeliverLastAt:F2} deliverLastAfter={totals.DeliverLastAfter:F2}");
        Log.Info(line.ToString());
    }
}
#endif
