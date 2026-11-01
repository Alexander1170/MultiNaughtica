using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NitroxClient.GameLogic;

/// <summary>
/// Which colliders are inside a trigger, for triggers that must stay active while any player is in them (the Precursor pedestals and terminals).
/// A bare counter of enters and exits never went back down when a remote player left the game inside the trigger (its colliders are destroyed without an exit),
/// which left the pedestal or terminal open for good. Colliders are kept instead, destroyed ones drop out, and <see cref="PlayerGone" /> closes what a leaving player held open.
/// </summary>
public static class TriggerOccupancy
{
    private class Entry
    {
        public Object Trigger;
        public readonly HashSet<Collider> Colliders = [];
        public Action OnEmpty;
    }

    private static readonly Dictionary<int, Entry> entriesByTriggerId = [];

    /// <param name="onEmpty">What to do when the last collider is gone without an exit (a player leaving the game)</param>
    /// <returns>How many colliders are inside the trigger, this one included</returns>
    public static int Enter(Object trigger, Collider collider, Action onEmpty)
    {
        int id = trigger.GetInstanceID();
        if (!entriesByTriggerId.TryGetValue(id, out Entry entry))
        {
            entry = new Entry { Trigger = trigger };
            entriesByTriggerId[id] = entry;
        }
        entry.OnEmpty = onEmpty;
        entry.Colliders.RemoveWhere(IsGone);
        entry.Colliders.Add(collider);
        return entry.Colliders.Count;
    }

    public static bool Contains(Object trigger, Collider collider)
    {
        return entriesByTriggerId.TryGetValue(trigger.GetInstanceID(), out Entry entry) && entry.Colliders.Contains(collider);
    }

    /// <returns>How many colliders are left inside the trigger</returns>
    public static int Exit(Object trigger, Collider collider)
    {
        if (!entriesByTriggerId.TryGetValue(trigger.GetInstanceID(), out Entry entry))
        {
            return 0;
        }
        entry.Colliders.Remove(collider);
        entry.Colliders.RemoveWhere(IsGone);
        return entry.Colliders.Count;
    }

    /// <summary>
    /// Called before a remote player's body is destroyed: it will not exit the triggers it is in, so remove it from them and close those that are now empty.
    /// </summary>
    public static void PlayerGone(GameObject body)
    {
        foreach (Entry entry in new List<Entry>(entriesByTriggerId.Values))
        {
            int removed = entry.Colliders.RemoveWhere(collider => IsGone(collider) || collider.transform.IsChildOf(body.transform));
            if (removed == 0 || entry.Colliders.Count > 0 || !entry.Trigger)
            {
                continue;
            }

            try
            {
                entry.OnEmpty?.Invoke();
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"[{nameof(TriggerOccupancy)}] Failed to close {entry.Trigger.name} after its last player left");
            }
        }
    }

    private static bool IsGone(Collider collider) => !collider;
}
