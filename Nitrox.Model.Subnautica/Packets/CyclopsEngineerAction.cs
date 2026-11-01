using System;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>What a <see cref="CyclopsEngineerAction" /> says.</summary>
public enum CyclopsEngineerActionStep : byte
{
    /// <summary>Not a message: the value of a field that was never filled in or a garbled packet. The server drops it.</summary>
    Unknown = 0,

    /// <summary>Client to server: the engineer pressed a button that only the simulator of the Cyclops can carry out.</summary>
    Request = 1,

    /// <summary>Server to the player who simulates the Cyclops (which may be the engineer): carry this out for <see cref="CyclopsEngineerAction.Engineer" /> and say how it went.</summary>
    Forward = 2,

    /// <summary>The answer: from the simulator by way of the server, or the server's own refusal, to the engineer alone. <see cref="CyclopsEngineerFailure.None" /> means it was carried out.</summary>
    Result = 3
}

/// <summary>The buttons of the engineer console that the server has to see. Routing the power needs the server only; the overdrive and the vent act on the engine and so need the simulator too. The fire suppression is the game's own relay.</summary>
public enum CyclopsEngineerActionKind : byte
{
    Unknown = 0,

    /// <summary>Send the power to <see cref="CyclopsEngineerAction.Value" /> (an <c>EngineerRoute</c>). The server keeps the route and tells everybody (<see cref="CyclopsEngineerState" />).</summary>
    Route = 1,

    /// <summary>The short speed boost past flank speed that heats the engine.</summary>
    Overdrive = 2,

    /// <summary>Let the engine heat off (costs energy, has a cooldown).</summary>
    Vent = 3
}

/// <summary>Why an action did not happen.</summary>
public enum CyclopsEngineerFailure : byte
{
    /// <summary>No failure: it was carried out.</summary>
    None = 0,

    /// <summary>The vehicle upgrades are off, the entity is no live Cyclops or the action is not known.</summary>
    NotAvailable = 1,

    /// <summary>The sender does not sit at the engineer console of this Cyclops.</summary>
    NotSeated = 2,

    /// <summary>Nobody simulates the Cyclops, so nobody can carry it out.</summary>
    NoSimulator = 3,

    /// <summary>The Cyclops has no Engineer Console module (checked by the simulator, which can see the upgrade console).</summary>
    NoModule = 4,

    /// <summary>The Cyclops has not the power the action needs.</summary>
    NoPower = 5,

    /// <summary>Another press a moment ago (the server keeps a floor of its own, and so does the simulator).</summary>
    TooSoon = 6,

    /// <summary>The overdrive needs the power routed to the engines.</summary>
    WrongRoute = 7,

    /// <summary>The overdrive needs the engine on.</summary>
    EngineOff = 8,

    /// <summary>The overdrive needs flank speed.</summary>
    NotFlank = 9,

    /// <summary>The overdrive or the vent is still cooling down.</summary>
    Cooldown = 10,

    /// <summary>The vent: the engine has no heat to take off (nothing is paid and no cooldown starts).</summary>
    AlreadyCool = 11
}

/// <summary>
/// A button of the engineer console that the server has to see (docs/coop/VEHICLE_UPGRADES.md, station 3). The engineer asks (<see cref="CyclopsEngineerActionStep.Request" />); the server checks the seat and passes it
/// to the player whose client simulates the Cyclops (<see cref="CyclopsEngineerActionStep.Forward" />), because the engine, its heat and its energy only exist there (the tow button and the sonar ping work the
/// same way); the simulator carries it out and answers (<see cref="CyclopsEngineerActionStep.Result" />) by way of the server, which tells the engineer alone. What changed for everybody (the route, the cooldowns) travels as <see cref="CyclopsEngineerState" />.
/// </summary>
[Serializable]
public class CyclopsEngineerAction : Packet
{
    public CyclopsEngineerActionStep Step { get; }

    public CyclopsEngineerActionKind Action { get; }

    public NitroxId CyclopsId { get; }

    /// <summary>The engineer's own count of its requests, echoed in the forward and the result, so that an answer is matched to the request it answers.</summary>
    public int Counter { get; }

    /// <summary>The engineer. A client leaves it empty in a request (the server uses the connection); the simulator names the engineer it answers in the result.</summary>
    public SessionId Engineer { get; }

    /// <summary>For a <see cref="CyclopsEngineerActionStep.Result" />: <see cref="CyclopsEngineerFailure.None" /> when it was carried out.</summary>
    public CyclopsEngineerFailure Failure { get; }

    /// <summary>For <see cref="CyclopsEngineerActionKind.Route" />: the route (an <c>EngineerRoute</c>). Otherwise 0. For a result of a vent or an overdrive: the engine heat after it (0 to 10).</summary>
    public int Value { get; }

    public CyclopsEngineerAction(CyclopsEngineerActionStep step, CyclopsEngineerActionKind action, NitroxId cyclopsId, int counter, SessionId engineer, CyclopsEngineerFailure failure, int value)
    {
        Step = step;
        Action = action;
        CyclopsId = cyclopsId;
        Counter = counter;
        Engineer = engineer;
        Failure = failure;
        Value = value;
    }

    public static CyclopsEngineerAction Request(CyclopsEngineerActionKind action, NitroxId cyclopsId, int counter, int value = 0) => new(CyclopsEngineerActionStep.Request, action, cyclopsId, counter, default, CyclopsEngineerFailure.None, value);

    public static CyclopsEngineerAction Forward(CyclopsEngineerActionKind action, NitroxId cyclopsId, int counter, SessionId engineer) => new(CyclopsEngineerActionStep.Forward, action, cyclopsId, counter, engineer, CyclopsEngineerFailure.None, 0);

    public static CyclopsEngineerAction Result(CyclopsEngineerActionKind action, NitroxId cyclopsId, int counter, SessionId engineer, CyclopsEngineerFailure failure, int value = 0) => new(CyclopsEngineerActionStep.Result, action, cyclopsId, counter, engineer, failure, value);
}
