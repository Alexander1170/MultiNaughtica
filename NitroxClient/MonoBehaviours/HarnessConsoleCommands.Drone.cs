#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Drone;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of feature group 3, station 4, the Cyclops Drone Bay and the Scout Drone (docs/coop/VEHICLE_UPGRADES.md, scout_drone_design.md). Each logs <c>[SyncHarness] vudrone: key=value</c>
///     lines. A chat command cannot be typed into the client whose console is open, so the script types everything into Alice's chat as <c>/console Bob vudrone ...</c> (the server's <c>/console</c> passes the command
///     and only TWO words after it). <c>vudrone [state|give|load|fly|exit|nudge dx,dy,dz|ray|track seconds|charge v|hurt v]</c>: <c>state</c> one line with the bay and the drone as this client sees them; <c>give</c> puts
///     a Scout Drone into the inventory; <c>load</c> and <c>fly</c> as the hand does at the console (<c>load</c> is the console with an empty bay, <c>fly</c> with a drone in the dock); <c>exit</c> gives the control up;
///     <c>nudge</c> moves the console and the dock in the hull's frame; <c>ray</c> casts rays outward and upward from the dock's place and logs where the hull's skin is (hull-local), to fit the dock to it;
///     <c>track</c> logs the drone's speed, its distance, the static and the black fade, the battery and the health twice a second; <c>charge</c> sets the battery, <c>hurt</c> takes health off the drone.
/// </summary>
public partial class HarnessConsoleCommands
{
    private bool vuDroneTracking;

    private static DroneBay DroneBayOfPlayer()
    {
        if (Player.main && Player.main.currentSub)
        {
            DroneBay own = Player.main.currentSub.GetComponent<DroneBay>();
            if (own)
            {
                return own;
            }
        }
        DroneBay nearest = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (DroneBay candidate in FindObjectsOfType<DroneBay>())
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

    private void LiveCommand_vudrone(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        if (what == "give")
        {
            CraftData.AddToInventory(DroneStation.DroneTech);
            Log.Info($"{VU}vudrone: give tech={DroneStation.DroneTech} count={(Inventory.main != null ? Inventory.main.container.GetCount(DroneStation.DroneTech) : -1)}");
            return;
        }
        DroneBay bay = DroneBayOfPlayer();
        if (!bay)
        {
            Log.Warn($"{VU}vudrone: no Cyclops with the drone bay component");
            return;
        }
        switch (what)
        {
            case "load":
                bay.UseConsole();
                break;
            case "fly":
                bay.Enter();
                break;
            case "exit":
                bay.Exit(true);
                break;
            case "nudge":
                {
                    string[] parts = LiveArgString(n, 1, "0,0,0").Split(',');
                    float Part(int i) => parts.Length > i && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
                    DroneBay.DebugNudge = new Vector3(Part(0), Part(1), Part(2));
                    Log.Info($"{VU}vudrone: nudge={VuNum(DroneBay.DebugNudge.x)},{VuNum(DroneBay.DebugNudge.y)},{VuNum(DroneBay.DebugNudge.z)}");
                }
                break;
            case "ray":
                LogRays(bay);
                return;
            case "track":
                if (!vuDroneTracking)
                {
                    StartCoroutine(VuDroneTrack(bay, LiveArgFloat(n, 1, 20f)));
                }
                else
                {
                    Log.Warn($"{VU}vudrone: track is already running");
                }
                return;
            case "charge":
                if (bay.Drone && bay.Drone.Camera && bay.Drone.Camera.energyMixin)
                {
                    EnergyMixin energy = bay.Drone.Camera.energyMixin;
                    float wanted = Mathf.Clamp(LiveArgFloat(n, 1, 100f), 0f, energy.capacity);
                    // ConsumeEnergy does nothing in creative mode (the probe runs in it): the battery is moved by the difference
                    energy.ModifyCharge(wanted - energy.charge);
                    Log.Info($"{VU}vudrone: charge now={VuNum(energy.charge)}/{VuNum(energy.capacity)}");
                }
                return;
            case "mark":
                {
                    // the mark key (vudrone mark) and its second key (vudrone mark other), as the operator would press them; "mark creature" and "mark ground" turn the drone to the nearest creature or to the ground in
                    // the same call first (two typed commands are 5 to 8 s apart, and a Stalker moves)
                    string markWord = LiveArgString(n, 1, "");
                    string aimed = "none";
                    if ((markWord == "creature" || markWord == "ground") && bay.Drone && bay.Drone.Camera)
                    {
                        aimed = VuDroneAim(bay.Drone.Camera, markWord).Replace(' ', '_');
                    }
                    NavMark placed = DroneMarker.TryMark(bay, markWord == "other", out string markMessage);
                    Log.Info($"{VU}vudrone: mark placed={(placed != null ? "yes" : "no")} kind={(placed != null ? placed.Kind : -1)} label={(placed != null ? placed.Label.Replace(' ', '_') : "none")} message={(markMessage ?? "none").Replace(' ', '_')} aimed={aimed}");
                }
                return;
            case "rawmark":
                {
                    // a mark of that kind (3 resource, 4 threat) from this client whether or not it flies the drone: what the server does with a mark that is not the operator's
                    byte kind = (byte)Mathf.Clamp((int)LiveArgFloat(n, 1, 3f), 0, CyclopsNavRules.MARK_KIND_COUNT - 1);
                    Vector3 where = bay.SubRoot.transform.position + bay.SubRoot.transform.forward * 30f;
                    NavMark placed = NavMarks.Place(bay.CyclopsId, where, kind, "Raw");
                    Log.Info($"{VU}vudrone: rawmark kind={kind} placed={(placed != null ? "yes" : "no")}");
                }
                return;
            case "zap":
                {
                    bool sent = DroneZap.TryRequest(bay, out string zapMessage);
                    Log.Info($"{VU}vudrone: zap sent={sent} message={(zapMessage ?? "none").Replace(' ', '_')}");
                }
                return;
            case "aim":
                {
                    // the drone turns to the nearest creature (vudrone aim creature) or to the ground (vudrone aim ground), so that the mark key has something to meet
                    MapRoomCamera aimCamera = bay.Drone ? bay.Drone.Camera : null;
                    if (!aimCamera)
                    {
                        Log.Info($"{VU}vudrone: aim refused (no drone)");
                        return;
                    }
                    Log.Info($"{VU}vudrone: {VuDroneAim(aimCamera, LiveArgString(n, 1, "creature"))}");
                }
                return;
            case "place":
            case "placein":
                {
                    // the drone goes to dx,dy,dz from the middle of its dock (world axes), at rest: for the leash and the range, which a probe cannot fly to
                    string[] parts = LiveArgString(n, 1, "0,0,0").Split(',');
                    float Part(int i) => parts.Length > i && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
                    if (bay.Drone && bay.Drone.Camera && bay.Drone.Camera.rigidBody && !bay.IsDocked)
                    {
                        MapRoomCamera placedCamera = bay.Drone.Camera;
                        Vector3 offset = new(Part(0), Part(1), Part(2));
                        placedCamera.transform.position = bay.DockPosition + offset;
                        placedCamera.rigidBody.velocity = Vector3.zero;
                        // facing outward (vudrone place) or back at the dock (vudrone placein): so that W flies away from it or toward it
                        if (offset.sqrMagnitude > 1f)
                        {
                            placedCamera.transform.rotation = Quaternion.LookRotation(what == "placein" ? -offset : offset);
                        }
                        Log.Info($"{VU}vudrone: place done distance={VuNum(Vector3.Distance(placedCamera.transform.position, bay.DockPosition))}");
                    }
                    else
                    {
                        Log.Info($"{VU}vudrone: place refused (no drone, or it sits in its dock)");
                    }
                }
                return;
            case "hurt":
                if (bay.Drone && bay.Drone.Camera && bay.Drone.Camera.liveMixin)
                {
                    bay.Drone.Camera.liveMixin.TakeDamage(LiveArgFloat(n, 1, 10f), bay.Drone.transform.position, DamageType.Normal);
                    Log.Info($"{VU}vudrone: health now={VuNum(bay.Drone.Camera.liveMixin.health)}/{VuNum(bay.Drone.Camera.liveMixin.maxHealth)}");
                }
                return;
        }
        Log.Info(DroneState(bay));
    }

    /// <summary>Turns the drone's camera to the ground ("ground") or to the nearest creature (anything else); the text says what it did.</summary>
    private static string VuDroneAim(MapRoomCamera camera, string what)
    {
        if (what == "ground")
        {
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return "aim ground";
        }
        if (VuNearestCreature(camera.transform.position, out Creature near, out float distance))
        {
            camera.transform.rotation = Quaternion.LookRotation(near.transform.position - camera.transform.position);
            return $"aim creature={near.name.Replace(' ', '_')} distance={VuNum(distance)}";
        }
        return "aim refused (no creature within 150 m)";
    }

    /// <summary>The nearest live creature within 150 m of a place (the probe spawns one near the drone and aims at it).</summary>
    private static bool VuNearestCreature(Vector3 from, out Creature nearest, out float distance)
    {
        nearest = null;
        distance = float.MaxValue;
        foreach (Creature creature in FindObjectsOfType<Creature>())
        {
            if (!creature || !creature.isActiveAndEnabled)
            {
                continue;
            }
            float d = Vector3.Distance(from, creature.transform.position);
            if (d < distance && d <= 150f)
            {
                distance = d;
                nearest = creature;
            }
        }
        return nearest;
    }

    private static string DroneState(DroneBay bay)
    {
        ScoutDrone drone = bay.Drone;
        MapRoomCamera camera = drone ? drone.Camera : null;
        StringBuilder line = new();
        line.Append($"{VU}vudrone: bay={(bay ? "yes" : "no")} available={DroneStation.IsAvailable(bay.SubRoot)} dock={(bay.Dock ? "yes" : "no")} docked={bay.IsDocked} operating={bay.IsOperating} drone={(drone ? "yes" : "none")}");
        if (camera)
        {
            EnergyMixin energy = camera.energyMixin;
            float distance = Vector3.Distance(camera.transform.position, bay.DockPosition);
            line.Append($" battery={VuNum(energy ? energy.charge : -1f)}/{VuNum(energy ? energy.capacity : -1f)} health={VuNum(camera.liveMixin ? camera.liveMixin.health : -1f)}/{VuNum(camera.liveMixin ? camera.liveMixin.maxHealth : -1f)}");
            line.Append($" distance={VuNum(distance)} speed={VuNum(camera.rigidBody ? camera.rigidBody.velocity.magnitude : -1f)} drag={VuNum(camera.rigidBody ? camera.rigidBody.drag : -1f)} kinematic={(camera.rigidBody ? camera.rigidBody.isKinematic : false)}");
            line.Append($" canControl={camera.CanBeControlled(null)} local={drone.LocalOperator} parent={(camera.transform.parent ? camera.transform.parent.name : "none")}");
        }
        line.Append($" items={(Inventory.main != null ? Inventory.main.container.GetCount(DroneStation.DroneTech) : -1)} range={VuNum(VehicleUpgradeSettings.DroneRange)} speedFactor={VuNum(VehicleUpgradeSettings.DroneSpeedFactor)}");
        if (camera)
        {
            Vector3 at = camera.transform.position;
            line.Append($" pos={VuNum(at.x)},{VuNum(at.y)},{VuNum(at.z)}");
        }
        if (camera && VuNearestCreature(camera.transform.position, out Creature nearest, out float nearestDistance))
        {
            line.Append($" nearest={nearest.name.Replace(' ', '_')} nearestDistance={VuNum(nearestDistance)}");
        }
        // The marks of the drone: how many, how many have a ping on this client's HUD
        int droneMarks = 0;
        int droneHud = 0;
        List<string> markItems = [];
        foreach (NavMark mark in NavMarks.MarksOf(bay.CyclopsId))
        {
            if (!CyclopsNavRules.IsDroneKind(mark.Kind))
            {
                continue;
            }
            droneMarks++;
            bool hud = mark.Ping && mark.Ping.visible;
            droneHud += hud ? 1 : 0;
            markItems.Add($"{mark.Label.Replace(' ', '_')}/{mark.Kind}/{(hud ? "hud" : "nohud")}/{mark.OwnerName}");
        }
        line.Append($" droneMarks={droneMarks} droneHud={droneHud} markList=[{string.Join(",", markItems)}]");
        // What the server says about the bay and the seat, and who holds what on this client (the live check compares the clients)
        NitroxId cyclopsId = bay.CyclopsId;
        string serverBay = "none";
        if (DroneBayState.TryGetLatest(cyclopsId, out CyclopsDroneState bayState))
        {
            serverBay = bayState.DroneId.HasValue ? (bayState.Docked ? "docked" : "out") : "empty";
        }
        string seatName = DroneSeats.TryGetOperator(cyclopsId, out DroneSeats.Seat seat) ? seat.Name : "none";
        bool lockedDrone = drone && drone.TryGetNitroxId(out NitroxId droneId) && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(droneId);
        line.Append($" serverBay={serverBay} seat={seatName} droneLock={lockedDrone} droneLockType={(drone ? VuLock(drone) : "none")} recharger={bay.IsRechargeAuthority} t={VuSyncedTime()}");
        return line.ToString();
    }

    /// <summary>Rays from the dock's place: outward (port, +x of the hull), up, and down, with the hit point in the hull's frame, to find the skin of the hull.</summary>
    private void LogRays(DroneBay bay)
    {
        Transform hull = bay.SubRoot.transform;
        (float x, float y, float z) = DroneLayout.DockCenter;
        Vector3 inside = hull.TransformPoint(new Vector3(1.0f, y, z));
        foreach ((string name, Vector3 local) in new[] { ("port", Vector3.right), ("up", Vector3.up), ("down", Vector3.down), ("port-up", new Vector3(1f, 1f, 0f).normalized), ("port-down", new Vector3(1f, -1f, 0f).normalized) })
        {
            Vector3 direction = hull.TransformDirection(local);
            RaycastHit[] hits = Physics.RaycastAll(inside, direction, 12f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            StringBuilder line = new($"{VU}vudrone: ray {name} from={VuNum(hull.InverseTransformPoint(inside).x)},{VuNum(hull.InverseTransformPoint(inside).y)},{VuNum(hull.InverseTransformPoint(inside).z)} hits={hits.Length}");
            for (int i = 0; i < hits.Length && i < 4; i++)
            {
                Vector3 p = hull.InverseTransformPoint(hits[i].point);
                line.Append($" [{i}] {hits[i].collider.name} d={VuNum(hits[i].distance)} at={VuNum(p.x)},{VuNum(p.y)},{VuNum(p.z)}");
            }
            Log.Info(line.ToString());
        }
    }

    private IEnumerator VuDroneTrack(DroneBay bay, float seconds)
    {
        vuDroneTracking = true;
        try
        {
            float end = Time.time + seconds;
            while (Time.time < end && bay)
            {
                MapRoomCamera camera = bay.Drone ? bay.Drone.Camera : null;
                if (camera)
                {
                    float distance = Vector3.Distance(camera.transform.position, bay.DockPosition);
                    float range = VehicleUpgradeSettings.DroneRange;
                    Log.Info($"{VU}vudrone: track speed={VuNum(camera.rigidBody ? camera.rigidBody.velocity.magnitude : -1f)} distance={VuNum(distance)} noise={VuNum(ScoutDroneRules.Noise(distance, range, VehicleUpgradeSettings.DroneStaticStart))} fader={VuNum(ScoutDroneRules.Fader(distance, range))} battery={VuNum(camera.energyMixin ? camera.energyMixin.charge : -1f)} health={VuNum(camera.liveMixin ? camera.liveMixin.health : -1f)} docked={bay.IsDocked} t={VuSyncedTime()}");
                }
                yield return new WaitForSeconds(0.5f);
            }
            Log.Info($"{VU}vudrone: track done");
        }
        finally
        {
            vuDroneTracking = false;
        }
    }
}
#endif
