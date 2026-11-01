using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// The sonar ping that the navigator fires from the console (docs/coop/VEHICLE_UPGRADES.md, station 1): the module's own ping, the wave that the game draws on the screen of a player aboard, with the sound of the
/// sonar button and the energy that it costs the Cyclops. The Cyclops needs the Cyclops Sonar Module (the console does not make a sonar of its own). Only the client that simulates the Cyclops changes its energy, so
/// a navigator who does not simulate it asks that client (<see cref="CyclopsSonarPing" />): the simulator fires the ping, pays for it and tells everybody aboard to play it; a failure is told to the navigator.
/// </summary>
public static class NavigatorSonar
{
    /// <summary>Seconds between two pings of one console (the game's own sonar button pings every five seconds).</summary>
    public const float COOLDOWN_SECONDS = 5f;

    private static float nextAllowed;
    private static readonly System.Collections.Generic.Dictionary<NitroxId, float> lastFired = [];

    /// <summary>The navigator asks for a ping of this Cyclops; <paramref name="message" /> is what they read at once when it cannot be (module, cooldown).</summary>
    public static void Request(SubRoot cyclops, System.Action<string> message)
    {
        if (!cyclops || !cyclops.TryGetNitroxId(out NitroxId id))
        {
            return;
        }
        if (!VehicleUpgradeSettings.Enabled || !TerrainSonar.IsAvailable(cyclops))
        {
            message(TextOf(CyclopsSonarFailure.NotAvailable));
            return;
        }
        if (!TerrainSonar.HasVanillaSonar(cyclops))
        {
            message(TextOf(CyclopsSonarFailure.NoSonarModule));
            return;
        }
        if (Time.unscaledTime < nextAllowed)
        {
            message(TextOf(CyclopsSonarFailure.TooSoon));
            return;
        }
        nextAllowed = Time.unscaledTime + COOLDOWN_SECONDS;
        if (HoldsLock(id))
        {
            Fire(cyclops, id, LocalSession(), message);
        }
        else
        {
            NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsSonarPing.Press(id));
        }
    }

    /// <summary>What the server says: a press to do (this client simulates the Cyclops), or the result of a ping.</summary>
    public static void OnPacket(CyclopsSonarPing packet)
    {
        if (packet.CyclopsId == null || !NitroxEntity.TryGetObjectFrom(packet.CyclopsId, out GameObject cyclopsObject) || !cyclopsObject.TryGetComponent(out SubRoot cyclops))
        {
            return;
        }
        switch (packet.Kind)
        {
            case CyclopsSonarPingKind.Forward:
                Fire(cyclops, packet.CyclopsId, packet.Presser, ErrorMessage.AddMessage);
                break;
            case CyclopsSonarPingKind.Result:
                if (packet.Failure == CyclopsSonarFailure.None)
                {
                    PlayEffect(cyclops);
                }
                else if (packet.Presser == LocalSession())
                {
                    ErrorMessage.AddMessage(TextOf(packet.Failure));
                }
                break;
        }
    }

    /// <summary>The simulating client fires the ping: the module and the power are checked here, where the energy is, and the result goes to everybody.</summary>
    private static void Fire(SubRoot cyclops, NitroxId id, SessionId presser, System.Action<string> message)
    {
        CyclopsSonarFailure failure = Check(cyclops);
        // a client that does not keep the interval (or two navigators at once) does not make this one pay for a ping every moment
        if (failure == CyclopsSonarFailure.None && lastFired.TryGetValue(id, out float last) && Time.unscaledTime - last < COOLDOWN_SECONDS - 0.5f)
        {
            failure = CyclopsSonarFailure.TooSoon;
        }
        if (failure == CyclopsSonarFailure.None && !PowerSystem.ConsumeEnergy(cyclops.powerRelay, cyclops.sonarPowerCost, out _))
        {
            failure = CyclopsSonarFailure.NoPower;
        }
        if (failure == CyclopsSonarFailure.None)
        {
            lastFired[id] = Time.unscaledTime;
            PlayEffect(cyclops);
            Log.Info($"[VehicleUpgrades] navigator: sonar ping fired on Cyclops {id} for session {presser}");
        }
        else if (presser == LocalSession())
        {
            message(TextOf(failure));
        }
        NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsSonarPing.Result(id, presser, failure));
    }

    private static CyclopsSonarFailure Check(SubRoot cyclops)
    {
        if (!VehicleUpgradeSettings.Enabled)
        {
            return CyclopsSonarFailure.NotAvailable;
        }
        if (!TerrainSonar.HasVanillaSonar(cyclops))
        {
            return CyclopsSonarFailure.NoSonarModule;
        }
        return cyclops.powerRelay && cyclops.powerRelay.IsPowered() ? CyclopsSonarFailure.None : CyclopsSonarFailure.NoPower;
    }

    /// <summary>The wave on the screen and the sound of the sonar button, for a player who is aboard that Cyclops (the same as the game's own ping does).</summary>
    private static void PlayEffect(SubRoot cyclops)
    {
        if (!Player.main || Player.main.currentSub != cyclops || !SNCameraRoot.main)
        {
            return;
        }
        SNCameraRoot.main.SonarPing();
        Log.Info($"[VehicleUpgrades] navigator: sonar ping played on this client for Cyclops {cyclops.name}");
        CyclopsSonarButton button = cyclops.GetComponentInChildren<CyclopsSonarButton>(true);
        if (button && button.soundFX)
        {
            button.soundFX.Play();
        }
    }

    public static string TextOf(CyclopsSonarFailure failure)
    {
        return failure switch
        {
            CyclopsSonarFailure.NotAvailable => "The vehicle upgrades are off",
            CyclopsSonarFailure.NoSimulator => "Nobody is running the Cyclops: the sonar cannot ping",
            CyclopsSonarFailure.NoSonarModule => "The ping needs the Cyclops Sonar Module",
            CyclopsSonarFailure.NoPower => "The Cyclops has not the power for a ping",
            CyclopsSonarFailure.TooSoon => "The sonar needs a moment",
            _ => string.Empty
        };
    }

    private static bool HoldsLock(NitroxId id) => NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);

    private static SessionId LocalSession() => NitroxServiceLocator.LocateService<IMultiplayerSession>().Reservation.SessionId;
}
