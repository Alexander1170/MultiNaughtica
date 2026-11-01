using System.Collections;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Packets.Processors.Core;
using NitroxClient.MonoBehaviours;
using UnityEngine;
using UWE;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness: puts the local player into a vehicle on request of the server (/warpinto). Seamoth/Exosuit: the
///     player is teleported next to the vehicle and the vehicle is hand-clicked, which goes through Nitrox's normal
///     Vehicle_OnHandClick_Patch lock request before the game's EnterVehicle. Cyclops: the player is placed behind the helm
///     and the sub is set as current sub (same calls as the teleport-into-sub processor); with <see cref="DebugWarpInto.Pilot" />
///     the piloting chair is hand-clicked afterwards. Only acts in Debug builds.
/// </summary>
internal sealed class DebugWarpIntoProcessor : IClientPacketProcessor<DebugWarpInto>
{
    public Task Process(ClientProcessorContext context, DebugWarpInto packet)
    {
#if DEBUG
        if (!NitroxEntity.TryGetObjectFrom(packet.VehicleId, out GameObject target))
        {
            Log.Error($"[SyncHarness] warpinto: no object with id {packet.VehicleId} on this client");
            return Task.CompletedTask;
        }
        CoroutineHost.StartCoroutine(WarpAsync(target, packet.Pilot));
#endif
        return Task.CompletedTask;
    }

#if DEBUG
    /// <summary>
    ///     A Cyclops does not move with the engine off. Presses the start button like a player does (<see cref="CyclopsEngineChangeState.OnClick" />: needs the player to be in that
    ///     sub, broadcasts the change to motor, HUD and <see cref="SubControl" />, and Nitrox's patch on it tells the other players), then waits for the engine to be on (the start takes about 4.5 s).
    ///     Every missing piece is an error in the log, never a silent skip: the runner fails a drive run whose log has no "engine on: True" line.
    /// </summary>
    private static IEnumerator StartEngine(SubRoot subRoot)
    {
        // OnClick toggles by the motor's state, not by the button's changeToEngineState, so either button does
        CyclopsEngineChangeState[] buttons = subRoot.GetComponentsInChildren<CyclopsEngineChangeState>(true);
        CyclopsEngineChangeState[] everywhere = UnityEngine.Object.FindObjectsOfType<CyclopsEngineChangeState>();
        Log.Info($"[SyncHarness] cyclops engine: {buttons.Length} engine buttons under the Cyclops, {everywhere.Length} in the scene");
        CyclopsEngineChangeState startButton = null;
        foreach (CyclopsEngineChangeState button in buttons)
        {
            if (button.changeToEngineState || !startButton)
            {
                startButton = button;
            }
        }
        if (!startButton)
        {
            foreach (CyclopsEngineChangeState button in everywhere)
            {
                if (button.subRoot == subRoot)
                {
                    startButton = button;
                    break;
                }
            }
        }
        CyclopsMotorMode motorMode = startButton ? startButton.motorMode : null;
        if (!motorMode)
        {
            // Vanilla reaches the motor through serialized references (SubControl.cyclopsMotorMode), not through a component on the root
            SubControl subControl = subRoot.GetComponentInChildren<SubControl>(true);
            motorMode = subControl ? subControl.cyclopsMotorMode : null;
            Log.Warn($"[SyncHarness] cyclops engine: no usable engine button (found {(startButton ? "one without motor" : "none")}), motor via SubControl: {(motorMode ? "found" : "missing")}");
        }
        if (!motorMode)
        {
            Log.Error("[SyncHarness] cyclops engine: no CyclopsMotorMode reachable from the Cyclops");
            yield break;
        }
        if (Player.main.currentSub != subRoot)
        {
            Log.Error($"[SyncHarness] cyclops engine: the player is not in the Cyclops (current sub {(Player.main.currentSub ? Player.main.currentSub.name : "none")}), the button would do nothing");
            yield break;
        }

        if (!motorMode.engineOn)
        {
            if (startButton)
            {
                startButton.OnClick();
            }
            else
            {
                // what the button does at the end of OnClick
                subRoot.BroadcastMessage("InvokeChangeEngineState", true, SendMessageOptions.DontRequireReceiver);
            }
        }
        float waited = 0f;
        while (!motorMode.engineOn && waited < 12f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (motorMode.engineOn)
        {
            Log.Info($"[SyncHarness] cyclops engine on: True after {waited:F1} s, piloting: {Player.main.isPiloting}");
        }
        else
        {
            Log.Error($"[SyncHarness] cyclops engine on: False, still off {waited:F1} s after the start button was pressed");
        }
    }

    private static IEnumerator WarpAsync(GameObject target, bool pilot)
    {
        Player player = Player.main;
        if (!player)
        {
            yield break;
        }

        if (target.TryGetComponent(out SubRoot subRoot) && subRoot.isCyclops)
        {
            PilotingChair chair = subRoot.GetComponentInChildren<PilotingChair>(true);
            // The chair stands at the front end (local z about -25), the hull reaches back from there along +z. Not along the chair's own axes (its forward points up, which put the
            // player 0.8 m into the floor, he fell out of the hull): 2 m behind the chair and 1.2 m above it, gravity lets him land on the floor.
            Vector3 inside = chair ? chair.transform.position + subRoot.transform.forward * 2f + Vector3.up * 1.2f : subRoot.transform.position;
            player.OnPlayerPositionCheat();
            player.SetPosition(inside);
            player.SetCurrentSub(subRoot, true);
            Log.Info($"[SyncHarness] warped into Cyclops at {inside} (pilot: {pilot})");
            if (pilot && chair)
            {
                yield return new WaitForSeconds(0.5f);
                chair.OnHandClick(player.guiHand);
                yield return StartEngine(subRoot);
            }
            yield break;
        }

        if (target.TryGetComponent(out Vehicle vehicle))
        {
            player.OnPlayerPositionCheat();
            player.SetPosition(vehicle.transform.position + vehicle.transform.right * 2f);
            yield return null;
            vehicle.OnHandClick(player.guiHand);
            Log.Info($"[SyncHarness] hand-clicked vehicle {vehicle.name} to enter it");
            yield break;
        }

        Log.Error($"[SyncHarness] warpinto: {target.name} is not a vehicle or Cyclops");
    }
#endif
}
