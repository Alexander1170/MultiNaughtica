using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic;

/// <summary>
/// The yellow "new" badges of the PDA databank (vanilla <c>NotificationManager</c>, group Encyclopedia) and the few decisions around them. Plain data, no game types
/// (the design notes (not part of this repository) row 7, bug 2 of the first play session: "one entry's badge takes several clicks to clear" for the second player).
/// <para>
/// What the vanilla IL says, and what that means for the report:
/// </para>
/// <list type="bullet">
/// <item><c>NotificationManager.notifications</c> is a <c>Dictionary&lt;NotificationId, NotificationData&gt;</c>: one entry per (group, key). A second <c>Add</c> of the same key
/// does not stack, it only sets the timer back to the full duration. Badges cannot pile up, whatever number of times an entry add reaches a client.</item>
/// <item><c>PDAEncyclopedia.Add(key, entry, verbose)</c> returns at its first instruction for a key that is known (<c>ContainsEntry</c>): a repeated add never reaches
/// <c>NotificationManager.Add</c> either. Only an entry that is new on this client gets a badge, and only when <c>verbose</c> is set (a scan, a story goal; the restore at join is quiet).</item>
/// <item>Nothing in the databank removes a badge on a click: no method of <c>uGUI_EncyclopediaTab</c> calls <c>NotificationManager.Remove</c>. <c>NotificationManager.LateUpdate</c> does it,
/// from the time the entry has been the <b>selected</b> one (<c>uGUI_ListEntry.IsVisible</c> is "active and selected"): the badge fades over 3 s of viewing and goes then; a deselected entry
/// loses it at once only if it was viewed for more than 1 s, otherwise its timer is set back to the full 3 s. Whoever clicks through a list of new entries at a click per second or faster
/// finds every badge still there, and has to come back and stay longer on each: "several clicks". The second player has the most of them (every scan of the other player is a badge for her).</item>
/// </list>
/// So a click is made to clear the badge of the entry it opens (<see cref="ShouldClearOnActivate" />, applied by <c>uGUI_EncyclopediaTab_Activate_Patch</c>), and an entry add that is
/// not new on this client is not applied at all (<see cref="DecideRemoteAdd" />), which the game would refuse by itself, so that the receiver is idempotent by its own rule and not only by the
/// luck of a vanilla guard.
/// </summary>
public static class PdaBadgeRules
{
    /// <summary>
    /// What the client does with an encyclopedia entry add that comes from another player (a live unlock or the restore at join).
    /// </summary>
    public enum RemoteAddAction
    {
        /// <summary>The entry is known here already: nothing to add, nothing to notify.</summary>
        Ignore,
        /// <summary>A new entry that is not announced (the restore at join, the entries the other player unlocked before this one came).</summary>
        AddQuietly,
        /// <summary>A new entry that is announced: one popup and one badge, like a scan of this player's own.</summary>
        AddWithBadge
    }

    /// <summary>
    /// The receiver of an entry add: a key that is known is never added again (no second badge, no second popup, no timer set back under a player who is just reading the entry), a new
    /// one is added with the badge the sender asked for. Which entries are unlocked does not depend on this: a known key stays known.
    /// </summary>
    /// <param name="entryKnown"><c>PDAEncyclopedia.ContainsEntry(key)</c> on this client.</param>
    /// <param name="verbose">The flag that the unlocking player's <c>PDAEncyclopedia.Add</c> had (the packet carries it).</param>
    public static RemoteAddAction DecideRemoteAdd(bool entryKnown, bool verbose)
    {
        if (entryKnown)
        {
            return RemoteAddAction.Ignore;
        }

        return verbose ? RemoteAddAction.AddWithBadge : RemoteAddAction.AddQuietly;
    }

    /// <summary>
    /// Whether opening a list entry of the databank takes its badge away at once. <c>uGUI_EncyclopediaTab.Activate</c> returns true only for an entry that became the displayed one
    /// (a category, a null node and the click on the entry that is displayed already return false: nothing to clear, the first click did it).
    /// </summary>
    /// <param name="activated">The result of <c>Activate</c>.</param>
    /// <param name="key">The id of the node, which is the key of the entry and of its notification.</param>
    public static bool ShouldClearOnActivate(bool activated, string key)
    {
        return activated && !string.IsNullOrEmpty(key);
    }

    /// <summary>
    /// Whether the databank shows its "Clear all" button: only when there is a badge to clear (the button is gone, not greyed out, when there is none) and only for a player with the mouse (the PDA
    /// turns its pointer raycaster off for a primary gamepad (not with the VR gaze cursor), so a button that is shown could not be pressed; the vanilla "clear pins" button of the journal hides itself the same way).
    /// </summary>
    /// <param name="badgeCount"><c>NotificationManager.GetCount(Group.Encyclopedia)</c>, the number the toolbar of the PDA shows on the databank tab (orphans included).</param>
    /// <param name="primaryDeviceIsGamepad"><c>GameInput.IsPrimaryDeviceGamepad()</c>.</param>
    public static bool ShouldShowClearAll(int badgeCount, bool primaryDeviceIsGamepad)
    {
        return badgeCount > 0 && !primaryDeviceIsGamepad;
    }

    /// <summary>
    /// Clears every badge of one group. The keys are read to the end BEFORE the first one is removed (removing changes the dictionary that the keys come from, and an enumerator over it is
    /// invalid after that), each key once (the notification ids compare keys by ordinal, so "Jellyray" and "jellyray" are two badges and both go), and nothing of another group is touched.
    /// A badge that came in while the removals ran is not in the snapshot and stays.
    /// </summary>
    /// <typeparam name="TGroup">The group type of the notifications (<c>NotificationManager.Group</c>); a type parameter keeps this free of game types so that it can be tested.</typeparam>
    /// <param name="notifications">Every notification of the manager, lazily is fine: it is drained first.</param>
    /// <param name="group">The group to clear.</param>
    /// <param name="removeBadge">Removes one badge and says whether it was still there (the vanilla <c>Contains</c> then <c>Remove</c>); a badge that is already gone is not counted.</param>
    /// <returns>The number of badges that were removed.</returns>
    public static int ClearAll<TGroup>(IEnumerable<(TGroup Group, string Key)> notifications, TGroup group, Func<string, bool> removeBadge)
        where TGroup : struct
    {
        if (notifications == null || removeBadge == null)
        {
            return 0;
        }

        List<string> keys = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((TGroup notificationGroup, string key) in notifications)
        {
            if (EqualityComparer<TGroup>.Default.Equals(notificationGroup, group) && !string.IsNullOrEmpty(key) && seen.Add(key))
            {
                keys.Add(key);
            }
        }

        int cleared = 0;
        foreach (string key in keys)
        {
            if (removeBadge(key))
            {
                cleared++;
            }
        }

        return cleared;
    }

    /// <summary>
    /// Where the "Clear all" button goes on the header row of the databank (one dimension, in the units of the canvas): right behind the end of the header text, as wide as its own text needs but
    /// not past the right edge of the list. When even <paramref name="minWidth" /> does not fit before <paramref name="limitRight" /> the button keeps that width and <see cref="ButtonPlacement.Fits" />
    /// is false (the caller says so once in the log; the button is never moved onto the header text).
    /// </summary>
    /// <param name="textRight">Where the header text ends.</param>
    /// <param name="gap">Space between the header text and the button.</param>
    /// <param name="wantedWidth">The width that the button's own label needs, with its padding.</param>
    /// <param name="limitRight">The right edge that the button must not pass (the right edge of the list).</param>
    /// <param name="minWidth">The least width that is still a button.</param>
    public static ButtonPlacement PlaceClearAllButton(float textRight, float gap, float wantedWidth, float limitRight, float minWidth)
    {
        float left = textRight + gap;
        float width = Math.Max(wantedWidth, minWidth);
        if (left + width <= limitRight)
        {
            return new ButtonPlacement(left, width, true);
        }

        float room = limitRight - left;
        if (room >= minWidth)
        {
            return new ButtonPlacement(left, room, true);
        }

        return new ButtonPlacement(left, minWidth, false);
    }

    /// <summary>
    /// Whether the header text starts far enough to the right of the left end of the list for the button to sit above the list, left of the header text. The first live run of the databank (batch
    /// 20261027-003115) showed that the header ("Databank") is centred on the tab and the list is a column on the left of it (its right edge 138 units left of the middle while the text ended 59
    /// right of it), so "right behind the header text, not past the list" had no room at all.
    /// </summary>
    public static bool ShouldPlaceOverList(float listLeft, float listRight, float textLeft, float gap, float minWidth)
    {
        return IsFinite(listLeft) && IsFinite(listRight) && IsFinite(textLeft) && listRight > listLeft && textLeft - gap - listLeft >= minWidth;
    }

    /// <summary>
    /// Where the button goes when the header text is to the right of the list's left end: on the header row, at the left end of the list, as wide as its label needs but not into the list's right
    /// edge and not closer than <paramref name="gap" /> to the header text. When even <paramref name="minWidth" /> does not fit, the button keeps that width and <see cref="ButtonPlacement.Fits" /> is false.
    /// </summary>
    public static ButtonPlacement PlaceClearAllButtonOverList(float listLeft, float listRight, float textLeft, float gap, float wantedWidth, float minWidth)
    {
        float right = Math.Min(listRight, textLeft - gap);
        float room = right - listLeft;
        if (room >= minWidth)
        {
            return new ButtonPlacement(listLeft, Math.Min(Math.Max(wantedWidth, minWidth), room), true);
        }

        return new ButtonPlacement(listLeft, minWidth, false);
    }

    /// <summary>
    /// How tall the button may be: it is centred on the header line, so its lower half reaches down to the top of the list at most (it would cover the first row and take its clicks). When the
    /// header line is not above the top of the list there is nothing to protect and the wanted height is kept; the result is never below <paramref name="minHeight" />.
    /// </summary>
    /// <param name="wantedHeight">The height that the databank's own rows have.</param>
    /// <param name="centerY">Where the middle of the header line is.</param>
    /// <param name="listTopY">Where the list starts (its top edge), in the same space.</param>
    /// <param name="minHeight">The least height that is still a button.</param>
    public static float ClampButtonHeight(float wantedHeight, float centerY, float listTopY, float minHeight)
    {
        float room = 2f * (centerY - listTopY);
        if (room <= 0f)
        {
            return wantedHeight;
        }

        return Math.Max(minHeight, Math.Min(wantedHeight, room));
    }

    /// <summary>
    /// Whether a measure that comes out of the UI (a text bound, a corner of a rectangle) is a number: a button is not placed with NaN or infinity (<c>float.IsFinite</c> does not exist on the
    /// game's net472).
    /// </summary>
    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct ButtonPlacement
    {
        public ButtonPlacement(float left, float width, bool fits)
        {
            Left = left;
            Width = width;
            Fits = fits;
        }

        public float Left { get; }

        public float Width { get; }

        /// <summary>False when the button does not fit between the header text and the limit: it is placed behind the text all the same and overhangs the limit.</summary>
        public bool Fits { get; }
    }

    /// <summary>
    /// What the notifications of the databank look like, for the Debug probe <c>pdabadges</c> (vanilla cannot make the first two numbers differ, they are what the check of the
    /// stacking theory reads).
    /// </summary>
    public readonly struct BadgeAudit
    {
        public BadgeAudit(int total, int distinct, int orphans)
        {
            Total = total;
            Distinct = distinct;
            Orphans = orphans;
        }

        /// <summary>Notifications of the group.</summary>
        public int Total { get; }

        /// <summary>Notifications that name a different entry (keys compared without regard to case, like <c>PDAEncyclopedia.entries</c> does).</summary>
        public int Distinct { get; }

        /// <summary>Notifications whose key is not an entry of this client: no node, no target, nothing can ever clear them by looking.</summary>
        public int Orphans { get; }

        /// <summary>Notifications that repeat another one under a key that differs only in case: the same entry shown twice in the counters.</summary>
        public int CaseDuplicates => Total - Distinct;

        public bool IsClean => CaseDuplicates == 0 && Orphans == 0;
    }

    /// <param name="badgeKeys">The keys of the notifications of the Encyclopedia group.</param>
    /// <param name="isKnownEntry">Whether this client has an entry under the key (<c>PDAEncyclopedia.ContainsEntry</c>).</param>
    public static BadgeAudit Audit(IEnumerable<string> badgeKeys, Func<string, bool> isKnownEntry)
    {
        HashSet<string> distinct = new(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        int orphans = 0;
        foreach (string key in badgeKeys)
        {
            total++;
            distinct.Add(key ?? string.Empty);
            if (string.IsNullOrEmpty(key) || !isKnownEntry(key))
            {
                orphans++;
            }
        }

        return new BadgeAudit(total, distinct.Count, orphans);
    }
}
