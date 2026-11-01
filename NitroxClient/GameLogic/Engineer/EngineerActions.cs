using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>The answer to a button that the engineer pressed (<see cref="EngineerActions.TryRequest" />).</summary>
public readonly struct EngineerAnswer
{
    public EngineerAnswer(bool answered, CyclopsEngineerFailure failure, int value)
    {
        Answered = answered;
        Failure = failure;
        Value = value;
    }

    /// <summary>False when no answer came in time.</summary>
    public bool Answered { get; }

    /// <summary><see cref="CyclopsEngineerFailure.None" />: it was carried out.</summary>
    public CyclopsEngineerFailure Failure { get; }

    /// <summary>For the overdrive and the vent: the engine heat after it.</summary>
    public int Value { get; }

    public bool Done => Answered && Failure == CyclopsEngineerFailure.None;
}

/// <summary>
/// The buttons of the engineer console that the server has to see (<see cref="CyclopsEngineerAction" />), on this client. As the engineer: ask (<see cref="TryRequest" />), wait three seconds for the answer and tell
/// the console what it was; one request of each kind at a time, each with its own number so that a late answer to an old request is not taken for the new one. As the player whose client simulates the Cyclops
/// (which may be the same player): the server forwards an overdrive or a vent to be carried out; this client checks the module, the engine and the power, changes the engine heat and the energy and always
/// answers. Only the client that simulates a Cyclops changes its energy (the sonar ping and the gunner's shot work the same way) and only there does the engine run.
/// </summary>
public static class EngineerActions
{
    private sealed class Outstanding
    {
        public int Counter;
        public NitroxId CyclopsId;
        public float SentAt;
        public Action<EngineerAnswer> Done;
    }

    private static readonly Dictionary<CyclopsEngineerActionKind, Outstanding> outstanding = [];
    private static int counter;

    public static bool IsWaiting(CyclopsEngineerActionKind action) => outstanding.ContainsKey(action);

    /// <summary>Forgets whatever is waiting (the session ended, the console closed): an answer that comes later belongs to nobody.</summary>
    public static void Reset()
    {
        outstanding.Clear();
    }

    /// <summary>Asks for a button to be pressed. False (nothing sent) when one of that kind is waiting already.</summary>
    public static bool TryRequest(NitroxId cyclopsId, CyclopsEngineerActionKind action, int value, Action<EngineerAnswer> done)
    {
        if (cyclopsId == null || outstanding.ContainsKey(action))
        {
            return false;
        }
        Outstanding request = new() { Counter = ++counter, CyclopsId = cyclopsId, SentAt = Time.time, Done = done };
        outstanding[action] = request;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsEngineerAction.Request(action, cyclopsId, request.Counter, value));
        return true;
    }

    /// <summary>Called every frame by the console: an answer that did not come in time is given up on.</summary>
    public static void Tick()
    {
        if (outstanding.Count == 0)
        {
            return;
        }
        List<CyclopsEngineerActionKind> late = null;
        foreach (KeyValuePair<CyclopsEngineerActionKind, Outstanding> pair in outstanding)
        {
            if (Time.time - pair.Value.SentAt > CyclopsEngineerRules.REQUEST_TIMEOUT_SECONDS)
            {
                (late ??= []).Add(pair.Key);
            }
        }
        if (late == null)
        {
            return;
        }
        foreach (CyclopsEngineerActionKind action in late)
        {
            Outstanding gaveUp = outstanding[action];
            outstanding.Remove(action);
            gaveUp.Done?.Invoke(new EngineerAnswer(false, CyclopsEngineerFailure.None, 0));
        }
    }

    public static void OnPacket(CyclopsEngineerAction packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Step)
        {
            case CyclopsEngineerActionStep.Forward:
                CarryOut(packet);
                break;
            case CyclopsEngineerActionStep.Result:
                if (outstanding.TryGetValue(packet.Action, out Outstanding request) && request.Counter == packet.Counter && request.CyclopsId.Equals(packet.CyclopsId))
                {
                    outstanding.Remove(packet.Action);
                    request.Done?.Invoke(new EngineerAnswer(true, packet.Failure, packet.Value));
                }
                break;
        }
    }

    /// <summary>This client simulates the Cyclops: check the module, the engine and the power, change the heat and the energy and answer. Always answers, so that the engineer never waits for a Cyclops this client cannot find.</summary>
    private static void CarryOut(CyclopsEngineerAction forward)
    {
        CyclopsEngineerFailure failure = CyclopsEngineerFailure.NotAvailable;
        int heat = 0;
        if (NitroxEntity.TryGetObjectFrom(forward.CyclopsId, out GameObject cyclopsObject) && cyclopsObject.TryGetComponent(out SubRoot cyclops))
        {
            failure = Execute(cyclops, forward, out heat);
        }
        else
        {
            Log.Info($"[VehicleUpgrades] engineer: asked to {forward.Action} Cyclops {forward.CyclopsId} that this client cannot find");
        }
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsEngineerAction.Result(forward.Action, forward.CyclopsId, forward.Counter, forward.Engineer, failure, heat));
    }

    private static CyclopsEngineerFailure Execute(SubRoot cyclops, CyclopsEngineerAction forward, out int heat)
    {
        heat = 0;
        if (!VehicleUpgradeSettings.Enabled || !VehicleUpgradeSettings.Received)
        {
            return CyclopsEngineerFailure.NotAvailable;
        }
        if (!EngineerStation.HasModule(cyclops))
        {
            return CyclopsEngineerFailure.NoModule;
        }
        // The engine and the energy of a Cyclops are only changed by the client that holds its lock: the lock may have moved since the server looked
        if (!NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(forward.CyclopsId))
        {
            return CyclopsEngineerFailure.NoSimulator;
        }
        SubFire fire = cyclops.GetComponentInChildren<SubFire>(true);
        SubControl control = cyclops.GetComponent<SubControl>();
        CyclopsMotorMode motor = control ? control.cyclopsMotorMode : null;
        PowerRelay relay = cyclops.powerRelay;
        // Creative mode needs no power: the game changes nothing then and calls every drain paid, so a Cyclops with no charged cells (nobody needs them there) can do all of it
        bool free = !GameModeUtils.RequiresPower();
        bool powered = free || (relay && relay.IsPowered());
        heat = EngineerHeat.Get(fire);

        switch (forward.Action)
        {
            case CyclopsEngineerActionKind.Overdrive:
            {
                bool flank = motor && motor.cyclopsMotorMode == CyclopsMotorMode.CyclopsMotorModes.Flank;
                CyclopsEngineerFailure failure = CyclopsEngineerRules.SimulatorCheckOverdrive(motor && motor.engineOn, flank, powered);
                if (failure != CyclopsEngineerFailure.None)
                {
                    return failure;
                }
                EngineerHeat.Set(fire, CyclopsEngineerRules.HeatAfterOverdriveStart(heat));
                heat = EngineerHeat.Get(fire);
                Log.Info($"[VehicleUpgrades] engineer: overdrive on Cyclops {forward.CyclopsId} for session {forward.Engineer}, heat now {heat}");
                return CyclopsEngineerFailure.None;
            }
            case CyclopsEngineerActionKind.Vent:
            {
                // A drain of more than there is takes what is left and fails: look first
                bool hasEnergy = free || (relay && relay.GetPower() >= CyclopsEngineerRules.VENT_ENERGY);
                CyclopsEngineerFailure failure = CyclopsEngineerRules.SimulatorCheckVent(powered, hasEnergy, heat);
                if (failure != CyclopsEngineerFailure.None)
                {
                    return failure;
                }
                if (!free && !PowerSystem.ConsumeEnergy(relay, CyclopsEngineerRules.VENT_ENERGY, out _))
                {
                    return CyclopsEngineerFailure.NoPower;
                }
                EngineerHeat.Set(fire, CyclopsEngineerRules.HeatAfterVent(heat));
                heat = EngineerHeat.Get(fire);
                Log.Info($"[VehicleUpgrades] engineer: vent on Cyclops {forward.CyclopsId} for session {forward.Engineer}, heat now {heat}{(free ? " (free: creative mode)" : string.Empty)}");
                return CyclopsEngineerFailure.None;
            }
            default:
                return CyclopsEngineerFailure.NotAvailable;
        }
    }

    /// <summary>What the engineer reads when a button was refused.</summary>
    public static string TextOf(CyclopsEngineerActionKind action, EngineerAnswer answer)
    {
        if (!answer.Answered)
        {
            return "The Cyclops did not answer";
        }
        return answer.Failure switch
        {
            CyclopsEngineerFailure.NotAvailable => "The engineer console is off",
            CyclopsEngineerFailure.NotSeated => "You are not at the engineer console",
            CyclopsEngineerFailure.NoSimulator => "Nobody is running the Cyclops",
            CyclopsEngineerFailure.NoModule => "The Cyclops has no Engineer Console",
            CyclopsEngineerFailure.NoPower => action == CyclopsEngineerActionKind.Vent ? "Not enough power to vent" : "The Cyclops has no power",
            CyclopsEngineerFailure.TooSoon => "A moment, please",
            CyclopsEngineerFailure.WrongRoute => "Send the power to the engines first",
            CyclopsEngineerFailure.EngineOff => "The engine is off",
            CyclopsEngineerFailure.NotFlank => "The overdrive needs flank speed",
            CyclopsEngineerFailure.Cooldown => action == CyclopsEngineerActionKind.Vent ? "The vents are still cooling" : "The overdrive is still cooling",
            CyclopsEngineerFailure.AlreadyCool => "The engine is cool already",
            _ => string.Empty
        };
    }
}
