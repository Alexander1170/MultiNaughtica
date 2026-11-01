#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.Subnautica.Helper;
using UnityEngine;
using StoryBeacons = NitroxClient.GameLogic.StoryBeacons;
using StoryGoalManager = Story.StoryGoalManager;

namespace NitroxClient.MonoBehaviours;

// Live check "beacons" : the lifepod beacons (the signals that the radio messages and other story goals hand out) on both clients, a late joiner and after a
// server restart. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's
// "/console <player> <command> [arg]" (or "/consoleall <command>") and log "[SyncHarness] <name>: key=value ..." lines (no spaces inside a value).
//
//   beacons                  the beacons that stand on THIS client: one "ping" line each (description key, label, position, distance, visible / registered / initialized / active), the "missing" lines
//                            (a goal that is complete here whose beacon does not stand) and ONE summary line last:
//                            "[SyncHarness] beacons: player=<name> pings=<n> expected=<n> missing=<n> table=<n> keys=<key>|<key>|..." (keys sorted). Run it on both clients after every message
//                            and compare the "keys=" values (and "missing=0")
//   signalgoals              the signal table of the game (Unity data, OnGoalUnlockData): one line per signal with the goal that unlocks it, the description key and its text, the position and whether
//                            the goal is complete here and the beacon stands. Run it once to read the real goal keys of the lifepod messages
//   beaconfix                runs the safety net now (StoryBeacons.EnsureAll) and logs how many beacons it created or registered again
//   beaconkill <key> [unregister]  destroys the beacon(s) whose description key contains the text (or, with "unregister", only removes them from the ping manager), on this client only: the
//                            failure that the safety net has to repair. Run beaconfix (or wait half a minute for the watchdog) afterwards
//   radioplay                plays the oldest pending radio message through the radio of the lifepod (Radio.PlayRadioMessage, what the click does after 1.25 s, so Nitrox sends the play to the server and
//                            the other player), logs the message, what is left in the queue and whether the game completed the played-goal
//   radiostate               the pending radio messages of this client (in order) and, for the first, whether its played-goal is complete, plus the health of the radio
public partial class HarnessConsoleCommands
{
    private static string BcnMe()
    {
        try
        {
            string name = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>().PlayerName;
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string BcnToken(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }

        return text.Replace(' ', '_').Replace('|', '_').Replace('=', '_');
    }

    private static string BcnNum(float value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string BcnPos(Vector3 position)
    {
        return $"{BcnNum(position.x)},{BcnNum(position.y)},{BcnNum(position.z)}";
    }

    private static string BcnPos(BeaconSpec spec)
    {
        return $"{BcnNum(spec.X)},{BcnNum(spec.Y)},{BcnNum(spec.Z)}";
    }

    private static List<BeaconSpec> BcnSpecs(List<StoryBeacons.TableEntry> table)
    {
        List<BeaconSpec> specs = new List<BeaconSpec>(table.Count);
        foreach (StoryBeacons.TableEntry entry in table)
        {
            specs.Add(entry.Spec);
        }

        return specs;
    }

    private void LiveCommand_beacons(NotificationCenter.Notification n)
    {
        try
        {
            StoryGoalManager manager = StoryGoalManager.main;
            if (!manager)
            {
                Log.Warn("[SyncHarness] beacons: failed reason=no-story-goal-manager");
                return;
            }

            List<StoryBeacons.TableEntry> table = StoryBeacons.ReadTable();
            List<SignalPing> pings = StoryBeacons.ReadPings();
            Vector3 playerPosition = Player.main ? Player.main.transform.position : Vector3.zero;

            List<string> keys = new List<string>();
            foreach (SignalPing ping in pings)
            {
                PingInstance instance = ping.pingInstance;
                bool initialized = instance && instance.Initialized;
                bool registered = initialized && PingManager.Get(instance.Id) == instance;
                bool visible = instance && instance.visible;
                string label = instance ? instance.GetLabel() : null;
                float distance = Vector3.Distance(playerPosition, ping.pos);
                keys.Add(ping.descriptionKey ?? "none");
                Log.Info($"[SyncHarness] beacons: ping key={BcnToken(ping.descriptionKey)} label={BcnToken(label)} pos={BcnPos(ping.pos)} dist={BcnNum(distance)} visible={visible} registered={registered} initialized={initialized} active={ping.gameObject.activeInHierarchy}");
            }

            keys.Sort(StringComparer.Ordinal);

            List<BeaconSpec> specs = BcnSpecs(table);
            List<BeaconSpec> expected = StoryBeaconPlan.Missing(specs, goal => manager.completedGoals.Contains(goal), spec => false);
            List<BeaconSpec> missing = StoryBeaconPlan.Missing(specs, goal => manager.completedGoals.Contains(goal), spec => StoryBeacons.IsPresent(spec, pings));
            foreach (BeaconSpec spec in missing)
            {
                Log.Info($"[SyncHarness] beacons: missing goal={BcnToken(spec.Goal)} key={BcnToken(spec.DescriptionKey)} pos={BcnPos(spec)}");
            }

            Log.Info($"[SyncHarness] beacons: player={BcnMe()} pings={pings.Count} expected={expected.Count} missing={missing.Count} table={table.Count} keys={(keys.Count > 0 ? string.Join("|", keys.ToArray()) : "none")}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] beacons: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }

    private void LiveCommand_signalgoals(NotificationCenter.Notification n)
    {
        try
        {
            StoryGoalManager manager = StoryGoalManager.main;
            if (!manager)
            {
                Log.Warn("[SyncHarness] signalgoals: failed reason=no-story-goal-manager");
                return;
            }

            List<StoryBeacons.TableEntry> table = StoryBeacons.ReadTable();
            List<SignalPing> pings = StoryBeacons.ReadPings();
            int complete = 0;
            int present = 0;
            foreach (StoryBeacons.TableEntry entry in table)
            {
                bool goalComplete = manager.completedGoals.Contains(entry.Spec.Goal);
                bool standing = StoryBeacons.IsPresent(entry.Spec, pings);
                complete += goalComplete ? 1 : 0;
                present += standing ? 1 : 0;
                Log.Info($"[SyncHarness] signalgoals: goal={BcnToken(entry.Spec.Goal)} key={BcnToken(entry.Spec.DescriptionKey)} text={BcnToken(Language.main.Get(entry.Spec.DescriptionKey))} pos={BcnPos(entry.Spec)} complete={goalComplete} present={standing}");
            }

            Log.Info($"[SyncHarness] signalgoals: player={BcnMe()} entries={table.Count} complete={complete} present={present}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] signalgoals: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }

    private void LiveCommand_beaconfix(NotificationCenter.Notification n)
    {
        try
        {
            int before = StoryBeacons.ReadPings().Count;
            int changed = StoryBeacons.EnsureAll("probe");
            int after = StoryBeacons.ReadPings().Count;
            Log.Info($"[SyncHarness] beaconfix: player={BcnMe()} changed={changed} pings_before={before} pings_after={after}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] beaconfix: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }

    private void LiveCommand_beaconkill(NotificationCenter.Notification n)
    {
        try
        {
            string text = LiveArgString(n, 0, "");
            bool unregisterOnly = LiveArgString(n, 1, "") == "unregister";
            if (text.Length == 0)
            {
                Log.Warn("[SyncHarness] beaconkill: failed reason=needs-a-description-key-or-part-of-it");
                return;
            }

            int hit = 0;
            foreach (SignalPing ping in StoryBeacons.ReadPings())
            {
                if (string.IsNullOrEmpty(ping.descriptionKey) || ping.descriptionKey.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                hit++;
                if (unregisterOnly)
                {
                    if (ping.pingInstance && ping.pingInstance.Initialized)
                    {
                        PingManager.Unregister(ping.pingInstance);
                    }
                }
                else
                {
                    Destroy(ping.gameObject);
                }
            }

            Log.Info($"[SyncHarness] beaconkill: player={BcnMe()} text={BcnToken(text)} mode={(unregisterOnly ? "unregister" : "destroy")} hit={hit}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] beaconkill: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }

    private void LiveCommand_radioplay(NotificationCenter.Notification n)
    {
        try
        {
            StoryGoalManager manager = StoryGoalManager.main;
            Radio radio = FindObjectOfType<Radio>();
            if (!manager || !radio)
            {
                Log.Warn($"[SyncHarness] radioplay: failed reason=no-{(manager ? "radio" : "story-goal-manager")}");
                return;
            }

            if (manager.pendingRadioMessages.Count == 0)
            {
                Log.Info($"[SyncHarness] radioplay: player={BcnMe()} played=none pending_left=0 reason=nothing-pending");
                return;
            }

            string head = manager.pendingRadioMessages[0];
            radio.PlayRadioMessage();
            Log.Info($"[SyncHarness] radioplay: player={BcnMe()} played={BcnToken(head)} pending_left={manager.pendingRadioMessages.Count} played_goal_complete={manager.IsGoalComplete(RadioGoalKeys.PlayedGoalOf(head))}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] radioplay: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }

    private void LiveCommand_radiostate(NotificationCenter.Notification n)
    {
        try
        {
            StoryGoalManager manager = StoryGoalManager.main;
            if (!manager)
            {
                Log.Warn("[SyncHarness] radiostate: failed reason=no-story-goal-manager");
                return;
            }

            Radio radio = FindObjectOfType<Radio>();
            string pending = manager.pendingRadioMessages.Count > 0 ? string.Join("|", manager.pendingRadioMessages.ToArray()) : "none";
            string head = manager.pendingRadioMessages.Count > 0 ? manager.pendingRadioMessages[0] : null;
            string playedGoal = head != null ? RadioGoalKeys.PlayedGoalOf(head) : "none";
            bool playedGoalComplete = head != null && manager.IsGoalComplete(playedGoal);
            Log.Info($"[SyncHarness] radiostate: player={BcnMe()} pending={pending} head={BcnToken(head)} head_played_goal={BcnToken(playedGoal)} head_played_goal_complete={playedGoalComplete} radio={(radio ? "found" : "none")} radio_full_health={(radio ? radio.IsFullHealth().ToString() : "n/a")}");
        }
        catch (Exception e)
        {
            Log.Warn($"[SyncHarness] radiostate: failed reason={e.GetType().Name}:{BcnToken(e.Message)}");
        }
    }
}
#endif
