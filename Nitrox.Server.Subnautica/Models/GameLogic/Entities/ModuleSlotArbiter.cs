using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Decides who gets a slot of a vehicle, upgrade console, charger, ... when two players put a module into it at the same moment (V0, U4). Each client only checks that the
///     slot is empty on its own copy (vanilla <c>Equipment.AddItem</c>), so both installs reach the server, which used to accept and forward both: every client then overwrote the
///     slot with the other player's module and the slot held a different module on each side.
/// </summary>
/// <remarks>
///     <para>
///         First wins per (owner, slot). The first accepted install leaves a claim; a different module of a different player for the same slot within
///         <see cref="CONTESTED_WINDOW_MS" /> loses against it. The claim is written by an accepted install only (a rejection never renews the window) and it is not
///         removed when the winner's module is destroyed again: it stays for the rest of the window, because the loser's client has already taken its module out of the slot
///         (or is about to) and the server has to agree with what that client did.
///     </para>
///     <para>
///         Everything else is not a race. A single player can never be rejected, the same module again never is, and a claim older than the window decides nothing: the
///         newcomer's local truth wins (his <c>AddItem</c> succeeded, so his slot was empty), and whatever module the registry still holds for that slot is stale
///         (<see cref="ModuleSlotDecision.StaleOccupantId" />). A slot therefore can never be blocked for good, a lost unequip or a lost reply delay another player by at most the window.
///     </para>
///     <para>
///         The claims are in memory only (a restart clears them) and are trimmed once there are many. The clock is injected so that the window can be tested.
///     </para>
/// </remarks>
public class ModuleSlotArbiter
{
    /// <summary>
    ///     How long after an accepted install another player's different module for the same slot counts as the loser of a race. It has to cover the round trip of the loser to
    ///     the server (his install was made before he heard about the winner's), the same value as the contested window of picking up an item.
    /// </summary>
    public const long CONTESTED_WINDOW_MS = 3000;

    private const int TRIM_ABOVE = 256;

    private readonly Func<long> clock;
    private readonly Dictionary<(NitroxId Parent, string Slot), Claim> claims = [];
    private readonly Lock gate = new();

    public ModuleSlotArbiter() : this(() => Environment.TickCount64)
    {
    }

    internal ModuleSlotArbiter(Func<long> clock)
    {
        this.clock = clock;
    }

    /// <summary>
    ///     The number of slots that hold a claim.
    /// </summary>
    internal int ClaimCount
    {
        get
        {
            lock (gate)
            {
                return claims.Count;
            }
        }
    }

    /// <summary>
    ///     Decides about an install that <paramref name="sender" /> reports. An accepted install becomes the claim of its slot, a rejected one changes nothing.
    /// </summary>
    /// <param name="entityRegistry">Only read, to find the module that the accepted install makes stale.</param>
    public ModuleSlotDecision Decide(SessionId sender, InstalledModuleEntity module, EntityRegistry entityRegistry)
    {
        if (module.ParentId == null || module.Slot == null)
        {
            // Not a module of a slot: nothing to arbitrate
            return new ModuleSlotDecision(false, null, null);
        }

        (NitroxId, string) key = (module.ParentId, module.Slot);
        lock (gate)
        {
            long now = clock();
            bool hasClaim = claims.TryGetValue(key, out Claim claim);

            if (hasClaim && claim.ItemId != module.Id && claim.Sender != sender && now - claim.Tick < CONTESTED_WINDOW_MS)
            {
                return new ModuleSlotDecision(true, claim.ItemId, null);
            }

            NitroxId? staleOccupantId = FindStaleOccupant(hasClaim ? claim.ItemId : null, module, entityRegistry);
            claims[key] = new Claim(module.Id, sender, now);
            if (claims.Count > TRIM_ABOVE)
            {
                Trim(now);
            }
            return new ModuleSlotDecision(false, null, staleOccupantId);
        }
    }

    /// <summary>
    ///     The module that the registry still holds in the slot of <paramref name="module" /> and that the install of <paramref name="module" /> replaces: the module of the
    ///     claim first (it is found even when its parent is not in the registry), else the first other module among the children of the owner with the same slot (modules
    ///     loaded from a save, claims that were trimmed).
    /// </summary>
    private static NitroxId? FindStaleOccupant(NitroxId? claimedItemId, InstalledModuleEntity module, EntityRegistry entityRegistry)
    {
        if (claimedItemId != null &&
            entityRegistry.TryGetEntityById(claimedItemId, out InstalledModuleEntity? claimed) &&
            claimed.Id != module.Id &&
            claimed.ParentId == module.ParentId &&
            claimed.Slot == module.Slot)
        {
            return claimed.Id;
        }

        if (entityRegistry.TryGetEntityById(module.ParentId, out Entity? parent))
        {
            // The children are a plain list that other packets change while this runs: work on a copy
            foreach (Entity child in parent.ChildEntities.ToArray())
            {
                if (child is InstalledModuleEntity other && other.Id != module.Id && other.Slot == module.Slot)
                {
                    return other.Id;
                }
            }
        }
        return null;
    }

    private void Trim(long now)
    {
        List<(NitroxId Parent, string Slot)> expired = [];
        foreach (KeyValuePair<(NitroxId Parent, string Slot), Claim> pair in claims)
        {
            // An older claim decides nothing any more: the registry tells what is stale
            if (now - pair.Value.Tick >= CONTESTED_WINDOW_MS)
            {
                expired.Add(pair.Key);
            }
        }
        foreach ((NitroxId Parent, string Slot) key in expired)
        {
            claims.Remove(key);
        }
    }

    private readonly record struct Claim(NitroxId ItemId, SessionId Sender, long Tick);
}

/// <summary>
///     The answer of <see cref="ModuleSlotArbiter.Decide" />.
/// </summary>
/// <param name="Rejected">Another player was first: the install is neither registered nor forwarded, the sender is told to take the module back.</param>
/// <param name="WinnerId">With <paramref name="Rejected" />: the module that holds the slot.</param>
/// <param name="StaleOccupantId">Without <paramref name="Rejected" />: the module that the registry still holds in the slot and that the accepted install replaces (null if none).</param>
public readonly record struct ModuleSlotDecision(bool Rejected, NitroxId? WinnerId, NitroxId? StaleOccupantId);
