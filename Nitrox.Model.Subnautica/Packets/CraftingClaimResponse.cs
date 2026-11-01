using System;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

public enum CraftingClaimOutcome : byte
{
    /// <summary>Not an answer: the value of a field that was never filled in or a garbled packet. It is never taken for a grant.</summary>
    Unknown = 0,

    /// <summary>The server removed every item (and told everybody): the craft can start.</summary>
    Granted = 1,

    /// <summary>At least one item is not in a container of the sub root any more (another player took it, moved it or crafted with it). Nothing was removed.</summary>
    Taken = 2,

    /// <summary>The server has crafting from lockers switched off. Nothing was removed.</summary>
    Disabled = 3,

    /// <summary>The request makes no sense (no items, too many, an unknown scope, the same item twice). Nothing was removed.</summary>
    Invalid = 4,
}

/// <summary>
/// The answer to a <see cref="CraftingClaimRequest" />. A granted answer comes after the <see cref="EntityDestroyed" /> packets of the claimed items, so the sender's lockers already
/// reflect the claim when it arrives.
/// </summary>
[Serializable]
public class CraftingClaimResponse : Packet
{
    public int ClaimId { get; }
    public CraftingClaimOutcome Outcome { get; }

    /// <summary>For <see cref="CraftingClaimOutcome.Taken" />: the items that were not available, so that the sender can pick others.</summary>
    public List<NitroxId> UnavailableIds { get; }

    public CraftingClaimResponse(int claimId, CraftingClaimOutcome outcome, List<NitroxId> unavailableIds)
    {
        ClaimId = claimId;
        Outcome = outcome;
        UnavailableIds = unavailableIds;
    }
}
