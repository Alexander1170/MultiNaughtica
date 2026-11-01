#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using Nitrox.Model.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.HUD;
using NitroxClient.GameLogic.HUD.PdaTabs;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "player_stats" (a harness script that is not part of this repository, item c): the stats under the names of the other players in the PDA's player list. Debug builds only.
// Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]"
// (a player, the command and TWO words after it; /consoleall the command and two): the modes carry what would need a third word.
//
//   pdastatsprobe [open|close|watch <seconds>|hold <seconds>]
//                            on the VIEWER: without a mode it only reads; "open" opens the PDA on the player list (tab 8) first, logs and closes it again; "watch <s>" opens it and logs every 0.5 s
//                            for s seconds and closes it again, "hold <s>" is the same but leaves the PDA open (a picture), "close" closes the PDA. One line per other player and one for this client:
//                              [SyncHarness] pdastatsprobe: n=.. t=.. player=<name> pdaOpen=.. tabOpen=.. shownName=.. shownVitals=.. shownPlace=.. shownVehicle=.. wantVitals=.. wantPlace=.. wantVehicle=.. same=.. fresh=..
//                                hp=.. food=.. water=.. age=.. stale=.. mode=.. rx=.. ry=.. rz=.. lx=.. ly=.. lz=.. ocean=.. dist=.. depth=.. vehicle=.. rawVehicle=.. rawSub=.. rawPod=.. chair=..
//                                rowH=.. refreshes=.. assigns=..
//                              [SyncHarness] pdastatsprobe: n=.. self hp=.. food=.. water=.. x=.. y=.. z=.. depth=.. vehicle=.. mode=..
//                            shown* come from the real label of the row (spaces as _), want* are made from the raw numbers on the same line by the same rules at the same moment. same= says the row
//                            shows exactly the text it was last given (a row that shows something else is wrong); fresh= says it also equals what the numbers make right now (it can be False for up to a
//                            second after a packet changed a shown number: the row is rebuilt once a second, so fresh= is information and same= is the check). hp/food/water/age are what the viewer
//                            kept of the PlayerStats packets, the self line is what the sender reads for them
//   pdastatsset <h,f,w>      sets this client's health, food and water ("-" keeps one: "40,-,20"); the next PlayerStats packet (within 3 s) carries them. Survival keeps changing them slowly
//   pdastatsmute <seconds>   stops this client's PlayerStatsBroadcaster for that long (the viewer's numbers go stale after 10 s), logs "pdastatsmute: resumed" when it is back
//   pdastatsexit             gets this player out of the vehicle he is in and next to it (Player.ExitLockedMode with a new position, what the exit key does for a Seamoth or a Prawn suit): the live check needs to leave one vehicle before it
//                            boards the next. Logs "pdastatsexit: before=<vehicle> exited=<bool> after=<vehicle>"; the check judges what the viewer sees afterwards, not this line
//   pdastatsui               one-off dump of the geometry of the player list (the tab, a few rows, the label of the first row, the buttons) for the height of the rows
//
// Every command logs "[SyncHarness] <command>: ..." lines with key=value pairs, or "failed reason=<text>". Nothing throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private const PDATab PLAYER_LIST_TAB = (PDATab)8;

    private bool pdaStatsBusy;
    private bool pdaStatsMuteActive;

    private static string PdaStatsText(string text)
    {
        return string.IsNullOrEmpty(text) ? "none" : text.Replace(' ', '_').Replace("\n", "/");
    }

    private static string PdaStatsF(double value)
    {
        return double.IsNaN(value) ? "n/a" : value.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static uGUI_PlayerListTab PdaStatsTab()
    {
        NitroxPDATabManager manager = NitroxServiceLocator.LocateService<NitroxPDATabManager>();
        return manager != null && manager.CustomTabs.TryGetValue(PLAYER_LIST_TAB, out NitroxPDATab tab) ? tab.uGUI_PDATab as uGUI_PlayerListTab : null;
    }

    private static bool PdaStatsTabIsShown()
    {
        PDA pda = Player.main ? Player.main.GetPDA() : null;
        uGUI_PDA ui = uGUI_PDA.main;
        return pda && pda.isOpen && ui && ui.currentTabType == PLAYER_LIST_TAB;
    }

    private static string PdaStatsVehicleName(Vehicle vehicle)
    {
        return vehicle ? vehicle.GetType().Name : "none";
    }

    private static void LogPdaStatsLines(int n)
    {
        uGUI_PlayerListTab tab = PdaStatsTab();
        PlayerManager playerManager = NitroxServiceLocator.LocateService<PlayerManager>();
        float now = Time.unscaledTime;
        bool pdaOpen = Player.main && Player.main.GetPDA() && Player.main.GetPDA().isOpen;
        bool tabOpen = PdaStatsTabIsShown();
        PlayerListStatsRules.StatsLabels labels = PlayerListStatsSource.Labels;
        if (tab && playerManager != null)
        {
            foreach (RemotePlayer remote in playerManager.GetAll())
            {
                uGUI_PlayerPingEntry row = FindPdaStatsRow(tab, remote);
                PlayerListStatsRules.StatsSnapshot snapshot = PlayerListStatsSource.Capture(remote, now);
                PlayerListStatsRules.StatsLine want = PlayerListStatsRules.Format(snapshot, labels);
                // the row is the name, then the lines it was given: the vitals (not in Creative), the place, the vehicle (when there is one); the stats lines sit inside <size=..%> .. </size>
                PlayerListStatsRules.StatsLine given = row ? row.LastLine : default;
                string[] shown = row ? row.ShownText.Split('\n') : new string[0];
                int index = 1;
                string shownName = shown.Length > 0 ? shown[0].Replace("<noparse>", string.Empty).Replace("</noparse>", string.Empty) : string.Empty;
                string shownVitals = given.Vitals.Length > 0 && shown.Length > index ? PdaStatsStrip(shown[index++]) : string.Empty;
                string shownPlace = given.Place.Length > 0 && shown.Length > index ? PdaStatsStrip(shown[index++]) : string.Empty;
                string shownVehicle = given.Vehicle.Length > 0 && shown.Length > index ? PdaStatsStrip(shown[index++]) : string.Empty;
                bool same = shownVitals == given.Vitals && shownPlace == given.Place && shownVehicle == given.Vehicle && shown.Length == index;
                bool fresh = shownVitals == want.Vitals && shownPlace == want.Place && shownVehicle == want.Vehicle;
                PlayerListStatsRules.VitalsSample vitals = remote.LastVitals;
                float age = vitals.AgeAt(now);
                Vector3 remotePosition = remote.Body ? remote.Body.transform.position : new Vector3(float.NaN, float.NaN, float.NaN);
                Vector3 localPosition = Player.main ? Player.main.transform.position : new Vector3(float.NaN, float.NaN, float.NaN);
                double distance = snapshot.Remote.IsKnown && snapshot.Local.IsKnown ? PlayerListStatsRules.Distance(snapshot.Remote, snapshot.Local) : double.NaN;
                double depth = snapshot.Remote.IsKnown ? snapshot.OceanLevel - snapshot.Remote.Y : double.NaN;
                Log.Info(string.Concat(
                    $"[SyncHarness] pdastatsprobe: n={n} t={PdaStatsF(Time.realtimeSinceStartup)} player={remote.PlayerName.Replace(' ', '_')} pdaOpen={pdaOpen} tabOpen={tabOpen} shownName={PdaStatsText(shownName)} shownVitals={PdaStatsText(shownVitals)} shownPlace={PdaStatsText(shownPlace)} shownVehicle={PdaStatsText(shownVehicle)} ",
                    $"wantVitals={PdaStatsText(want.Vitals)} wantPlace={PdaStatsText(want.Place)} wantVehicle={PdaStatsText(want.Vehicle)} same={same} fresh={fresh} hp={PdaStatsF(vitals.HasValue ? vitals.Health : double.NaN)} food={PdaStatsF(vitals.HasValue ? vitals.Food : double.NaN)} ",
                    $"water={PdaStatsF(vitals.HasValue ? vitals.Water : double.NaN)} age={PdaStatsF(age)} stale={PlayerListStatsRules.IsStale(age)} mode={remote.PlayerContext.GameMode} ",
                    $"rx={PdaStatsF(remotePosition.x)} ry={PdaStatsF(remotePosition.y)} rz={PdaStatsF(remotePosition.z)} lx={PdaStatsF(localPosition.x)} ly={PdaStatsF(localPosition.y)} lz={PdaStatsF(localPosition.z)} ",
                    $"ocean={PdaStatsF(snapshot.OceanLevel)} dist={PdaStatsF(distance)} depth={PdaStatsF(depth)} vehicle={snapshot.Vehicle} rawVehicle={PdaStatsVehicleName(remote.Vehicle)} ",
                    $"rawSub={(remote.SubRoot ? remote.SubRoot.name.Replace(' ', '_') : "none")} rawPod={(remote.EscapePod ? "yes" : "none")} chair={(remote.PilotingChair ? "yes" : "none")} ",
                    $"rowH={(row ? PdaStatsF(row.rectTransform.rect.height) : "n/a")} refreshes={tab.StatsRefreshCount} assigns={(row ? row.AssignCount : -1)}"));
            }
        }

        LogPdaStatsSelf(n, now);
    }

    private static string PdaStatsStrip(string line)
    {
        return line.Replace("<size=65%>", string.Empty).Replace("</size>", string.Empty);
    }

    private static uGUI_PlayerPingEntry FindPdaStatsRow(uGUI_PlayerListTab tab, RemotePlayer remote)
    {
        foreach (uGUI_PlayerPingEntry candidate in tab.GetComponentsInChildren<uGUI_PlayerPingEntry>(true))
        {
            if (candidate && ReferenceEquals(candidate.NitroxPlayer, remote))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void LogPdaStatsSelf(int n, float now)
    {
        if (!Player.main)
        {
            Log.Info($"[SyncHarness] pdastatsprobe: n={n} self failed reason=no local player");
            return;
        }

        Survival survival = Player.main.GetComponent<Survival>();
        Vector3 position = Player.main.transform.position;
        Vehicle vehicle = Player.main.currentMountedVehicle;
        SubRoot sub = Player.main.currentSub;
        PlayerListStatsRules.RemoteVehicleKind kind = PlayerListStatsRules.ClassifyVehicle(vehicle, vehicle is SeaMoth, vehicle is Exosuit, Player.main.isPiloting || (sub && sub.isCyclops), sub && sub.isBase, Player.main.currentEscapePod);
        Log.Info($"[SyncHarness] pdastatsprobe: n={n} self hp={PdaStatsF(Player.main.liveMixin.health)} food={PdaStatsF(survival ? survival.food : double.NaN)} water={PdaStatsF(survival ? survival.water : double.NaN)} x={PdaStatsF(position.x)} y={PdaStatsF(position.y)} z={PdaStatsF(position.z)} depth={PdaStatsF(Ocean.GetDepthOf(position))} vehicle={kind} mode={GameModeUtils.currentGameMode}");
    }

    private void LiveCommand_pdastatsprobe(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, string.Empty);
            if (mode == "close")
            {
                PDA pda = Player.main ? Player.main.GetPDA() : null;
                if (pda && pda.isInUse)
                {
                    pda.Close();
                }

                Log.Info("[SyncHarness] pdastatsprobe: closed");
                return;
            }

            if (mode == "open" || mode == "watch" || mode == "hold")
            {
                if (pdaStatsBusy)
                {
                    Log.Info("[SyncHarness] pdastatsprobe: failed reason=the previous pdastatsprobe is still running");
                    return;
                }

                StartCoroutine(PdaStatsProbeRoutine(mode == "open" ? 0f : Mathf.Clamp(LiveArgFloat(n, 1, 10f), 1f, 600f), mode == "hold"));
                return;
            }

            LogPdaStatsLines(1);
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdastatsprobe: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator PdaStatsProbeRoutine(float watchSeconds, bool stay)
    {
        pdaStatsBusy = true;
        try
        {
            PDA pda = null;
            string failure = null;
            bool openedHere = false;
            try
            {
                pda = Player.main ? Player.main.GetPDA() : null;
                if (!pda)
                {
                    failure = "no PDA (the client is not in the world)";
                }
                else if (!pda.isInUse)
                {
                    openedHere = pda.Open(PLAYER_LIST_TAB, null, null);
                    if (!openedHere)
                    {
                        failure = "the PDA refused to open";
                    }
                }
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }

            if (failure == null)
            {
                float deadline = Time.unscaledTime + 8f;
                while (Time.unscaledTime < deadline && !PdaStatsTabIsShown())
                {
                    yield return null;
                }

                if (!PdaStatsTabIsShown())
                {
                    failure = "the player list did not open within 8 s (the PDA is on another tab?)";
                }
            }

            if (failure != null)
            {
                Log.Info($"[SyncHarness] pdastatsprobe: failed reason={failure}");
                yield break;
            }

            // the rows get their lines in the first late update after the tab is shown (at once, then every second)
            yield return new WaitForSecondsRealtime(0.6f);
            if (watchSeconds <= 0f)
            {
                LogPdaStatsLines(1);
            }
            else
            {
                float end = Time.realtimeSinceStartup + watchSeconds;
                int count = 0;
                while (Time.realtimeSinceStartup < end)
                {
                    LogPdaStatsLines(++count);
                    yield return new WaitForSecondsRealtime(0.5f);
                }

                Log.Info("[SyncHarness] pdastatsprobe: done");
            }

            if (openedHere && !stay && pda && pda.isInUse)
            {
                pda.Close();
            }
        }
        finally
        {
            pdaStatsBusy = false;
        }
    }

    private void LiveCommand_pdastatsset(NotificationCenter.Notification n)
    {
        try
        {
            string[] parts = LiveArgString(n, 0, string.Empty).Split(',');
            if (parts.Length != 3 || !Player.main)
            {
                Log.Info("[SyncHarness] pdastatsset: failed reason=needs health,food,water (\"-\" keeps one) and a player in the world");
                return;
            }

            Survival survival = Player.main.GetComponent<Survival>();
            float value;
            if (parts[0] != "-" && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                Player.main.liveMixin.health = value;
            }

            if (survival && parts[1] != "-" && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                survival.food = value;
            }

            if (survival && parts[2] != "-" && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                survival.water = value;
            }

            Log.Info($"[SyncHarness] pdastatsset: hp={PdaStatsF(Player.main.liveMixin.health)} food={PdaStatsF(survival ? survival.food : double.NaN)} water={PdaStatsF(survival ? survival.water : double.NaN)}");
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdastatsset: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_pdastatsmute(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 14f), 1f, 120f);
        if (pdaStatsMuteActive)
        {
            Log.Info("[SyncHarness] pdastatsmute: failed reason=already running, wait for \"pdastatsmute: resumed\"");
            return;
        }

        PlayerStatsBroadcaster broadcaster = FindObjectOfType<PlayerStatsBroadcaster>();
        if (!broadcaster)
        {
            Log.Info("[SyncHarness] pdastatsmute: failed reason=no PlayerStatsBroadcaster (not in a multiplayer session)");
            return;
        }

        StartCoroutine(PdaStatsMute(broadcaster, seconds));
    }

    private IEnumerator PdaStatsMute(PlayerStatsBroadcaster broadcaster, float seconds)
    {
        pdaStatsMuteActive = true;
        broadcaster.enabled = false;
        Log.Info($"[SyncHarness] pdastatsmute: stats broadcast stopped for {seconds:F0} s");
        yield return new WaitForSeconds(seconds);
        if (broadcaster)
        {
            broadcaster.enabled = true;
        }

        pdaStatsMuteActive = false;
        Log.Info("[SyncHarness] pdastatsmute: resumed");
    }

    private void LiveCommand_pdastatsexit(NotificationCenter.Notification n)
    {
        try
        {
            if (!Player.main)
            {
                Log.Info("[SyncHarness] pdastatsexit: failed reason=no local player");
                return;
            }

            string before = PdaStatsVehicleName(Player.main.currentMountedVehicle);
            // findNewPosition = true: the player is put next to the vehicle, like the exit key does it (Player.TryEject), and not left inside its hull
            bool exited = Player.main.currentMountedVehicle && Player.main.ExitLockedMode(false, true);
            Log.Info($"[SyncHarness] pdastatsexit: before={before} exited={exited} after={PdaStatsVehicleName(Player.main.currentMountedVehicle)}");
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdastatsexit: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_pdastatsui(NotificationCenter.Notification n)
    {
        try
        {
            uGUI_PlayerListTab tab = PdaStatsTab();
            if (!tab)
            {
                Log.Info("[SyncHarness] pdastatsui: failed reason=no player list tab");
                return;
            }

            int index = 0;
            foreach (uGUI_PlayerPingEntry row in tab.GetComponentsInChildren<uGUI_PlayerPingEntry>(false))
            {
                if (++index > 4)
                {
                    break;
                }

                RectTransform rect = row.rectTransform;
                UnityEngine.UI.LayoutElement layout = row.GetComponent<UnityEngine.UI.LayoutElement>();
                TMPro.TextMeshProUGUI label = row.label;
                Log.Info(string.Concat(
                    $"[SyncHarness] pdastatsui: row {index} player={PdaStatsText(row.PlayerName)} local={row.IsLocalPlayer} height={PdaNumber(rect.rect.height)} width={PdaNumber(rect.rect.width)} pivot=({PdaNumber(rect.pivot.x)},{PdaNumber(rect.pivot.y)}) ",
                    $"layout={(layout ? $"min={PdaNumber(layout.minHeight)}/pref={PdaNumber(layout.preferredHeight)}/ignore={layout.ignoreLayout}" : "none")} ",
                    $"label=(font={PdaNumber(label.fontSize)} auto={label.enableAutoSizing} overflow={label.overflowMode} wrap={label.enableWordWrapping} rich={label.richText} prefW={PdaNumber(label.preferredWidth)} prefH={PdaNumber(label.preferredHeight)} rectW={PdaNumber(label.rectTransform.rect.width)} rectH={PdaNumber(label.rectTransform.rect.height)}) ",
                    $"mute={(row.MuteObject && row.MuteObject.activeSelf)} kick={(row.KickObject && row.KickObject.activeSelf)} teleport={(row.TeleportToObject && row.TeleportToObject.activeSelf)} assigns={row.AssignCount}"));
            }

            pdaUiLines = 0;
            PdaUiDump(tab.transform, tab.name, 1);
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdastatsui: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
