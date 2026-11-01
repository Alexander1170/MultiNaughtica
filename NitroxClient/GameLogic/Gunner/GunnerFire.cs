using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Gunner;

/// <summary>The answer to a shot that the gunner asked for (<see cref="GunnerFire.TryRequest" />).</summary>
public readonly struct GunnerShotAnswer
{
    public GunnerShotAnswer(bool answered, CyclopsGunnerFailure failure, float charge)
    {
        Answered = answered;
        Failure = failure;
        Charge = charge;
    }

    /// <summary>False when no answer came in time.</summary>
    public bool Answered { get; }

    /// <summary><see cref="CyclopsGunnerFailure.None" />: the shot is paid for, fire it.</summary>
    public CyclopsGunnerFailure Failure { get; }

    /// <summary>The charge that was asked for and paid (the server keeps it inside 0 to 1).</summary>
    public float Charge { get; }

    public bool Paid => Answered && Failure == CyclopsGunnerFailure.None;
}

/// <summary>
/// The permission and the payment of a shot of the gunner station (<see cref="CyclopsGunnerFire" />), on this client. As the gunner: ask (<see cref="TryRequest" />), wait for the answer for three seconds and tell the
/// console what it was; one request at a time, each with its own number so that a late answer to an old request is not taken for the new one. As the player whose client simulates the Cyclops (which may be the same
/// player): the server forwards a shot to be paid for; this client checks the module and the power, takes the energy from the Cyclops and always answers. Only the client that simulates a Cyclops changes its energy
/// (the sonar ping works the same way).
/// </summary>
public static class GunnerFire
{
    private sealed class Outstanding
    {
        public int Shot;
        public NitroxId CyclopsId;
        public float SentAt;
        public Action<GunnerShotAnswer> Done;
    }

    private static readonly Dictionary<NitroxId, float> lastPaid = [];
    private static Outstanding outstanding;
    private static int shotCounter;

    /// <summary>A request is waiting for its answer.</summary>
    public static bool IsWaiting => outstanding != null;

    /// <summary>Forgets whatever is waiting (the session ended, the console closed): an answer that comes later belongs to nobody.</summary>
    public static void Reset()
    {
        outstanding = null;
        lastPaid.Clear();
    }

    /// <summary>Forgets the request that is waiting (the console closed): an answer that comes later belongs to nobody. The payments this client made as the simulator are kept.</summary>
    public static void CancelOutstanding()
    {
        outstanding = null;
    }

    /// <summary>Asks for a shot of this charge. False (nothing sent) when one is waiting already.</summary>
    public static bool TryRequest(NitroxId cyclopsId, float charge, Action<GunnerShotAnswer> done)
    {
        if (cyclopsId == null || outstanding != null)
        {
            return false;
        }
        outstanding = new Outstanding { Shot = ++shotCounter, CyclopsId = cyclopsId, SentAt = Time.time, Done = done };
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsGunnerFire.Request(cyclopsId, outstanding.Shot, charge, CyclopsGunnerAmmo.StasisSphere));
        return true;
    }

    /// <summary>Called every frame by the console: an answer that did not come in time is given up on.</summary>
    public static void Tick()
    {
        if (outstanding != null && Time.time - outstanding.SentAt > GunnerRules.REQUEST_TIMEOUT_SECONDS)
        {
            Outstanding gaveUp = outstanding;
            outstanding = null;
            gaveUp.Done?.Invoke(new GunnerShotAnswer(false, CyclopsGunnerFailure.None, 0f));
        }
    }

    public static void OnPacket(CyclopsGunnerFire packet)
    {
        if (packet.CyclopsId == null)
        {
            return;
        }
        switch (packet.Kind)
        {
            case CyclopsGunnerFireKind.Forward:
                Pay(packet);
                break;
            case CyclopsGunnerFireKind.Result:
                if (outstanding != null && outstanding.Shot == packet.Shot && outstanding.CyclopsId.Equals(packet.CyclopsId))
                {
                    Outstanding answered = outstanding;
                    outstanding = null;
                    answered.Done?.Invoke(new GunnerShotAnswer(true, packet.Failure, packet.ChargeNormalized));
                }
                break;
        }
    }

    /// <summary>This client simulates the Cyclops: check the module and the power, take the energy and answer. Always answers, so that the gunner never waits for a Cyclops this client cannot find.</summary>
    private static void Pay(CyclopsGunnerFire forward)
    {
        CyclopsGunnerFailure failure = CyclopsGunnerFailure.NotAvailable;
        if (NitroxEntity.TryGetObjectFrom(forward.CyclopsId, out GameObject cyclopsObject) && cyclopsObject.TryGetComponent(out SubRoot cyclops))
        {
            failure = Check(cyclops, forward);
        }
        else
        {
            Log.Info($"[VehicleUpgrades] gunner: asked to pay for a shot of Cyclops {forward.CyclopsId} that this client cannot find");
        }
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsGunnerFire.Result(forward.CyclopsId, forward.Shot, forward.Gunner, forward.ChargeNormalized, forward.Ammo, failure));
    }

    private static CyclopsGunnerFailure Check(SubRoot cyclops, CyclopsGunnerFire forward)
    {
        if (!VehicleUpgradeSettings.Enabled || !VehicleUpgradeSettings.Received)
        {
            return CyclopsGunnerFailure.NotAvailable;
        }
        if (!GunnerStation.HasModule(cyclops))
        {
            return CyclopsGunnerFailure.NoModule;
        }
        // The server keeps the cooldown too (80 percent of it); this is for a server that does not, and for two clients with different settings
        float cooldown = VehicleUpgradeSettings.GunnerCooldownSeconds * 0.8f;
        if (lastPaid.TryGetValue(forward.CyclopsId, out float last) && GunnerRules.CooldownRemaining(last, Time.unscaledTime, cooldown) > 0f)
        {
            return CyclopsGunnerFailure.TooSoon;
        }
        float cost = GunnerRules.ShotEnergy(forward.ChargeNormalized, VehicleUpgradeSettings.GunnerShotEnergy);
        // Creative mode needs no power: the game changes nothing then and calls every drain paid, so a Cyclops with no charged cells (nobody needs them there) can fire too
        bool free = cost <= 0f || !GameModeUtils.RequiresPower();
        if (!free)
        {
            PowerRelay relay = cyclops.powerRelay;
            // A drain of more than there is takes what is left and fails: look first
            if (!relay || relay.GetPower() < cost || !PowerSystem.ConsumeEnergy(relay, cost, out _))
            {
                return CyclopsGunnerFailure.NoPower;
            }
        }
        lastPaid[forward.CyclopsId] = Time.unscaledTime;
        Log.Info($"[VehicleUpgrades] gunner: paid {cost:F1} energy for a shot of {forward.ChargeNormalized:F2} on Cyclops {forward.CyclopsId} for session {forward.Gunner}{(free ? " (free: creative mode or no cost)" : string.Empty)}");
        return CyclopsGunnerFailure.None;
    }

    /// <summary>What the gunner reads when a shot was refused.</summary>
    public static string TextOf(GunnerShotAnswer answer)
    {
        if (!answer.Answered)
        {
            return "The Cyclops did not answer: no shot";
        }
        return answer.Failure switch
        {
            CyclopsGunnerFailure.NotAvailable => "The gunner station is off",
            CyclopsGunnerFailure.NotSeated => "You are not at the gunner station",
            CyclopsGunnerFailure.NoSimulator => "Nobody is running the Cyclops: the gunner cannot fire",
            CyclopsGunnerFailure.NoModule => "The Cyclops has no Gunner Station",
            CyclopsGunnerFailure.NoPower => "The Cyclops has not the power for a shot",
            CyclopsGunnerFailure.TooSoon => "The turret needs a moment",
            _ => string.Empty
        };
    }
}
