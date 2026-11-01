using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Subnautica.Helper;
using NitroxClient.MonoBehaviours;
using Story;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// The beacons of the story (the signals of the game's unlock table: six lifepods, the Degasi bases, the Aurora rendezvous point; Lifepod 4, 6 and 7 have none) on a multiplayer client.<br />
/// Vanilla hands a beacon out in the one moment its goal completes: <c>OnGoalUnlockTracker.NotifyGoalComplete</c> takes the unlock out of its table and instantiates the
/// signals; they live on in the saved game. A client has no saved pings. It builds the table when it joins and every goal that completes later (its own or one that the other
/// player reported) goes through that one moment, so a beacon that is missed there is missed for good, and the other player, whose moment went well, has it
/// (the first lifepod's beacon appeared in the first real play session, no later one did; the goals of all ten radio messages had completed on both clients).
/// This class states the invariant of vanilla for every client, on every goal, at every join: <b>a goal that is complete has its beacons</b> (<see cref="StoryBeaconPlan" />).
/// It adds nothing that vanilla would not add; a beacon that stands is never touched.
/// </summary>
public static class StoryBeacons
{
    private const float CHECK_DELAY_SECONDS = 3f;
    private const float FIRST_WATCHDOG_SECONDS = 10f;
    private const float WATCHDOG_SECONDS = 30f;

    private static readonly Dictionary<string, GameObject> createdHere = new();
    private static Multiplayer queuedOn;
    private static Multiplayer watchdogHost;

    /// <summary>
    /// A signal of the game's unlock table and the goal that unlocks it.
    /// </summary>
    internal sealed class TableEntry
    {
        public BeaconSpec Spec;
        public UnlockSignalData Data;
    }

    /// <summary>
    /// Every signal of the game's unlock data (<c>OnGoalUnlockData</c>, Unity data that no IL shows), with its goal.
    /// </summary>
    internal static List<TableEntry> ReadTable()
    {
        List<TableEntry> table = new();
        StoryGoalManager manager = StoryGoalManager.main;
        if (!manager || !manager.onGoalUnlockTracker || !manager.onGoalUnlockTracker.unlockData || manager.onGoalUnlockTracker.unlockData.onGoalUnlocks == null)
        {
            return table;
        }

        foreach (OnGoalUnlock unlock in manager.onGoalUnlockTracker.unlockData.onGoalUnlocks)
        {
            if (unlock == null || unlock.signals == null)
            {
                continue;
            }

            foreach (UnlockSignalData data in unlock.signals)
            {
                if (data == null)
                {
                    continue;
                }

                Vector3 position = data.targetPosition;
                table.Add(new TableEntry { Spec = new BeaconSpec(unlock.goal, data.targetDescription, position.x, position.y, position.z), Data = data });
            }
        }

        return table;
    }

    /// <summary>
    /// The beacons that stand on this client (the ones the game made, the join made and this class made), switched off ones included.
    /// </summary>
    internal static List<SignalPing> ReadPings()
    {
        List<SignalPing> pings = new();
        foreach (SignalPing ping in Resources.FindObjectsOfTypeAll<SignalPing>())
        {
            // Loaded assets (the prefab) are not in a scene; the prototype of the remote players' pings has no description
            if (ping && ping.gameObject.scene.IsValid() && !string.IsNullOrEmpty(ping.descriptionKey))
            {
                pings.Add(ping);
            }
        }

        return pings;
    }

    internal static bool IsPresent(BeaconSpec spec, List<SignalPing> pings)
    {
        foreach (SignalPing ping in pings)
        {
            if (spec.IsSameSpot(ping.descriptionKey, ping.pos.x, ping.pos.y, ping.pos.z))
            {
                return true;
            }
        }

        // One that this class made is not made a second time while its object lives
        return createdHere.TryGetValue(CreatedKey(spec), out GameObject made) && made;
    }

    /// <summary>
    /// Makes sure that every beacon of a complete goal stands and is registered. Safe to call at any time after the join; does nothing before it.
    /// </summary>
    /// <returns>How many beacons were created or registered again</returns>
    internal static int EnsureAll(string reason)
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return 0;
        }

        try
        {
            StoryGoalManager manager = StoryGoalManager.main;
            List<TableEntry> table = ReadTable();
            if (!manager || table.Count == 0)
            {
                return 0;
            }

            List<SignalPing> pings = ReadPings();
            int registeredAgain = RegisterUnregistered(pings, reason);

            List<BeaconSpec> specs = new(table.Count);
            foreach (TableEntry entry in table)
            {
                specs.Add(entry.Spec);
            }

            List<BeaconSpec> missing = StoryBeaconPlan.Missing(specs, goal => manager.completedGoals.Contains(goal), spec => IsPresent(spec, pings));
            int created = 0;
            foreach (BeaconSpec spec in missing)
            {
                TableEntry entry = table.Find(candidate => candidate.Spec == spec);
                GameObject made = entry != null ? Create(manager.onGoalUnlockTracker, entry.Data) : null;
                if (made)
                {
                    createdHere[CreatedKey(spec)] = made;
                    created++;
                    Log.Info($"[Beacons] ({reason}) the goal {spec.Goal} is complete but its beacon was missing on this client: created {spec}");
                }
                else
                {
                    Log.Warn($"[Beacons] ({reason}) the goal {spec.Goal} is complete but its beacon was missing on this client, and it could not be created ({spec})");
                }
            }

            if (created > 0 || registeredAgain > 0)
            {
                Log.Info($"[Beacons] ({reason}) {specs.Count} beacons in the game's table, {created} created, {registeredAgain} registered again");
            }

            return created + registeredAgain;
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[Beacons] check ({reason}) failed");
            return 0;
        }
    }

    /// <summary>
    /// A goal completed (here or on the other player's client, the game does not tell the two apart): say so in the log when it carries beacons, and look at them in a moment.
    /// </summary>
    public static void OnGoalCompleted(string key, bool newlyCompleted)
    {
        if (!newlyCompleted || !Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        try
        {
            List<string> keys = new();
            foreach (TableEntry entry in ReadTable())
            {
                if (string.Equals(entry.Spec.Goal, key, StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(entry.Spec.DescriptionKey);
                }
            }

            if (keys.Count == 0)
            {
                return;
            }

            // The game takes the unlock out of its table in the moment it makes the beacons: still in the table means the beacons were not made
            bool stillInTable = StoryGoalManager.main.onGoalUnlockTracker.goalUnlocks.ContainsKey(key);
            Log.Info($"[Beacons] the goal {key} completed here and unlocks {keys.Count} beacon(s) ({string.Join(", ", keys.ToArray())}); the game's table still holds the unlock: {stillInTable}");
            RequestCheck($"goal {key}");
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[Beacons] goal {key}");
        }
    }

    /// <summary>
    /// Looks at the beacons once, in a moment (the ones that the game makes are made in the frame of the goal and register themselves in the next one).
    /// </summary>
    internal static void RequestCheck(string reason, float delaySeconds = CHECK_DELAY_SECONDS)
    {
        Multiplayer host = Multiplayer.Main;
        if (!host || queuedOn == host)
        {
            return;
        }

        queuedOn = host;
        host.StartCoroutine(DelayedCheck(reason, delaySeconds));
    }

    /// <summary>
    /// Looks at the beacons now and then for the rest of the session: the join and the goals are the moments that matter, the rest is a safety net that costs one scan of the
    /// unlock table every half minute.
    /// </summary>
    internal static void StartWatchdog()
    {
        Multiplayer host = Multiplayer.Main;
        if (!host || watchdogHost == host)
        {
            return;
        }

        watchdogHost = host;
        host.StartCoroutine(Watchdog());
    }

    private static IEnumerator DelayedCheck(string reason, float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        queuedOn = null;
        EnsureAll(reason);
    }

    private static IEnumerator Watchdog()
    {
        yield return new WaitForSeconds(FIRST_WATCHDOG_SECONDS);
        while (true)
        {
            EnsureAll("watchdog");
            yield return new WaitForSeconds(WATCHDOG_SECONDS);
        }
    }

    /// <summary>
    /// The same instantiation as <c>UnlockSignalData.Trigger</c>, without the voice line (it would announce a beacon the player may have heard of long ago).
    /// </summary>
    private static GameObject Create(OnGoalUnlockTracker tracker, UnlockSignalData data)
    {
        if (!tracker || !tracker.signalPrefab || data == null)
        {
            return null;
        }

        GameObject made = UnityEngine.Object.Instantiate(tracker.signalPrefab);
        SignalPing ping = made.GetComponent<SignalPing>();
        if (!ping)
        {
            UnityEngine.Object.Destroy(made);
            return null;
        }

        ping.pos = data.targetPosition;
        ping.descriptionKey = data.targetDescription;
        return made;
    }

    /// <summary>
    /// A beacon whose ping is initialized but unknown to the ping manager is on neither the HUD nor the PDA list; one that the manager knows but the HUD does not have missed its
    /// notification (the HUD takes the pings of the manager when it is enabled and then listens for new ones).
    /// </summary>
    private static int RegisterUnregistered(List<SignalPing> pings, string reason)
    {
        uGUI_Pings hud = UnityEngine.Object.FindObjectOfType<uGUI_Pings>();
        int count = 0;
        foreach (SignalPing ping in pings)
        {
            PingInstance instance = ping.pingInstance;
            if (!instance)
            {
                continue;
            }

            bool initialized = instance.Initialized;
            bool registered = initialized && PingManager.Get(instance.Id) == instance;
            if (StoryBeaconPlan.NeedsRegistering(initialized, registered))
            {
                PingManager.Register(instance);
                count++;
                Log.Info($"[Beacons] ({reason}) the beacon {ping.descriptionKey} was not known to the ping manager: registered it again");
                registered = PingManager.Get(instance.Id) == instance;
            }

            if (registered && hud && hud.pings != null && !hud.pings.ContainsKey(instance.Id))
            {
                hud.OnAdd(instance);
                count++;
                Log.Info($"[Beacons] ({reason}) the beacon {ping.descriptionKey} was known to the ping manager but not on the HUD: added it");
            }
        }

        return count;
    }

    private static string CreatedKey(BeaconSpec spec)
    {
        return $"{spec.DescriptionKey}@{Math.Round(spec.X)},{Math.Round(spec.Y)},{Math.Round(spec.Z)}";
    }
}
