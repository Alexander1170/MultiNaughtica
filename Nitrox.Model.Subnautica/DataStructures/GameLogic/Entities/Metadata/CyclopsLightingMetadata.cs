using System;
using System.Runtime.Serialization;
using BinaryPack.Attributes;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

[Serializable]
[DataContract]
public class CyclopsLightingMetadata : EntityMetadata
{
    [DataMember(Order = 1)]
    public bool FloodLightsOn { get; set; }

    [DataMember(Order = 2)]
    public bool InternalLightsOn { get; set; }

    /// <summary>The on/off switch of the Cyclops Power Share, a button next to the light buttons (off when a world was saved before it existed).</summary>
    [DataMember(Order = 3)]
    public bool PowerShareOn { get; set; }

    /// <summary>Nothing is named: the report carries the lights as the client has them (and a switch that is only its old view).</summary>
    public const byte CHANGED_NOTHING = 0;

    /// <summary>The press that sent the report changed the floodlights.</summary>
    public const byte CHANGED_FLOOD = 1;

    /// <summary>The press that sent the report changed the interior lights.</summary>
    public const byte CHANGED_INTERNAL = 2;

    /// <summary>The press that sent the report changed the Power Share switch.</summary>
    public const byte CHANGED_POWER_SHARE = 4;

    /// <summary>
    ///     What the press that sent this report changed (<see cref="CHANGED_FLOOD" />, <see cref="CHANGED_INTERNAL" />, <see cref="CHANGED_POWER_SHARE" />). A client reports the whole object from its own state, so the
    ///     parts the press did not touch are only its view of them, which can be old (<see cref="Merge" />). Never stored: <see cref="Merge" /> clears it.
    /// </summary>
    [DataMember(Order = 4)]
    public byte Changed { get; set; }

    [IgnoreConstructor]
    protected CyclopsLightingMetadata()
    {
        // Constructor for serialization. Has to be "protected" for json serialization.
    }

    public CyclopsLightingMetadata(bool floodLightsOn, bool internalLightsOn, bool powerShareOn, byte changed = CHANGED_NOTHING)
    {
        FloodLightsOn = floodLightsOn;
        InternalLightsOn = internalLightsOn;
        PowerShareOn = powerShareOn;
        Changed = changed;
    }

    /// <summary>
    ///     What the server keeps after a report of a lighting panel. The report is the whole object from the presser's own state, so only what the press changed (<see cref="Changed" />) is news; the rest is that
    ///     client's view, which is old when the press of another player has not reached it yet (or never does: an update for a Cyclops that is not spawned for the client is dropped), and storing it would undo that
    ///     press for everybody. The parts the press changed are the reported ones, the others are the ones the server has. A report that names nothing is a report of the lights (the switch is the server's). The
    ///     reported ones when the server has nothing yet.
    /// </summary>
    public static CyclopsLightingMetadata Merge(CyclopsLightingMetadata stored, CyclopsLightingMetadata reported)
    {
        if (stored == null)
        {
            return new CyclopsLightingMetadata(reported.FloodLightsOn, reported.InternalLightsOn, reported.PowerShareOn);
        }
        bool reportsLights = reported.Changed == CHANGED_NOTHING;
        bool floodLightsOn = reportsLights || (reported.Changed & CHANGED_FLOOD) != 0 ? reported.FloodLightsOn : stored.FloodLightsOn;
        bool internalLightsOn = reportsLights || (reported.Changed & CHANGED_INTERNAL) != 0 ? reported.InternalLightsOn : stored.InternalLightsOn;
        bool powerShareOn = (reported.Changed & CHANGED_POWER_SHARE) != 0 ? reported.PowerShareOn : stored.PowerShareOn;
        return new CyclopsLightingMetadata(floodLightsOn, internalLightsOn, powerShareOn);
    }

    /// <summary>Whether the lights and the switch are the same (what the press changed is not looked at): a reporter whose state differs from what the server kept is told what the server kept.</summary>
    public bool HasSameStateAs(CyclopsLightingMetadata other)
    {
        return other != null && FloodLightsOn == other.FloodLightsOn && InternalLightsOn == other.InternalLightsOn && PowerShareOn == other.PowerShareOn;
    }

    public override string ToString()
    {
        return $"[CyclopsLightningMetadata FloodLightsOn: {FloodLightsOn}, InternalLightsOn: {InternalLightsOn}, PowerShareOn: {PowerShareOn}{(Changed != CHANGED_NOTHING ? $", Changed: {Changed}" : "")}]";
    }
}
