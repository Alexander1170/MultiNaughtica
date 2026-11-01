using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsSonarPing" /> says.</summary>
public enum CyclopsSonarPingKind : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the navigator at the console of a Cyclops that another player simulates asks for a sonar ping.</summary>
    Press = 1,

    /// <summary>Server to the player who simulates the Cyclops: <see cref="CyclopsSonarPing.Presser" /> asked for a ping, do what the sonar button at the helm would do.</summary>
    Forward = 2,

    /// <summary>The result, from the simulator by way of the server: a ping that was fired reaches everybody (each client aboard sees the wave), a failure only the player who asked.</summary>
    Result = 3
}

/// <summary>Why the ping did not happen (the message the navigator reads).</summary>
public enum CyclopsSonarFailure : byte
{
    /// <summary>No failure: the ping was fired.</summary>
    None = 0,

    /// <summary>The vehicle upgrades are off on this server.</summary>
    NotAvailable = 1,

    /// <summary>Nobody simulates the Cyclops (nobody is near enough to run it), so nobody can fire the ping.</summary>
    NoSimulator = 2,

    /// <summary>The Cyclops has no Cyclops Sonar Module: the console fires the module's own ping and needs it.</summary>
    NoSonarModule = 3,

    /// <summary>The Cyclops has not the power that a ping costs.</summary>
    NoPower = 4,

    /// <summary>The Cyclops pinged a moment ago (the sonar button of the game pings every five seconds): the server and the simulating client both keep that.</summary>
    TooSoon = 5
}

/// <summary>
/// The sonar ping that the navigator fires from the console (docs/coop/VEHICLE_UPGRADES.md, station 1): the module's own ping, the wave that the game draws on the screen of a player aboard and the energy that
/// it costs. Energy of a Cyclops is only changed by the client that simulates it, so a navigator who does not is passed on, like the tow hook button: <see cref="CyclopsSonarPingKind.Press" /> (to the server) &#8594;
/// <see cref="CyclopsSonarPingKind.Forward" /> (to the simulator) &#8594; <see cref="CyclopsSonarPingKind.Result" /> (from the simulator, by way of the server, to everybody, or to the navigator alone when it failed).
/// The navigator who simulates the Cyclops fires it at once and only sends the result.
/// </summary>
[Serializable]
public class CyclopsSonarPing : Packet
{
    public CyclopsSonarPingKind Kind { get; }

    /// <summary>The Cyclops that pings.</summary>
    public NitroxId CyclopsId { get; }

    /// <summary>The navigator who asked. A client leaves it empty in a press (the server fills in the sender's); the simulator names the navigator it answers in the result.</summary>
    public SessionId Presser { get; }

    /// <summary>For a <see cref="CyclopsSonarPingKind.Result" />: <see cref="CyclopsSonarFailure.None" /> when the ping was fired.</summary>
    public CyclopsSonarFailure Failure { get; }

    public CyclopsSonarPing(CyclopsSonarPingKind kind, NitroxId cyclopsId, SessionId presser, CyclopsSonarFailure failure)
    {
        Kind = kind;
        CyclopsId = cyclopsId;
        Presser = presser;
        Failure = failure;
    }

    public static CyclopsSonarPing Press(NitroxId cyclopsId) => new(CyclopsSonarPingKind.Press, cyclopsId, default, CyclopsSonarFailure.None);

    public static CyclopsSonarPing Forward(NitroxId cyclopsId, SessionId presser) => new(CyclopsSonarPingKind.Forward, cyclopsId, presser, CyclopsSonarFailure.None);

    public static CyclopsSonarPing Result(NitroxId cyclopsId, SessionId presser, CyclopsSonarFailure failure) => new(CyclopsSonarPingKind.Result, cyclopsId, presser, failure);
}
