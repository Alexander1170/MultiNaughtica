#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Gunner;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of feature group 3, station 2, the Cyclops Gunner Station (docs/coop/VEHICLE_UPGRADES.md, a harness script that is not part of this repository). Each logs
///     <c>[SyncHarness] vugun: key=value</c> lines. No chat command can be typed into the client whose console is open (the console takes the input group, and the keys of a typed word reach the game), so the
///     rounds that need the console open run inside one self-driving command (<c>demo</c>) or are driven from the other client. <c>vugun [state|open|close|leave|take|aim yaw,pitch|fire [charge]|power v|cooldown|hand dx,dy,dz|horizon|camdump|demo [charge]]</c> (the server's <c>/console</c> passes the command and only TWO words after it):
///     <c>state</c> one line with the station as this client sees it; <c>open</c> and <c>close</c> as the hand and the exit key do; <c>take</c> sends a seat request whatever the table says (the server's refusal can be
///     seen); <c>leave</c> gives the seat up; <c>aim</c> points the turret (yaw from the bow, positive to starboard, and pitch, positive up); <c>fire</c> releases a charge (0 to 1) as the mouse button does, without
///     holding it for seconds; <c>power</c> sets the Cyclops' energy; <c>cooldown</c> forgets the local cooldown; <c>hand</c> moves the hand target in the hull's frame; <c>horizon</c> prints the scanned limits;
///     <c>camdump</c> the camera root and its parents; <c>demo [charge]</c> opens the console, waits for the seat, aims at the nearest creature (at starboard, 10 degrees down, when there is none; the local dev console
///     also takes <c>demo charge yaw,pitch</c> for a fixed aim), fires, tries a second shot inside the cooldown, takes pictures, looks at the creature and closes it (always).
/// </summary>
public partial class HarnessConsoleCommands
{
    private bool vuGunDemoRunning;

    /// <summary>The console of the Cyclops the player is in, else the nearest real one.</summary>
    private static GunnerConsole GunConsole()
    {
        if (Player.main && Player.main.currentSub)
        {
            GunnerConsole own = Player.main.currentSub.GetComponent<GunnerConsole>();
            if (own)
            {
                return own;
            }
        }
        GunnerConsole nearest = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (GunnerConsole candidate in FindObjectsOfType<GunnerConsole>())
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

    private static bool VuGunPair(NotificationCenter.Notification n, int index, out float first, out float second)
    {
        first = 0f;
        second = 0f;
        string text = LiveArgString(n, index, string.Empty);
        string[] parts = text.Split(',');
        return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out first) && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out second);
    }

    private void LiveCommand_vugun(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        GunnerConsole console = GunConsole();
        if (!console)
        {
            Log.Warn($"{VU}vugun: no Cyclops with the gunner console component");
            return;
        }
        console.TryGetCyclopsIdForDebug(out NitroxId id);
        switch (what)
        {
            case "open":
                console.Enter();
                break;
            case "close":
                console.Close(true, "vugun close");
                break;
            case "leave":
                GunnerSeats.SetBusy(id, false);
                GunnerSeats.Leave(id);
                break;
            case "take":
                // a seat request that ignores the table and the local checks: the server decides (and refuses an occupied desk). The desk is marked busy so that a grant is kept (until "leave"), not handed back as one nobody wants
                GunnerSeats.SetBusy(id, true);
                NitroxServiceLocator.LocateService<IPacketSender>().Send(CyclopsGunnerSeat.Take(id));
                Log.Info($"{VU}vugun: take sent for {VuId(console.gameObject)}");
                break;
            case "aim":
                if (console.Rig && console.Rig.Active && VuGunPair(n, 1, out float yaw, out float pitch))
                {
                    console.Rig.SetAim(yaw, pitch);
                }
                else
                {
                    Log.Warn($"{VU}vugun: aim needs the console open and yaw,pitch");
                }
                break;
            case "fire":
                {
                    float charge = LiveArgFloat(n, 1, 1f);
                    bool sent = console.TryFire(charge, out string why);
                    Log.Info($"{VU}vugun: fire charge={VuNum(charge)} sent={sent} why=\"{why}\"");
                }
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
                    Log.Info($"{VU}vugun: power was={VuNum(now)} wanted={VuNum(wanted)} now={VuNum(relay ? relay.GetPower() : 0f)} survival={GameModeUtils.RequiresPower()}");
                }
                break;
            case "cooldown":
                console.DebugResetCooldown();
                break;
            case "hand":
                {
                    Vector3 nudge = VuNavVector(n, 0f, 0f, 0f);
                    GunnerConsole.DebugHandNudge = nudge;
                }
                break;
            case "horizon":
                Log.Info($"{VU}vugun: horizon {VuGunHorizon(console)}");
                break;
            case "camdump":
                VuGunCamDump();
                break;
            case "demo":
                if (vuGunDemoRunning)
                {
                    Log.Warn($"{VU}vugun: demo is already running");
                }
                else
                {
                    float charge = LiveArgFloat(n, 1, 1f);
                    float demoYaw = 90f;
                    float demoPitch = -10f;
                    // automatic unless a fixed aim is given as the third word (possible from the local dev console only: /console passes two words)
                    bool auto = !VuGunPair(n, 2, out demoYaw, out demoPitch);
                    if (auto)
                    {
                        demoYaw = 90f;
                        demoPitch = -10f;
                    }
                    StartCoroutine(VuGunDemo(console, charge, demoYaw, demoPitch, auto));
                }
                return;
        }
        Log.Info($"{VU}vugun: {VuGunState(console, id)}");
    }

    /// <summary>One line, plain tokens (a value with spaces is quoted, there are no brackets) so that the live script's key=value parser reads it.</summary>
    private static string VuGunState(GunnerConsole console, NitroxId id)
    {
        SubRoot cyclops = console.SubRoot;
        bool canEnter = console.CanEnter(out string why);
        GunnerSeats.TryGetGunner(id, out GunnerSeats.Seat seat);
        PowerRelay relay = cyclops ? cyclops.powerRelay : null;
        TurretCameraRig rig = console.Rig;
        Rigidbody body = cyclops ? cyclops.GetComponent<Rigidbody>() : null;
        bool holdsLock = id != null && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
        float sinceShot = GunnerTurret.LastShotAt > float.NegativeInfinity ? Time.unscaledTime - GunnerTurret.LastShotAt : -1f;
        float sinceRemote = BulletManager.LastRemoteStasisShotAt >= 0f ? Time.unscaledTime - BulletManager.LastRemoteStasisShotAt : -1f;
        int scanned = 0;
        if (console.Horizon != null)
        {
            foreach (float sector in console.Horizon)
            {
                if (!float.IsNaN(sector))
                {
                    scanned++;
                }
            }
        }
        StringBuilder line = new();
        line.Append($"cyclops={VuId(console.gameObject)} module={GunnerStation.HasModule(cyclops)} available={GunnerStation.IsAvailable(cyclops)} canEnter={canEnter} why=\"{why}\" open={console.IsOpen} busy={console.IsBusy} ");
        line.Append($"gunner=\"{(seat.Session.IsPlayer ? seat.Name : "none")}\" gunnerSession=\"{(seat.Session.IsPlayer ? seat.Session.ToString() : "none")}\" seatMine={(id != null && GunnerSeats.IsMine(id))} seatOther={(id != null && GunnerSeats.IsTakenByOther(id))} ");
        line.Append($"rig={(rig && rig.Active)} yaw={(rig ? VuNum(rig.Yaw) : "none")} pitch={(rig ? VuNum(rig.Pitch) : "none")} scanned={scanned} cooldown={VuNum(console.CooldownRemaining)} waiting={console.IsWaitingForShot} ");
        line.Append($"energy={(relay ? VuNum(relay.GetPower()) : "none")} energyMax={(relay ? VuNum(relay.GetMaxPower()) : "none")} survival={GameModeUtils.RequiresPower()} shotEnergy={VuNum(VehicleUpgradeSettings.GunnerShotEnergy)} range={VuNum(VehicleUpgradeSettings.GunnerRange)} cooldownSetting={VuNum(VehicleUpgradeSettings.GunnerCooldownSeconds)} ");
        line.Append($"shots={GunnerTurret.Shots} sinceShot={VuNum(sinceShot)} remoteShots={BulletManager.RemoteStasisShots} remoteHits={BulletManager.RemoteStasisHits} sinceRemoteShot={VuNum(sinceRemote)} simulator={holdsLock} hullKinematic={(body ? body.isKinematic.ToString() : "none")} ");
        line.Append($"target=\"{console.TargetText}\" ");
        Vector3 hullPosition = cyclops ? cyclops.transform.position : Vector3.zero;
        Vector3 starboard = cyclops ? -cyclops.transform.right : Vector3.zero;
        line.Append($"hull={VuNum(hullPosition.x)},{VuNum(hullPosition.y)},{VuNum(hullPosition.z)} starboard={VuNum(starboard.x)},{VuNum(starboard.y)},{VuNum(starboard.z)} ");
        Creature nearest = VuGunNearestCreature(console, out float creatureDistance);
        line.Append(nearest ? $"creature={nearest.name.Replace(' ', '_')} creatureDistance={VuNum(creatureDistance)} creatureKinematic={VuGunKinematic(nearest)}" : "creature=none creatureDistance=-1 creatureKinematic=none");
        int stalkers = 0;
        int frozenStalkers = 0;
        foreach (Creature creature in FindObjectsOfType<Creature>())
        {
            if (creature.isActiveAndEnabled && creature.name.Contains("Stalker"))
            {
                stalkers++;
                Rigidbody stalkerBody = creature.GetComponent<Rigidbody>();
                frozenStalkers += stalkerBody && stalkerBody.isKinematic ? 1 : 0;
            }
        }
        line.Append($" stalkers={stalkers} frozenStalkers={frozenStalkers}");
        return line.ToString();
    }

    /// <summary>
    /// The creature to aim at, with its distance to the turret's mount; null when there is none. A Stalker within 150 m is preferred to anything nearer (the probe spawns one 30 m abeam, and the fish of the shallows
    /// are nearer: the first live run aimed at a Bladderfish at 12 m and never saw the Stalker); with no Stalker it is the nearest creature.
    /// </summary>
    private static Creature VuGunNearestCreature(GunnerConsole console, out float distance)
    {
        distance = -1f;
        (float mx, float my, float mz) = GunnerStationLayout.MountPosition;
        Vector3 mount = console.transform.TransformPoint(new Vector3(mx, my, mz));
        Creature nearest = null;
        float best = float.MaxValue;
        Creature nearestStalker = null;
        float bestStalker = float.MaxValue;
        foreach (Creature creature in FindObjectsOfType<Creature>())
        {
            if (!creature.isActiveAndEnabled)
            {
                continue;
            }
            float d = Vector3.Distance(mount, creature.transform.position);
            if (d < best)
            {
                best = d;
                nearest = creature;
            }
            if (d < bestStalker && d <= 150f && creature.name.Contains("Stalker"))
            {
                bestStalker = d;
                nearestStalker = creature;
            }
        }
        if (nearestStalker)
        {
            distance = bestStalker;
            return nearestStalker;
        }
        distance = nearest ? best : -1f;
        return nearest;
    }

    private static string VuGunKinematic(Creature creature)
    {
        Rigidbody body = creature ? creature.GetComponent<Rigidbody>() : null;
        return body ? body.isKinematic.ToString() : "none";
    }

    /// <summary>The yaw and pitch that point the turret at a world position (the formula of <see cref="GunnerRules.Direction" /> turned round).</summary>
    private static void VuGunAimAt(GunnerConsole console, Vector3 world, out float yaw, out float pitch)
    {
        (float mx, float my, float mz) = GunnerStationLayout.MountPosition;
        Vector3 local = console.transform.InverseTransformPoint(world) - new Vector3(mx, my, mz);
        Vector3 direction = local.normalized;
        yaw = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private static string VuGunHorizon(GunnerConsole console)
    {
        StringBuilder builder = new();
        foreach (float yaw in new[] { -180f, -150f, -120f, -90f, -60f, -30f, 0f, 30f, 60f, 90f, 120f, 150f })
        {
            builder.Append($"{yaw:F0}:{GunnerRules.MinPitch(yaw, console.Horizon):F0}/{GunnerRules.MinPitch(yaw):F0} ");
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>The camera root and its parents, the camera's own pose and components: what the turret camera takes over and puts back.</summary>
    private static void VuGunCamDump()
    {
        if (!SNCameraRoot.main || !MainCamera.camera)
        {
            Log.Warn($"{VU}vugun: camdump: no camera");
            return;
        }
        Transform root = SNCameraRoot.main.transform;
        StringBuilder chain = new();
        for (Transform t = root; t; t = t.parent)
        {
            chain.Append($"{t.name}@{t.localPosition.ToString("F2")}/{t.localEulerAngles.ToString("F1")} ");
        }
        Camera camera = MainCamera.camera;
        Log.Info($"{VU}vugun: camdump root=\"{root.name}\" local=\"{root.localPosition.ToString("F2")}\" world=\"{root.position.ToString("F2")}\" cameraLocal=\"{camera.transform.localPosition.ToString("F2")}\" fov={VuNum(camera.fieldOfView)} near={VuNum(camera.nearClipPlane)} mask={camera.cullingMask} chain=\"{chain.ToString().TrimEnd()}\"");
    }

    private IEnumerator VuGunDemo(GunnerConsole console, float charge, float yaw, float pitch, bool auto)
    {
        vuGunDemoRunning = true;
        try
        {
            console.Enter();
            float until = Time.unscaledTime + 10f;
            while (!console.IsOpen && Time.unscaledTime < until)
            {
                yield return null;
            }
            if (!console.IsOpen)
            {
                Log.Info($"{VU}vugun: demo FAILED the console did not open (canEnter={console.CanEnter(out string why)} why=\"{why}\")");
                yield break;
            }
            Log.Info($"{VU}vugun: demo opened");

            // the hull's horizon is scanned over a few frames
            float scanUntil = Time.unscaledTime + 8f;
            while (console.IsOpen && Time.unscaledTime < scanUntil && !VuGunScanned(console))
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Log.Info($"{VU}vugun: demo horizon {VuGunHorizon(console)}");

            Creature target = null;
            if (auto)
            {
                target = VuGunNearestCreature(console, out float targetDistance);
                if (target)
                {
                    VuGunAimAt(console, target.transform.position, out yaw, out pitch);
                    Log.Info($"{VU}vugun: demo auto-aim at {target.name.Replace(' ', '_')} {VuNum(targetDistance)} m: yaw={VuNum(yaw)} pitch={VuNum(pitch)}");
                }
                else
                {
                    Log.Info($"{VU}vugun: demo auto-aim found no creature: aiming at yaw={VuNum(yaw)} pitch={VuNum(pitch)}");
                }
            }
            console.Rig.SetAim(yaw, pitch);
            yield return new WaitForSecondsRealtime(1.5f);
            Log.Info($"{VU}vugun: demo aim yaw={VuNum(console.Rig.Yaw)} pitch={VuNum(console.Rig.Pitch)} target=\"{console.TargetText}\"");
            yield return VuGunDemoShot(console, "gun-aim");

            if (auto)
            {
                // The Stalkers come to the hull and circle under it, and the turret cannot point under its own hull (the horizon): wait up to 40 s until one is where the turret can point at, then aim at it with the
                // lead that the flight needs and fire at once
                float waitUntil = Time.unscaledTime + 40f;
                float waitFrom = Time.unscaledTime;
                float sphereSpeed = GunnerRules.SphereFlight(VehicleUpgradeSettings.GunnerRange).Speed;
                float reach = VehicleUpgradeSettings.GunnerRange - 5f;
                (float mx, float my, float mz) = GunnerStationLayout.MountPosition;
                bool found = false;
                while (!found && console.IsOpen && Time.unscaledTime < waitUntil)
                {
                    Vector3 mount = console.transform.TransformPoint(new Vector3(mx, my, mz));
                    Creature best = null;
                    float bestDistance = float.MaxValue;
                    foreach (Creature candidate in FindObjectsOfType<Creature>())
                    {
                        if (!candidate.isActiveAndEnabled || !candidate.name.Contains("Stalker"))
                        {
                            continue;
                        }
                        Rigidbody candidateBody = candidate.GetComponent<Rigidbody>();
                        Vector3 candidateVelocity = candidateBody && !candidateBody.isKinematic ? candidateBody.velocity : Vector3.zero;
                        float candidateDistance = Vector3.Distance(mount, candidate.transform.position);
                        Vector3 candidatePoint = candidate.transform.position + candidateVelocity * (candidateDistance / Mathf.Max(1f, sphereSpeed));
                        VuGunAimAt(console, candidatePoint, out float candidateYaw, out float candidatePitch);
                        // a margin of 2 degrees above the horizon's limit, and not so near that the field would hit the Cyclops' own hull
                        if (candidateDistance >= 8f && candidateDistance <= reach && candidatePitch >= GunnerRules.MinPitch(candidateYaw, console.Horizon) + 2f && candidateDistance < bestDistance)
                        {
                            best = candidate;
                            bestDistance = candidateDistance;
                        }
                    }
                    if (best)
                    {
                        target = best;
                        found = true;
                        break;
                    }
                    yield return new WaitForSecondsRealtime(0.2f);
                }
                if (found && target)
                {
                    Rigidbody body = target.GetComponent<Rigidbody>();
                    Vector3 velocity = body && !body.isKinematic ? body.velocity : Vector3.zero;
                    float distanceNow = Vector3.Distance(console.transform.TransformPoint(new Vector3(mx, my, mz)), target.transform.position);
                    VuGunAimAt(console, target.transform.position + velocity * (distanceNow / Mathf.Max(1f, sphereSpeed)), out float freshYaw, out float freshPitch);
                    console.Rig.SetAim(freshYaw, freshPitch);
                    yield return new WaitForSecondsRealtime(0.3f);
                    // the creature moved while the turret turned: once more, and straight to the shot
                    distanceNow = Vector3.Distance(console.transform.TransformPoint(new Vector3(mx, my, mz)), target.transform.position);
                    velocity = body && !body.isKinematic ? body.velocity : Vector3.zero;
                    VuGunAimAt(console, target.transform.position + velocity * (distanceNow / Mathf.Max(1f, sphereSpeed)), out freshYaw, out freshPitch);
                    console.Rig.SetAim(freshYaw, freshPitch);
                    yield return null;
                    Log.Info($"{VU}vugun: demo re-aim at {target.name.Replace(' ', '_')} {VuNum(distanceNow)} m after waiting {VuNum(Time.unscaledTime - waitFrom)} s (range {VuNum(VehicleUpgradeSettings.GunnerRange)} m, creature speed {VuNum(velocity.magnitude)} m/s): wanted yaw={VuNum(freshYaw)} pitch={VuNum(freshPitch)}, the rig took yaw={VuNum(console.Rig.Yaw)} pitch={VuNum(console.Rig.Pitch)}");
                }
                else
                {
                    Log.Info($"{VU}vugun: demo re-aim found no Stalker the turret can point at within {VuNum(Time.unscaledTime - waitFrom)} s: the shot goes where the first aim was");
                }
            }
            int shotsBefore = GunnerTurret.Shots;
            float energyBefore = console.SubRoot.powerRelay ? console.SubRoot.powerRelay.GetPower() : -1f;
            bool sent = console.TryFire(charge, out string fireWhy);
            Log.Info($"{VU}vugun: demo fire charge={VuNum(charge)} sent={sent} why=\"{fireWhy}\"");
            // a second release inside the cooldown is turned down by the client before anything is sent
            bool second = console.TryFire(charge, out string secondWhy);
            Log.Info($"{VU}vugun: demo second sent={second} why=\"{secondWhy}\"");

            float answerUntil = Time.unscaledTime + 6f;
            float waitStart = Time.unscaledTime;
            while (console.IsOpen && Time.unscaledTime < answerUntil && GunnerTurret.Shots == shotsBefore)
            {
                if (!console.IsWaitingForShot && Time.unscaledTime - waitStart > 1f)
                {
                    break;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            float energyAfter = console.SubRoot.powerRelay ? console.SubRoot.powerRelay.GetPower() : -1f;
            Log.Info($"{VU}vugun: demo result shotsBefore={shotsBefore} shotsAfter={GunnerTurret.Shots} energyBefore={VuNum(energyBefore)} energyAfter={VuNum(energyAfter)} survival={GameModeUtils.RequiresPower()} waiting={console.IsWaitingForShot}");
            yield return new WaitForSecondsRealtime(3f);
            if (target)
            {
                StasisSphere fired = GunnerTurret.Sphere;
                Log.Info($"{VU}vugun: demo creature {target.name.Replace(' ', '_')} kinematic={VuGunKinematic(target)} distance={VuNum(Vector3.Distance(target.transform.position, console.transform.position))} at={target.transform.position.ToString("F1")} sphereAt={(fired ? fired.transform.position.ToString("F1") : "none")} distanceToSphere={(fired ? VuNum(Vector3.Distance(target.transform.position, fired.transform.position)) : "none")}");
            }
            yield return VuGunDemoShot(console, "gun-shot");
            Log.Info($"{VU}vugun: demo state {VuGunState(console, VuGunId(console))}");
        }
        finally
        {
            console.Close(true, "vugun demo");
            vuGunDemoRunning = false;
            Log.Info($"{VU}vugun: demo done");
        }
    }

    private static bool VuGunScanned(GunnerConsole console)
    {
        if (console.Horizon == null)
        {
            return false;
        }
        foreach (float sector in console.Horizon)
        {
            if (float.IsNaN(sector))
            {
                return false;
            }
        }
        return true;
    }

    private static NitroxId VuGunId(GunnerConsole console)
    {
        console.TryGetCyclopsIdForDebug(out NitroxId id);
        return id;
    }

    private IEnumerator VuGunDemoShot(GunnerConsole console, string tag)
    {
        if (!console.IsOpen)
        {
            Log.Info($"{VU}vugun: demo shot {tag} skipped: the console closed");
            yield break;
        }
        yield return StartCoroutine(LiveShotRoutine(false, 0f, tag));
    }
}
#endif
