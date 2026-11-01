using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The Scout Drone (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4): the game's camera drone (<c>MapRoomCamera</c>) of a prefab clone with its own TechType. This component is on the clone, so every client
/// that spawns the item by its class id has it, and it is what the patches of the station look for: a camera without it is a scanner room's camera and is left alone. It changes three things about the game's drone:
/// its thrust (a multiple of the Seamoth's speed), its health and its impacts (fragile, but not made of glass), and it is the thing that the bay's console flies. The drone is flown by one client at a time, the
/// operator's (<see cref="LocalOperator" />); on every other client the copy is inert.
/// </summary>
public class ScoutDrone : MonoBehaviour
{
    /// <summary>The speed (m/s) above which a collision hurts the drone (the camera drone's prefab has 1.6: at 22 m/s every touch of a wall would take it apart).</summary>
    public const float SELF_DAMAGE_SPEED = 12f;

    /// <summary>The share of the damage that a collision does to the drone itself (the prefab has 0.5).</summary>
    public const float MIRRORED_SELF_DAMAGE = 0.1f;

    /// <summary>The first impacts of a drone that are written to the log (they show whether it collides at all, with what, and how fast).</summary>
    private const int IMPACTS_LOGGED = 24;

    private MapRoomCamera droneCamera;
    private bool started;
    private bool healthApplied;
    private EcoTarget creatureTarget;
    private int impactsLogged;
    private bool bodyReported;
    private float nextBodyCheck;
    private int stopsLogged;
    private int probesLogged;
    private float nextProbe;
    private readonly List<Renderer> hiddenRenderers = [];

    /// <summary>How far in front of itself the drone keeps from a wall that it is stopped by (metres).</summary>
    private const float WALL_SKIN = 0.08f;

    private const int STOPS_LOGGED = 24;
    private const int PROBES_LOGGED = 12;
    private const float PROBE_SECONDS = 3f;
    private static readonly RaycastHit[] wallHits = new RaycastHit[24];
    private static readonly Collider[] probeColliders = new Collider[24];
    private static int solidMask = -1;

    /// <summary>The camera drone of the game that this is.</summary>
    public MapRoomCamera Camera
    {
        get
        {
            if (!droneCamera)
            {
                droneCamera = GetComponent<MapRoomCamera>();
            }
            return droneCamera;
        }
    }

    /// <summary>This client is the operator of the drone: it may be controlled here (the game's <c>CanBeControlled</c> is false for a scout drone otherwise, so that no scanner room screen can fly it).</summary>
    public bool LocalOperator { get; set; }

    /// <summary>The bay whose dock the drone belongs to, as far as this client knows (the leash measures from it).</summary>
    public DroneBay Bay { get; set; }

    /// <summary>The scout drone of a camera, if it is one.</summary>
    public static bool Of(MapRoomCamera camera, out ScoutDrone drone)
    {
        drone = camera ? camera.GetComponent<ScoutDrone>() : null;
        return drone;
    }

    private void Start()
    {
        started = true;
        ApplyHealth();
        ApplyImpacts();
        // The game's drone is a shiny thing that Stalkers carry off (its EcoTarget is type Shiny); a drone that the operator flies cannot be carried off (its lock), so the target would only make them try again and
        // again. The creatures that matter hunt it through the Shark target that the bay adds while it flies.
        MapRoomCamera camera = Camera;
        if (camera && camera.shinyTarget)
        {
            camera.shinyTarget.enabled = false;
        }
        // What hostile creatures hunt in a Seamoth or a Prawn Suit is an eco target of the Shark type (the fork's remote players use the same); a drone in its dock is not hunted, the way a docked vehicle is not
        creatureTarget = gameObject.AddComponent<EcoTarget>();
        creatureTarget.SetTargetType(EcoTargetType.Shark);
        creatureTarget.enabled = false;
    }

    /// <summary>The drone's own Start has run (the game's dock lets go of a drone in the Start of its camera, which must not be taken for the player's own pickup).</summary>
    public bool HasStarted => started;

    /// <summary>The game's dock holds the drone (docked in the bay, or in a scanner room's dock, which a scout drone is kept out of).</summary>
    public bool IsDocked => Camera && Camera.dockingPoint;

    private void Update()
    {
        MapRoomCamera camera = Camera;
        if (!camera)
        {
            return;
        }
        bool docked = IsDocked;
        // In the dock the hull carries the drone: kinematic on every client (the copies on the other clients are made dynamic by the movement replicator, which places nothing while the drone is docked)
        if (docked && camera.rigidBody && !camera.rigidBody.isKinematic)
        {
            camera.rigidBody.isKinematic = true;
        }
        bool hunted = ScoutDroneRules.IsHunted(docked);
        if (creatureTarget && creatureTarget.enabled != hunted)
        {
            creatureTarget.enabled = hunted;
        }
        if (!docked && LocalOperator && Time.time >= nextBodyCheck)
        {
            // the operator's client moves the drone through physics: whatever made it kinematic or switched its collision off is undone (twice a second is soon enough for that)
            nextBodyCheck = Time.time + 0.5f;
            ConfigureFlightBody();
        }
    }

    /// <summary>
    /// The body of a drone that this client flies: a rigidbody that physics moves (not kinematic: the game's dock makes it kinematic, and a loose item's treatment may do it again), that detects collisions, with the
    /// collider on, and with continuous collision detection against moving bodies as well as static ones (<see cref="CollisionDetectionMode.ContinuousDynamic" />). The prefab has plain continuous detection, which sweeps
    /// against static colliders only; a base carries a rigidbody of its own, and a drone at twice a Seamoth's speed crosses a thin wall in a step. What it had to change is logged, once per flight.
    /// </summary>
    public void ConfigureFlightBody()
    {
        MapRoomCamera camera = Camera;
        Rigidbody body = camera ? camera.rigidBody : null;
        if (!body || IsDocked || (camera.pickupAble && camera.pickupAble.attached))
        {
            return;
        }
        List<string> changes = [];
        if (body.isKinematic)
        {
            body.isKinematic = false;
            changes.Add("it was kinematic");
        }
        if (body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
        {
            changes.Add($"detection was {body.collisionDetectionMode}");
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        if (!body.detectCollisions)
        {
            body.detectCollisions = true;
            changes.Add("it did not detect collisions");
        }
        int solid = 0;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger)
            {
                continue;
            }
            solid++;
            if (!collider.enabled)
            {
                collider.enabled = true;
                changes.Add($"collider {collider.name} was off");
            }
        }
        if (!bodyReported || changes.Count > 0)
        {
            bodyReported = true;
            Log.Info($"[VehicleUpgrades] scout drone: flight body: {solid} solid colliders, layer {gameObject.layer}, detection {body.collisionDetectionMode}, mass {body.mass:F1}, drag {body.drag:F2}{(changes.Count > 0 ? $"; changed: {string.Join(", ", changes)}" : "; nothing to change")}");
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (impactsLogged >= IMPACTS_LOGGED)
        {
            return;
        }
        impactsLogged++;
        Collider other = collision.collider;
        Log.Info($"[VehicleUpgrades] scout drone: hit {(other ? other.name : "?")} (layer {(other ? other.gameObject.layer : -1)}, root {(other ? other.transform.root.name : "?")}) at {collision.relativeVelocity.magnitude:F1} m/s");
    }

    /// <summary>The health of the server's <c>DroneHealth</c>, in a copy of the prefab's data (the data is shared by every instance of the prefab).</summary>
    public void ApplyHealth()
    {
        LiveMixin live = Camera ? Camera.liveMixin : null;
        if (healthApplied || !live || !live.data)
        {
            return;
        }
        float oldMax = live.maxHealth;
        float fraction = oldMax > 0f ? Mathf.Clamp01(live.health / oldMax) : 1f;
        LiveMixinData copy = Instantiate(live.data);
        copy.maxHealth = VehicleUpgradeSettings.DroneHealth;
        live.data = copy;
        live.health = copy.maxHealth * fraction;
        healthApplied = true;
    }

    private void ApplyImpacts()
    {
        DealDamageOnImpact impact = GetComponent<DealDamageOnImpact>();
        if (impact)
        {
            impact.speedMinimumForSelfDamage = SELF_DAMAGE_SPEED;
            impact.mirroredSelfDamageFraction = MIRRORED_SELF_DAMAGE;
        }
    }

    /// <summary>Where the drone's signal comes from: the dock of its bay (the game measures the distance from the screen it is flown from, which sits there).</summary>
    public Vector3 HomePosition => Bay ? Bay.DockPosition : transform.position;

    /// <summary>Whether the battery has what it takes to fly (the game's rule: some charge, or a mode that needs no power).</summary>
    public bool HasCharge => Camera && Camera.energyMixin && (Camera.energyMixin.charge > 0f || !GameModeUtils.RequiresPower());

    /// <summary>
    /// The game's physics step of the drone with the mod's changes (<c>MapRoomCamera.FixedUpdate</c>, whose body this is): the thrust of <see cref="ScoutDroneRules.ThrustAcceleration" /> instead of the literal 20,
    /// and past the range the leash takes the outward part out of the wish, so that the drone can only come back. Only the operator's client controls the drone.
    /// </summary>
    public void PhysicsStep()
    {
        MapRoomCamera camera = Camera;
        if (!camera || !camera.rigidBody || !camera.IsControlled())
        {
            return;
        }
        // The game flies the drone in water only
        if (camera.transform.position.y >= camera.worldForces.waterDepth)
        {
            return;
        }
        Vector3 wish = camera.transform.rotation * camera.wishDir;
        Vector3 away = camera.transform.position - HomePosition;
        float distance = away.magnitude;
        if (distance > 0.001f)
        {
            Vector3 outward = away / distance;
            (float x, float y, float z) = ScoutDroneRules.Leash(distance, VehicleUpgradeSettings.DroneRange, outward.x, outward.y, outward.z, wish.x, wish.y, wish.z);
            wish = new Vector3(x, y, z);
        }
        float thrust = ScoutDroneRules.ThrustAcceleration(VehicleUpgradeSettings.DroneSpeedFactor, camera.rigidBody.drag, Time.fixedDeltaTime);
        camera.rigidBody.AddForce(thrust * wish, ForceMode.Acceleration);
        camera.StabilizeRoll();
        KeepOutOfWalls(camera.rigidBody);
        ProbeSurroundings(camera.rigidBody);
    }

    /// <summary>What the drone cannot fly into: the terrain, the bases and their clip proxies, the hulls and the vehicles (not the player, the triggers, loose items or creatures).</summary>
    private static int SolidMask
    {
        get
        {
            if (solidMask < 0)
            {
                solidMask = LayerMask.GetMask("Default", "BuiltEntities", "Vehicle", "TerrainCollider", "SubRigidbodyExclude", "BaseClipProxy");
            }
            return solidMask;
        }
    }

    private float BodyRadius
    {
        get
        {
            SphereCollider sphere = GetComponent<SphereCollider>();
            return sphere ? sphere.radius * Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)) : 0.35f;
        }
    }

    /// <summary>
    /// The wall that stops the drone, by a query and not by the physics engine's contacts: before every step a sphere the size of the drone is swept along its velocity, and when a solid thing lies within the step
    /// the part of the velocity that goes into it is taken out (the rest slides along it), so that the drone ends the step at <see cref="WALL_SKIN" /> from it. Whatever the contacts of the rigidbody do or do not
    /// do (a build without continuous detection saw no impact at all in two minutes of flight), a drone at twice a Seamoth's speed cannot cross a wall or the sea floor.
    /// </summary>
    private void KeepOutOfWalls(Rigidbody body)
    {
        Vector3 velocity = body.velocity;
        float speed = velocity.magnitude;
        if (speed < 0.3f)
        {
            return;
        }
        float step = Time.fixedDeltaTime;
        Vector3 direction = velocity / speed;
        int count = Physics.SphereCastNonAlloc(body.position, BodyRadius * 0.95f, direction, wallHits, speed * step + WALL_SKIN, SolidMask, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = wallHits[i];
            Collider other = hit.collider;
            if (!other || other.attachedRigidbody == body || other.transform.IsChildOf(transform) || hit.distance >= nearestDistance || other.GetComponentInParent<Creature>())
            {
                continue;
            }
            nearest = i;
            nearestDistance = hit.distance;
        }
        if (nearest < 0)
        {
            return;
        }
        RaycastHit wall = wallHits[nearest];
        float into = Vector3.Dot(velocity, wall.normal);
        if (into >= 0f)
        {
            return;
        }
        Vector3 slide = velocity - wall.normal * into;
        float room = Mathf.Max(0f, wall.distance - WALL_SKIN);
        body.velocity = slide + direction * (room / step);
        if (stopsLogged < STOPS_LOGGED)
        {
            stopsLogged++;
            Collider hitCollider = wall.collider;
            Log.Info($"[VehicleUpgrades] scout drone: stopped by {hitCollider.name} (layer {hitCollider.gameObject.layer}, root {hitCollider.transform.root.name}) {wall.distance:F2} m ahead at {speed:F1} m/s");
        }
    }

    /// <summary>
    /// A line in the log every few seconds of the first flights: how many solid colliders there are within a few metres of the drone and what is under it, so that a drone that flies through something can be told from
    /// terrain whose colliders are not loaded where the drone is.
    /// </summary>
    private void ProbeSurroundings(Rigidbody body)
    {
        if (probesLogged >= PROBES_LOGGED || Time.time < nextProbe)
        {
            return;
        }
        nextProbe = Time.time + PROBE_SECONDS;
        probesLogged++;
        Vector3 here = body.position;
        int near = Physics.OverlapSphereNonAlloc(here, 6f, probeColliders, SolidMask, QueryTriggerInteraction.Ignore);
        string nearest = near > 0 && probeColliders[0] ? $"{probeColliders[0].name} on layer {probeColliders[0].gameObject.layer}, root {probeColliders[0].transform.root.name}" : "none";
        string ground = Physics.Raycast(here, Vector3.down, out RaycastHit below, 80f, SolidMask, QueryTriggerInteraction.Ignore) ? $"{below.distance:F1} m below ({below.collider.name}, layer {below.collider.gameObject.layer})" : "none within 80 m";
        Log.Info($"[VehicleUpgrades] scout drone: probe at {here.ToString("F0")}: {near} solid colliders within 6 m (first: {nearest}); ground {ground}; speed {body.velocity.magnitude:F1} m/s");
    }

    /// <summary>
    /// The drone's own picture is not shown to the one who flies it: the game's camera sits at the drone and the model is behind it, which is as good as hidden, but a camera that is a little off (or ahead of the
    /// drone for a frame at twice the Seamoth's speed) shows the rear and the fins. The meshes are switched off on the operator's client only; the others see the drone.
    /// </summary>
    public void HideOwnModel()
    {
        MapRoomCamera camera = Camera;
        Transform effect = camera && camera.screenEffectModel ? camera.screenEffectModel.transform : null;
        hiddenRenderers.Clear();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            bool mesh = renderer is MeshRenderer || renderer is SkinnedMeshRenderer;
            if (!mesh || !renderer.enabled || (effect && (renderer.transform == effect || renderer.transform.IsChildOf(effect))))
            {
                continue;
            }
            renderer.enabled = false;
            hiddenRenderers.Add(renderer);
        }
        Log.Info($"[VehicleUpgrades] scout drone: its own model is hidden from its operator ({hiddenRenderers.Count} meshes)");
    }

    public void ShowOwnModel()
    {
        foreach (Renderer renderer in hiddenRenderers)
        {
            if (renderer)
            {
                renderer.enabled = true;
            }
        }
        hiddenRenderers.Clear();
    }

    /// <summary>
    /// The dock's recharge, in place of the game's (<c>MapRoomCamera.UpdateEnergyRecharge</c>, which raids the Cyclops' power relay on every client every frame): a fixed rate, paid by nobody (design note, D5).
    /// </summary>
    public void RechargeStep()
    {
        MapRoomCamera camera = Camera;
        if (!camera || !camera.energyMixin)
        {
            return;
        }
        bool charging = false;
        // Exactly one client charges the drone in the dock: the one that simulates the Cyclops (the others would add the same energy to their copies of the battery; the battery's charge travels from the one that does)
        if (camera.dockingPoint && Bay && Bay.Dock == camera.dockingPoint && Bay.IsRechargeAuthority)
        {
            float add = ScoutDroneRules.Recharge(camera.energyMixin.charge, camera.energyMixin.capacity, Time.deltaTime);
            if (add > 0f)
            {
                camera.energyMixin.AddEnergy(add);
                charging = true;
            }
        }
        if (charging)
        {
            camera.chargingSound.Play();
        }
        else
        {
            camera.chargingSound.Stop();
        }
    }

    private void OnDestroy()
    {
        ShowOwnModel();
        if (LocalOperator && started)
        {
            LocalOperator = false;
        }
    }
}
