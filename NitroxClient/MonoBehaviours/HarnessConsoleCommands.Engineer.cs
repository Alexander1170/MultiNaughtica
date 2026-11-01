#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of feature group 3, station 3, the Cyclops Engineer Console (docs/coop/VEHICLE_UPGRADES.md, a harness script that is not part of this repository). Each logs
///     <c>[SyncHarness] vueng: key=value</c> lines. A chat command cannot be typed into the client whose console is open (the console takes the input group), so the script types everything into Alice's chat as
///     <c>/console Bob vueng ...</c>; the commands go through the same code as the buttons of the screen (<see cref="EngineerActions" />, <see cref="EngineerSeats" />), so the live check exercises the real path.
///     <c>vueng [state|open|close|leave|take|route balanced|engines|shields|systems|overdrive|vent|suppress|mode slow|standard|flank|heat n|power v|silent|nudge dx,dy,dz|track seconds]</c> (the server's <c>/console</c> passes
///     the command and only TWO words after it): <c>state</c> one line with the station as this client sees it; <c>open</c> and <c>close</c> as the hand and the exit key do; <c>take</c> sends a seat request
///     whatever the table says; <c>leave</c> gives the seat up; <c>route</c>, <c>overdrive</c> and <c>vent</c> press the buttons and log the answer; <c>suppress</c> presses the fire suppression; <c>mode</c> clicks the
///     motor mode button of the helm (the pilot's client only); <c>heat</c> sets the engine heat (the simulating client only); <c>silent</c> clicks the silent running button; <c>nudge</c> moves the hand target and the
///     panel in the hull's frame; <c>track</c> logs the speed and the factors twice a second.
/// </summary>
public partial class HarnessConsoleCommands
{
    private bool vuEngTracking;

    private static EngineerConsole EngConsole()
    {
        if (Player.main && Player.main.currentSub)
        {
            EngineerConsole own = Player.main.currentSub.GetComponent<EngineerConsole>();
            if (own)
            {
                return own;
            }
        }
        EngineerConsole nearest = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (EngineerConsole candidate in FindObjectsOfType<EngineerConsole>())
        {
            float distance = Vector3.Distance(from, candidate.transform.position);
            if (distance < best && VuIsRealCyclops(candidate.GetComponent<SubRoot>()))
            {
                best = distance;
                nearest = candidate;
            }
        }
        return nearest;
    }

    private void LiveCommand_vueng(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        EngineerConsole console = EngConsole();
        if (!console)
        {
            Log.Warn($"{VU}vueng: no Cyclops with the engineer console component");
            return;
        }
        NitroxId id = console.CyclopsId;
        switch (what)
        {
            case "open":
                console.Enter();
                break;
            case "place":
                {
                    // vueng place x,z: the Cyclops goes to that spot at the same height, bow to world -z, at rest. The probe's drives start in deep water: the spawn point (z 330) has 2 to 6 m under the keel and a drive
                    // toward -z runs aground within 40 m (the drive samples show 0.2 to 0.6 m/s with W held)
                    string[] xz = LiveArgString(n, 1, "").Split(',');
                    if (xz.Length == 2 && float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float placeX) && float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float placeZ))
                    {
                        SubRoot placed = console.SubRoot;
                        Rigidbody placedBody = placed.GetComponent<Rigidbody>();
                        Vector3 placeFrom = placed.transform.position;
                        Vector3 placeTo = new(placeX, placeFrom.y, placeZ);
                        placed.transform.SetPositionAndRotation(placeTo, Quaternion.identity);
                        if (placedBody)
                        {
                            placedBody.position = placeTo;
                            placedBody.rotation = Quaternion.identity;
                            placedBody.velocity = Vector3.zero;
                            placedBody.angularVelocity = Vector3.zero;
                        }
                        Physics.SyncTransforms();
                        Log.Info($"{VU}vueng: place from {placeFrom.ToString("F1")} to {placeTo.ToString("F1")}");
                    }
                    else
                    {
                        Log.Warn($"{VU}vueng: place needs x,z");
                    }
                }
                break;
            case "close":
                console.Close(true, "vueng close");
                break;
            case "leave":
                EngineerSeats.SetBusy(id, false);
                EngineerSeats.Leave(id);
                break;
            case "take":
                // a seat request that ignores the table and the local checks: the server decides (and refuses an occupied console). The console is marked busy so that a grant is kept (until "leave")
                EngineerSeats.SetBusy(id, true);
                NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsEngineerSeat.Take(id));
                Log.Info($"{VU}vueng: take sent for {VuId(console.gameObject)}");
                break;
            case "route":
                {
                    string name = LiveArgString(n, 1, "balanced");
                    EngineerRoute route = name.ToLowerInvariant() switch
                    {
                        "engines" => EngineerRoute.Engines,
                        "shields" => EngineerRoute.Shields,
                        "systems" => EngineerRoute.Systems,
                        _ => EngineerRoute.Balanced
                    };
                    VuEngPress(id, CyclopsEngineerActionKind.Route, (int)route);
                }
                return;
            case "overdrive":
                VuEngPress(id, CyclopsEngineerActionKind.Overdrive, 0);
                return;
            case "vent":
                VuEngPress(id, CyclopsEngineerActionKind.Vent, 0);
                return;
            case "suppress":
                {
                    bool done = EngineerDamage.TrySuppress(console.SubRoot);
                    Log.Info($"{VU}vueng: suppress started={done} {VuEngSuppression(console.SubRoot)}");
                }
                break;
            case "mode":
                VuEngMode(console.SubRoot, LiveArgString(n, 1, "standard"));
                break;
            case "power":
                {
                    PowerRelay relay = console.SubRoot.powerRelay;
                    float wanted = LiveArgFloat(n, 1, 200f);
                    float now = relay ? relay.GetPower() : 0f;
                    if (relay && wanted > now)
                    {
                        PowerSystem.AddEnergy(relay, wanted - now, out _);
                    }
                    else if (relay && wanted < now)
                    {
                        PowerSystem.ConsumeEnergy(relay, now - wanted, out _);
                    }
                    Log.Info($"{VU}vueng: power was={VuNum(now)} wanted={VuNum(wanted)} now={VuNum(relay ? relay.GetPower() : 0f)} survival={GameModeUtils.RequiresPower()}");
                }
                break;
            case "heat":
                {
                    SubFire fire = console.SubRoot.GetComponentInChildren<SubFire>(true);
                    int wanted = Mathf.RoundToInt(LiveArgFloat(n, 1, 0f));
                    EngineerHeat.Set(fire, wanted);
                    Log.Info($"{VU}vueng: heat set wanted={wanted} now={EngineerHeat.Get(fire)}");
                }
                break;
            case "silent":
                {
                    CyclopsSilentRunningAbilityButton button = console.SubRoot.GetComponentInChildren<CyclopsSilentRunningAbilityButton>(true);
                    if (button)
                    {
                        button.OnClick();
                    }
                    Log.Info($"{VU}vueng: silent clicked={button != null} silentRunning={console.SubRoot.silentRunning}");
                }
                break;
            case "click":
                {
                    // a click on a bubble of the PDA page, by its name (shields, systems, engines, balanced, overdrive, vent, firesuppression), through the page's own handler
                    string bubble = LiveArgString(n, 1, "balanced");
                    EngineerPdaTab tab = EngineerPdaTab.Instance;
                    bool clicked = tab && tab.DebugClick(bubble);
                    Log.Info($"{VU}vueng: click bubble={bubble} clicked={clicked}");
                }
                break;
            case "nudge":
                EngineerConsole.DebugHandNudge = VuNavVector(n, 0f, 0f, 0f);
                break;
            case "track":
                if (vuEngTracking)
                {
                    Log.Warn($"{VU}vueng: track is already running");
                }
                else
                {
                    StartCoroutine(VuEngTrack(console, Mathf.Clamp(LiveArgFloat(n, 1, 20f), 1f, 120f)));
                }
                return;
        }
        Log.Info($"{VU}vueng: {VuEngState(console, id)}");
    }

    /// <summary>Presses a button the way the screen does and logs the answer (or that none came) in one line.</summary>
    private static void VuEngPress(NitroxId id, CyclopsEngineerActionKind action, int value)
    {
        bool sent = EngineerActions.TryRequest(id, action, value, answer =>
            Log.Info($"{VU}vueng: answer action={action} answered={answer.Answered} failure={answer.Failure} value={answer.Value} t={VuSyncedTime()}"));
        Log.Info($"{VU}vueng: press action={action} value={value} sent={sent} t={VuSyncedTime()}");
    }

    private static void VuEngMode(SubRoot cyclops, string name)
    {
        CyclopsMotorMode.CyclopsMotorModes wanted = name.ToLowerInvariant() switch
        {
            "slow" => CyclopsMotorMode.CyclopsMotorModes.Slow,
            "flank" => CyclopsMotorMode.CyclopsMotorModes.Flank,
            _ => CyclopsMotorMode.CyclopsMotorModes.Standard
        };
        bool clicked = false;
        foreach (CyclopsMotorModeButton button in cyclops.GetComponentsInChildren<CyclopsMotorModeButton>(true))
        {
            if (button.motorModeIndex == wanted)
            {
                button.OnClick();
                clicked = true;
            }
        }
        SubControl control = cyclops.GetComponent<SubControl>();
        Log.Info($"{VU}vueng: mode wanted={wanted} clicked={clicked} now={(control && control.cyclopsMotorMode ? control.cyclopsMotorMode.cyclopsMotorMode.ToString() : "none")}");
    }

    private static string VuEngSuppression(SubRoot cyclops)
    {
        EngineerDamage.Suppression suppression = EngineerDamage.SuppressionOf(cyclops);
        return $"suppressInstalled={suppression.Installed} suppressReady={suppression.Ready} suppressRunning={suppression.Running} suppressCooldown={VuNum(suppression.CooldownRemaining)}";
    }

    /// <summary>One line, plain tokens (a value with spaces is quoted, there are no brackets) so that the live script's key=value parser reads it.</summary>
    private static string VuEngState(EngineerConsole console, NitroxId id)
    {
        SubRoot cyclops = console.SubRoot;
        bool canEnter = console.CanEnter(out string why);
        EngineerSeats.TryGetEngineer(id, out EngineerSeats.Seat seat);
        EngineReading engine = console.ReadEngine();
        SubControl control = cyclops ? cyclops.GetComponent<SubControl>() : null;
        CyclopsMotorMode motor = control ? control.cyclopsMotorMode : null;
        bool flank = motor && motor.cyclopsMotorMode == CyclopsMotorMode.CyclopsMotorModes.Flank;
        Rigidbody body = cyclops ? cyclops.GetComponent<Rigidbody>() : null;
        bool holdsLock = id != null && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
        EngineerDamage.Picture picture = new();
        EngineerDamage.Collect(cyclops, picture);
        CyclopsNoiseManager noise = cyclops ? cyclops.noiseManager : null;
        StringBuilder line = new();
        line.Append($"cyclops={VuId(console.gameObject)} module={EngineerStation.HasModule(cyclops)} available={EngineerStation.IsAvailable(cyclops)} canEnter={canEnter} why=\"{why}\" console={console.DebugStateName} open={console.IsOpen} busy={console.IsBusy} panel={console.DebugHasPanel} handTarget={console.DebugHasHandTarget} ");
        line.Append($"engineer=\"{(seat.Session.IsPlayer ? seat.Name : "none")}\" engineerSession=\"{(seat.Session.IsPlayer ? seat.Session.ToString() : "none")}\" seatMine={(id != null && EngineerSeats.IsMine(id))} seatOther={(id != null && EngineerSeats.IsTakenByOther(id))} simulator={holdsLock} ");
        line.Append($"route={EngineerState.RouteOf(id)} effectiveRoute={EngineerState.EffectiveRoute(cyclops, out _)} overdriveActive={EngineerState.IsOverdriveActive(id)} overdrive={VuNum(EngineerState.OverdriveRemaining(id))} overdriveReady={VuNum(EngineerState.OverdriveReadyRemaining(id))} ventReady={VuNum(EngineerState.VentReadyRemaining(id))} ");
        line.Append($"speedFactor={VuNum(EngineerState.SpeedFactor(cyclops, flank))} shieldFactor={VuNum(EngineerState.ShieldCostFactor(cyclops))} sonarFactor={VuNum(EngineerState.SonarFactor(cyclops))} noiseFactor={VuNum(EngineerState.NoiseFactor(cyclops))} silentCostFactor={VuNum(EngineerState.SilentRunningCostFactor(cyclops))} ");
        line.Append($"silent={(cyclops && cyclops.silentRunning)} noisePercent={(noise ? VuNum(noise.GetNoisePercent()) : "none")} engineOn={(motor && motor.engineOn)} mode={(motor ? motor.cyclopsMotorMode.ToString() : "none")} throttle={(control && control.appliedThrottle)} ");
        line.Append($"heatKnown={engine.Known} heat={engine.Heat} heating={engine.Heating} power={VuNum(engine.PowerPercent)} {VuEngSuppression(cyclops)} fires={picture.Fires.Count} breaches={picture.Breaches.Count} rooms={picture.Rooms.Count} ");
        line.Append($"speed={(body ? VuNum(body.velocity.magnitude) : "none")} accelScale={(control ? VuNum(control.accelScale) : "none")} survival={GameModeUtils.RequiresPower()} ");
        line.Append($"overdriveSetting={VuNum(VehicleUpgradeSettings.OverdriveSeconds)} overdriveCooldownSetting={VuNum(VehicleUpgradeSettings.OverdriveCooldownSeconds)} ventCooldownSetting={VuNum(VehicleUpgradeSettings.VentCooldownSeconds)} speedBonus={VuNum(VehicleUpgradeSettings.EngineerSpeedBonus)} ");
        line.Append($"{EngineerPdaTab.DebugSummary()} ");
        line.Append($"t={VuSyncedTime()}");
        return line.ToString();
    }

    private IEnumerator VuEngTrack(EngineerConsole console, float seconds)
    {
        vuEngTracking = true;
        try
        {
            float end = Time.time + seconds;
            SubRoot cyclops = console.SubRoot;
            SubControl control = cyclops.GetComponent<SubControl>();
            Rigidbody body = cyclops.GetComponent<Rigidbody>();
            while (Time.time < end && cyclops)
            {
                NitroxId id = console.CyclopsId;
                CyclopsMotorMode motor = control ? control.cyclopsMotorMode : null;
                bool flank = motor && motor.cyclopsMotorMode == CyclopsMotorMode.CyclopsMotorModes.Flank;
                EngineReading engine = console.ReadEngine();
                Log.Info($"{VU}vueng: track speed={(body ? VuNum(body.velocity.magnitude) : "none")} factor={VuNum(EngineerState.SpeedFactor(cyclops, flank))} applied={VuNum(EngineerState.DebugAppliedAcceleration)} overdrive={VuNum(EngineerState.OverdriveRemaining(id))} mode={(motor ? motor.cyclopsMotorMode.ToString() : "none")} throttle={(control && control.appliedThrottle)} heat={engine.Heat} heatKnown={engine.Known} t={VuSyncedTime()}");
                yield return new WaitForSeconds(0.5f);
            }
            Log.Info($"{VU}vueng: track done");
        }
        finally
        {
            vuEngTracking = false;
        }
    }
}
#endif
