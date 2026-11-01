#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "pdabadges" (a harness script that is not part of this repository, bug 2): the yellow "new" badges of the PDA databank take several clicks to clear for the second player. Debug builds only.
// Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> [arg] [arg]".
//
//   pdabadges [max]          what the NotificationManager of this client holds: one "summary" line (the Encyclopedia notifications: total, distinct entries, case duplicates, orphans, registered targets,
//                            how many of them are the selected entry; the entries of this client; whether the databank is open, which entry is displayed and the count its list keeps at its root, which the
//                            category counters add up to; the count of every group), then one "item" line per
//                            Encyclopedia notification (key, timeLeft of its 3 s, whether a list entry is registered for it and whether that entry is the selected one, whether the entry is known here), at most max (60)
//   pdaadd [next|<key>] [n]  unlocks encyclopedia entries on this client through PDAEncyclopedia.Add(key, true), the call a scan makes (the patch sends it to the server, the other player gets it
//                            as a live unlock): "next" takes the first n (1) entries of the game's list that this client does not have yet, in key order; a key unlocks that entry
//   pdaclick <key>[,<key>...] [stay]
//                            opens the PDA on the databank tab, shows the entry and activates it the way a click on its row does (uGUI_EncyclopediaTab.ExpandTo, then Activate: the call under
//                            OnButtonDown); with several keys it clicks them 0.3 s apart (a click-through); half a second after the last it logs, per key, whether the badge was there before and after;
//                            closes the PDA again unless "stay" is given (then the next pdaclick finds it open)
//
// Every command logs "[SyncHarness] <command>: ..." lines with key=value pairs, or "failed reason=<text>". Nothing throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private bool pdaClickRunning;

    private static string PdaNumber(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private void LiveCommand_pdabadges(NotificationCenter.Notification n)
    {
        try
        {
            int max = Mathf.Max(0, Mathf.RoundToInt(LiveArgFloat(n, 0, 60f)));
            NotificationManager manager = NotificationManager.main;
            if (!manager)
            {
                Log.Info("[SyncHarness] pdabadges: failed reason=no NotificationManager");
                return;
            }

            Dictionary<NotificationManager.Group, int> perGroup = new();
            List<string> keys = new();
            List<string> items = new();
            int targets = 0;
            int selected = 0;
            foreach (KeyValuePair<NotificationManager.NotificationId, NotificationManager.NotificationData> pair in manager.notifications)
            {
                perGroup.TryGetValue(pair.Key.group, out int inGroup);
                perGroup[pair.Key.group] = inGroup + 1;
                if (pair.Key.group != NotificationManager.Group.Encyclopedia)
                {
                    continue;
                }

                string key = pair.Key.key;
                keys.Add(key);
                bool hasTarget = manager.targets.TryGetValue(pair.Key, out INotificationTarget target) && target != null && !target.IsDestroyed();
                bool visible = hasTarget && target.IsVisible();
                if (hasTarget)
                {
                    targets++;
                }
                if (visible)
                {
                    selected++;
                }

                items.Add($"[SyncHarness] pdabadges: item key={key} timeLeft={PdaNumber(pair.Value.timeLeft)} duration={PdaNumber(pair.Value.duration)} target={(hasTarget ? "registered" : "none")} selected={visible} entry={(PDAEncyclopedia.entries != null && PDAEncyclopedia.ContainsEntry(key) ? "known" : "unknown")}");
            }

            PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit(keys, key => PDAEncyclopedia.entries != null && PDAEncyclopedia.ContainsEntry(key));

            PDA pda = Player.main ? Player.main.GetPDA() : null;
            uGUI_PDA ui = uGUI_PDA.main;
            bool tabOpen = pda && pda.isOpen && ui && ui.currentTabType == PDATab.Encyclopedia;
            uGUI_EncyclopediaTab tab = ui ? ui.tabEncyclopedia as uGUI_EncyclopediaTab : null;
            string active = tab && tab.activeEntry ? tab.activeEntry.Key : "none";
            // The count the databank keeps at the root of its list (what the category counters add up to): equal to "encyclopedia" while the databank is open, 0 or stale before its list was built
            int treeCount = tab && tab.tree != null ? uGUI_EncyclopediaTab.GetNodeNotificationsCount(tab.tree) : -1;

            StringBuilder groups = new();
            foreach (KeyValuePair<NotificationManager.Group, int> pair in perGroup)
            {
                groups.Append(groups.Length > 0 ? "," : string.Empty).Append(pair.Key.ToString()).Append(':').Append(pair.Value);
            }

            Log.Info($"[SyncHarness] pdabadges: summary encyclopedia={audit.Total} distinct={audit.Distinct} caseDuplicates={audit.CaseDuplicates} orphans={audit.Orphans} targets={targets} selected={selected} entries={(PDAEncyclopedia.entries != null ? PDAEncyclopedia.entries.Count : -1)} tabOpen={tabOpen} active={active} treeCount={treeCount} groups={(groups.Length > 0 ? groups.ToString() : "none")}");
            items.Sort(StringComparer.Ordinal);
            for (int i = 0; i < items.Count && i < max; i++)
            {
                Log.Info(items[i]);
            }
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdabadges: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_pdaadd(NotificationCenter.Notification n)
    {
        try
        {
            string what = LiveArgString(n, 0, "next");
            int count = Mathf.Max(1, Mathf.RoundToInt(LiveArgFloat(n, 1, 1f)));
            List<string> keys = new();
            if (what == "next")
            {
                if (PDAEncyclopedia.mapping != null)
                {
                    foreach (KeyValuePair<string, PDAEncyclopedia.EntryData> pair in PDAEncyclopedia.mapping)
                    {
                        if (pair.Value != null && pair.Value.kind == PDAEncyclopedia.EntryData.Kind.Encyclopedia && !PDAEncyclopedia.ContainsEntry(pair.Key))
                        {
                            keys.Add(pair.Key);
                        }
                    }
                }

                keys.Sort(StringComparer.Ordinal);
                if (keys.Count > count)
                {
                    keys.RemoveRange(count, keys.Count - count);
                }
            }
            else
            {
                keys.Add(what);
            }

            if (keys.Count == 0)
            {
                Log.Info("[SyncHarness] pdaadd: failed reason=no entry left to unlock");
                return;
            }

            foreach (string key in keys)
            {
                PDAEncyclopedia.EntryData data = PDAEncyclopedia.Add(key, true);
                bool badge = NotificationManager.main && NotificationManager.main.Contains(NotificationManager.Group.Encyclopedia, key);
                Log.Info($"[SyncHarness] pdaadd: key={key} added={data != null} badge={badge}");
            }
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdaadd: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_pdaclick(NotificationCenter.Notification n)
    {
        string[] keys = LiveArgString(n, 0, string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0)
        {
            Log.Info("[SyncHarness] pdaclick: failed reason=no key (pdaclick <key>[,<key>...] [stay])");
            return;
        }

        if (pdaClickRunning)
        {
            Log.Info("[SyncHarness] pdaclick: failed reason=the previous click is still running");
            return;
        }

        StartCoroutine(PdaClickRoutine(keys, LiveArgString(n, 1, string.Empty) == "stay"));
    }

    private IEnumerator PdaClickRoutine(string[] keys, bool stayOpen)
    {
        pdaClickRunning = true;
        try
        {
            PDA pda = null;
            string failure = null;
            try
            {
                pda = Player.main ? Player.main.GetPDA() : null;
                if (!pda)
                {
                    failure = "no PDA (the client is not in the world)";
                }
                else if (!pda.isInUse && !pda.Open(PDATab.Encyclopedia, null, null))
                {
                    failure = "the PDA refused to open";
                }
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }

            uGUI_EncyclopediaTab tab = null;
            if (failure == null)
            {
                float deadline = Time.unscaledTime + 8f;
                while (Time.unscaledTime < deadline)
                {
                    uGUI_PDA ui = uGUI_PDA.main;
                    uGUI_EncyclopediaTab candidate = ui ? ui.tabEncyclopedia as uGUI_EncyclopediaTab : null;
                    if (candidate && pda.isOpen && ui.currentTabType == PDATab.Encyclopedia && candidate.tree != null)
                    {
                        tab = candidate;
                        break;
                    }

                    yield return null;
                }

                if (!tab)
                {
                    failure = "the databank tab did not open within 8 s";
                }
            }

            // One activation per key, 0.3 s apart (so that each entry is the displayed one for less than the 1 s that vanilla needs to count a look), then a pause for the LateUpdate of the game
            List<string> clicks = new();
            for (int i = 0; failure == null && i < keys.Length; i++)
            {
                try
                {
                    bool before = NotificationManager.main.Contains(NotificationManager.Group.Encyclopedia, keys[i]);
                    CraftNode node = tab.ExpandTo(keys[i]);
                    bool activated = node != null && tab.Activate(node);
                    clicks.Add($"key={keys[i]} node={node != null} activated={activated} badgeBefore={before}");
                }
                catch (Exception exception)
                {
                    failure = $"{exception.GetType().Name}: {exception.Message}";
                }

                if (failure == null && i < keys.Length - 1)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (failure == null)
            {
                yield return new WaitForSeconds(0.5f);
            }

            try
            {
                if (failure != null)
                {
                    Log.Info($"[SyncHarness] pdaclick: failed reason={failure}");
                }
                else
                {
                    for (int i = 0; i < clicks.Count; i++)
                    {
                        bool after = NotificationManager.main.Contains(NotificationManager.Group.Encyclopedia, keys[i]);
                        Log.Info($"[SyncHarness] pdaclick: {clicks[i]} badgeAfter={after}");
                    }
                }

                if (!stayOpen && pda && pda.isInUse)
                {
                    pda.Close();
                }
            }
            catch (Exception exception)
            {
                Log.Info($"[SyncHarness] pdaclick: failed reason={exception.GetType().Name}: {exception.Message}");
            }
        }
        finally
        {
            pdaClickRunning = false;
        }
    }
}
#endif
