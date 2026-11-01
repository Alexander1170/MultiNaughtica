using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic;

/// <summary>
/// The calls into the vanilla <c>NotificationManager</c> that the "new" badges of the PDA databank need (the decisions are in <see cref="PdaBadgeRules" />). Local only: a badge is the
/// personal state of one player, nothing here is sent.
/// </summary>
public static class PdaBadges
{
    /// <summary>
    /// Takes the badge of an encyclopedia entry away now (the entry was just opened), with the vanilla <c>NotificationManager.Remove</c>: the category counters follow through the listener
    /// of the databank tab, exactly as when the badge ends by itself after 3 s of viewing (<c>NotificationManager.LateUpdate</c>).
    /// </summary>
    /// <returns>Whether the entry had a badge.</returns>
    public static bool ClearEncyclopediaBadge(string key)
    {
        if (!RemoveEncyclopediaBadge(key))
        {
            return false;
        }

        Log.Info($"[PDA] the encyclopedia entry {key} was opened: its new badge is cleared now (vanilla fades it only after 3 s of viewing)");
        return true;
    }

    /// <summary>
    /// The number the PDA shows on the databank tab: every notification of the Encyclopedia group, those of entries this client does not have included (vanilla <c>GetCount</c>, which the
    /// toolbar polls every frame as well).
    /// </summary>
    public static int CountEncyclopediaBadges()
    {
        NotificationManager manager = NotificationManager.main;
        return manager ? manager.GetCount(NotificationManager.Group.Encyclopedia) : 0;
    }

    /// <summary>
    /// The "Clear all" button of the databank: takes every badge of the Encyclopedia group of this player away, one vanilla <c>NotificationManager.Remove</c> each, so the counters of the
    /// categories, the "+" on the rows and the number on the PDA's tab follow exactly as for a badge that ends by itself. The keys come from the manager's own dictionary (the one source that
    /// also holds badges without a row: a collapsed category, an entry this client does not have) and are copied before the first removal (<see cref="PdaBadgeRules.ClearAll{TGroup}" />).
    /// Local only: the badge is this player's own state, the other player's stay.
    /// </summary>
    /// <returns>The number of badges that were cleared.</returns>
    public static int ClearAllEncyclopediaBadges()
    {
        NotificationManager manager = NotificationManager.main;
        if (!manager)
        {
            return 0;
        }

        int orphans = 0;
        int failures = 0;
        int cleared = PdaBadgeRules.ClearAll(Notifications(manager), NotificationManager.Group.Encyclopedia, key =>
        {
            try
            {
                if (!RemoveEncyclopediaBadge(key))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                // a listener that throws for one badge must not keep the others from being cleared
                if (failures++ == 0)
                {
                    Log.Error(exception, $"[PDA] Clear all could not clear the badge of {key}; the others are cleared all the same");
                }

                return false;
            }

            if (PDAEncyclopedia.entries == null || !PDAEncyclopedia.ContainsEntry(key))
            {
                orphans++;
            }

            return true;
        });

        if (cleared > 0)
        {
            int left = manager.GetCount(NotificationManager.Group.Encyclopedia);
            Log.Info($"[PDA] the databank's Clear all was pressed: {cleared} new badge(s) cleared now, {orphans} of them for entries this client does not have, {left} left (local only, nothing is sent)");
        }

        return cleared;
    }

    private static bool RemoveEncyclopediaBadge(string key)
    {
        NotificationManager manager = NotificationManager.main;
        if (!manager || !manager.Contains(NotificationManager.Group.Encyclopedia, key))
        {
            return false;
        }

        manager.Remove(NotificationManager.Group.Encyclopedia, key);
        return true;
    }

    /// <summary>
    /// Every notification of the manager as (group, key), read lazily from its dictionary: <see cref="PdaBadgeRules.ClearAll{TGroup}" /> drains it before it removes anything.
    /// </summary>
    private static IEnumerable<(NotificationManager.Group Group, string Key)> Notifications(NotificationManager manager)
    {
        foreach (KeyValuePair<NotificationManager.NotificationId, NotificationManager.NotificationData> pair in manager.notifications)
        {
            yield return (pair.Key.group, pair.Key.key);
        }
    }
}
