using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic;

/// <summary>
/// The modules this client put into a slot of a vehicle, upgrade console, charger, ... a few seconds ago. When the server refuses an install (another player was first, V0 U4) the
/// module of the loser must leave the slot again, and the winner's module arrives as a spawn that would blindly overwrite the slot: only a module that this client installed a
/// moment ago is taken out first (<see cref="EquipmentSlots.ReleaseContestedOccupant" />); what the prefab, the join snapshot or a forced respawn put into a slot is left as it is.
/// Holds no Unity type, the clock is injected (<c>() =&gt; Time.unscaledTime</c> in the game), so that it can be unit tested.
/// </summary>
public sealed class RecentModuleInstalls
{
    /// <summary>
    /// Longer than the 3 s window of the server (<c>ModuleSlotArbiter.CONTESTED_WINDOW_MS</c>) plus a round trip and the frame that the winner's spawn waits.
    /// </summary>
    public const float WINDOW_SECONDS = 10f;

    private const int TRIM_ABOVE = 64;

    private readonly Func<float> clock;
    private readonly Dictionary<NitroxId, float> installedAt = new();

    public RecentModuleInstalls(Func<float> clock)
    {
        this.clock = clock;
    }

    /// <summary>
    /// The number of modules that are remembered (including expired ones that were not trimmed yet).
    /// </summary>
    public int Count => installedAt.Count;

    /// <summary>
    /// A module was just installed by this client. Installing it again refreshes the time.
    /// </summary>
    public void Add(NitroxId itemId)
    {
        if (itemId == null)
        {
            return;
        }

        float now = clock();
        installedAt[itemId] = now;
        if (installedAt.Count > TRIM_ABOVE)
        {
            Trim(now);
        }
    }

    /// <summary>
    /// True if this client installed the module less than <see cref="WINDOW_SECONDS" /> ago and has not taken it out since.
    /// </summary>
    public bool IsRecent(NitroxId itemId)
    {
        return itemId != null && installedAt.TryGetValue(itemId, out float at) && clock() - at < WINDOW_SECONDS;
    }

    /// <summary>
    /// The module left its slot (unequipped, destroyed, taken back).
    /// </summary>
    public void Forget(NitroxId itemId)
    {
        if (itemId != null)
        {
            installedAt.Remove(itemId);
        }
    }

    private void Trim(float now)
    {
        List<NitroxId> expired = new();
        foreach (KeyValuePair<NitroxId, float> pair in installedAt)
        {
            if (now - pair.Value >= WINDOW_SECONDS)
            {
                expired.Add(pair.Key);
            }
        }

        foreach (NitroxId id in expired)
        {
            installedAt.Remove(id);
        }
    }
}

/// <summary>
/// What the client has to do with a module that the server refused (<see cref="Nitrox.Model.Subnautica.Packets.ModuleSlotRejected" />).
/// </summary>
public enum ModuleTakeBack
{
    /// <summary>
    /// The module is gone, is somewhere else or is in another slot (its install is arbitrated on its own): leave everything alone.
    /// </summary>
    NothingToDo,

    /// <summary>
    /// The module is in the rejected slot: take it out with the vanilla unequip and put it into the inventory.
    /// </summary>
    TakeOutOfSlot,

    /// <summary>
    /// The module belongs to the equipment but no slot holds it (the winner's module overwrote its entry): uncount it, tell the owner and put it into the inventory.
    /// </summary>
    ClearOrphan
}

/// <summary>
/// Plain data version of the look at a module that the server refused, so that it can be tested without the game.
/// </summary>
public static class ModuleSlotRejectionRules
{
    /// <param name="itemIsInAnEquipmentOfTheParent">The module's inventory item is held by the <c>Equipment</c> of the owner named in the packet.</param>
    /// <param name="slotOfItemInDictionary">The slot whose dictionary entry is this module, null if no slot holds it.</param>
    /// <param name="rejectedSlot">The slot named in the packet.</param>
    public static ModuleTakeBack Classify(bool itemIsInAnEquipmentOfTheParent, string slotOfItemInDictionary, string rejectedSlot)
    {
        if (!itemIsInAnEquipmentOfTheParent)
        {
            return ModuleTakeBack.NothingToDo;
        }

        if (slotOfItemInDictionary == null)
        {
            return ModuleTakeBack.ClearOrphan;
        }

        // Moved to another slot since: that install is its own request to the server, a stale reject must not undo it
        return string.Equals(slotOfItemInDictionary, rejectedSlot, StringComparison.Ordinal) ? ModuleTakeBack.TakeOutOfSlot : ModuleTakeBack.NothingToDo;
    }
}
