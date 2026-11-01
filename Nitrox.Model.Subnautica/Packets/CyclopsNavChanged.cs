using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsNavChanged" /> says happened on a Cyclops' navigator (feature group 3, station 1).</summary>
public enum CyclopsNavChange : byte
{
    /// <summary>A mark was placed at <see cref="CyclopsNavChanged.Position" />.</summary>
    MarkAdded = 0,

    /// <summary>The mark <see cref="CyclopsNavChanged.MarkId" /> was taken away.</summary>
    MarkRemoved = 1,

    /// <summary>Every mark of the Cyclops was taken away.</summary>
    MarksCleared = 2
}

/// <summary>
/// The navigator of a Cyclops with the Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md, station 1): a player placed or removed a mark on the map of the console, or cleared them. The player's client
/// sends it, the server checks it, keeps the marks of each Cyclops (<c>CyclopsNavRegistry</c>) and passes it on to everybody else; every client keeps the marks it has heard of and draws them (a ping on the
/// HUD of the players aboard, an icon on the helm hologram and on the console map). A client that joins later is sent the marks that are on, as ordinary <see cref="MarkAdded" /> packets. The marks belong to
/// the Cyclops, not to the player who placed them, so they stay when that player leaves. They are not saved with the world: a server that restarts has none. The server also sends this on its own, with
/// <see cref="CyclopsNavChange.MarkRemoved" /> or <see cref="CyclopsNavChange.MarksCleared" />, to the sender only, when it refused a placement or a removal, so that the sender takes its own copy back.
/// </summary>
[Serializable]
public class CyclopsNavChanged : Packet
{
    public CyclopsNavChange Change { get; }

    /// <summary>The Cyclops that the marks belong to.</summary>
    public NitroxId CyclopsId { get; }

    /// <summary>The mark that was added or removed (made up by the client that placed it; unused for <see cref="CyclopsNavChange.MarksCleared" />).</summary>
    public NitroxId MarkId { get; }

    /// <summary>Where the mark is in the world.</summary>
    public NitroxVector3 Position { get; }

    /// <summary>What kind of mark: see <c>CyclopsNavRules</c>.</summary>
    public byte MarkKind { get; }

    public string Label { get; }

    /// <summary>The player who placed the mark (the server puts the sender's own session in).</summary>
    public SessionId Owner { get; }

    public string OwnerName { get; }

    public CyclopsNavChanged(CyclopsNavChange change, NitroxId cyclopsId, NitroxId markId, NitroxVector3 position, byte markKind, string label, SessionId owner, string ownerName)
    {
        Change = change;
        CyclopsId = cyclopsId;
        MarkId = markId;
        Position = position;
        MarkKind = markKind;
        Label = label;
        Owner = owner;
        OwnerName = ownerName;
    }
}
