using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: a player's crafting station needs items that are only in the lockers of its sub root. The client picks the exact items (the ones its own
/// copy of the lockers holds), the server checks that every one of them is still in a container of that sub root, removes them and tells everybody (<see cref="EntityDestroyed" />), and only
/// then answers with a <see cref="CraftingClaimResponse" />: the craft starts after a granted answer. Two players who want the same last items cannot both get them.
/// </summary>
[Serializable]
public class CraftingClaimRequest : Packet
{
    /// <summary>Most items one claim may hold. A recipe is a handful of ingredients; the cap only keeps a broken or hostile client from emptying a base with one packet.</summary>
    public const int MAX_ITEMS = 64;

    /// <summary>The number the sender gave this claim; the answer carries it back.</summary>
    public int ClaimId { get; }

    /// <summary>The base (the id of its <c>Base</c>) or the Cyclops (the id of its vehicle) whose lockers the items are taken from.</summary>
    public NitroxId ScopeId { get; }

    public List<NitroxId> ItemIds { get; }

    public CraftingClaimRequest(int claimId, NitroxId scopeId, List<NitroxId> itemIds)
    {
        ClaimId = claimId;
        ScopeId = scopeId;
        ItemIds = itemIds;
    }
}
