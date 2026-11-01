using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// On the towed vehicle, on the towing player's client only: pulls the vehicle behind the tower with a spring and a damper, and watches over the tow. The cable pulls the towed vehicle only (the tower
/// is slowed by the server's factor instead, see <see cref="VehicleTows.SpeedFactor" />), with an acceleration like the pilot's own thrust and the Prawn Suit's grappling arm (it ignores the mass), and
/// pulls with no force while the cable is slack, so the two hulls are not pushed apart. The numbers are in <see cref="TowRules" />. It ends the tow when the cable gets too long, when a vehicle is
/// destroyed or docked, when a lock moves, when a pilot gets in, or when the hook is gone.
/// </summary>
public class TowLine : MonoBehaviour
{
    private VehicleTows.Tow tow;
    private Component tower;
    private Transform towerRoot;
    private Rigidbody towerBody;
    private Vehicle towed;
    private Rigidbody towedBody;
    private float secondsOver;
    private float secondsFrozen;
    private Vector3 carryVelocity;
    private bool carrying;
    private TowTrail trail;
    private float nextDepthCheck;
    private float nextModuleCheck;
    private float lastDepthWarning;
    private string lastDepthState;
    private float nextLog;

    public void Initialize(VehicleTows.Tow tow, Component tower)
    {
        this.tow = tow;
        this.tower = tower;
        towerRoot = tower.transform;
        towerBody = tower is Vehicle vehicle ? vehicle.useRigidbody : tower.GetComponent<Rigidbody>();
        towed = GetComponent<Vehicle>();
        towedBody = towed ? towed.useRigidbody : null;
    }

    private void FixedUpdate()
    {
        if (tow == null)
        {
            return;
        }
        if (!tower || !towed || !towerRoot)
        {
            VehicleTows.Release(tow, TowReleaseReason.Destroyed);
            return;
        }

        SimulationOwnership ownership = this.Resolve<SimulationOwnership>();
        if (!ownership.HasAnyLockType(tow.TowerId) || !ownership.HasAnyLockType(tow.TowedId))
        {
            VehicleTows.Release(tow, ownership.HasAnyLockType(tow.TowedId) ? TowReleaseReason.LockLost : TowReleaseReason.Entered);
            return;
        }
        if (towed.docked || (tower is Vehicle towerVehicle && towerVehicle.docked) || towed.GetPilotingMode())
        {
            VehicleTows.Release(tow, towed.GetPilotingMode() ? TowReleaseReason.Entered : TowReleaseReason.Docked);
            return;
        }
        if (!VehicleUpgradeSettings.Enabled)
        {
            VehicleTows.Release(tow, TowReleaseReason.Disabled);
            return;
        }
        if (Time.time >= nextModuleCheck)
        {
            // The hook taken out of a Cyclops' upgrade console is not announced to anyone: look once a second (the Seamoth's patch announces it)
            nextModuleCheck = Time.time + 1f;
            VehicleTows.OnModulesChanged(tower);
            if (!VehicleTows.IsOn(tow))
            {
                return;
            }
        }
        if (towed.liveMixin && !towed.liveMixin.IsAlive())
        {
            VehicleTows.Release(tow, TowReleaseReason.Destroyed);
            return;
        }

        if (!TryGetAnchors(out Vector3 towerAnchor, out Vector3 towedAnchor))
        {
            return;
        }
        Vector3 cable = towedAnchor - towerAnchor;
        float length = cable.magnitude;
        float maxLength = VehicleUpgradeSettings.TowMaxDistance;

        secondsOver = length > maxLength ? secondsOver + Time.fixedDeltaTime : 0f;
        if (TowRules.IsTooLong(length, maxLength, secondsOver))
        {
            VehicleTows.Release(tow, TowReleaseReason.TooLong);
            return;
        }

        // A towed vehicle that the game keeps still cannot be pushed by a force. For a vehicle that is grabbed or being teleported nothing can be done, and the tow ends before long; for one that is still because the
        // world is not built around it (it is beyond about 37 m from the camera of the local player: the stern of a Cyclops is 50 m from its helm) the cable carries it
        if (!towedBody || (towedBody.isKinematic && (towed.isGrabbed || towed.teleporting)))
        {
            secondsFrozen += Time.fixedDeltaTime;
            if (secondsFrozen > 2f)
            {
                VehicleTows.Release(tow, TowReleaseReason.Frozen);
            }
            return;
        }
        secondsFrozen = 0f;
        if (towedBody.isKinematic)
        {
            Carry(cable, length, towerAnchor);
            return;
        }
        if (carrying)
        {
            // The game lets the vehicle go again (it came near the player): it moves on with the speed it was carried at
            carrying = false;
            towedBody.velocity = carryVelocity;
            trail = null;
            LogIfInsideTerrain();
        }

        if (length > 0.001f)
        {
            Vector3 direction = cable / length;
            Vector3 towerVelocity = towerBody ? towerBody.GetPointVelocity(towerAnchor) : Vector3.zero;
            float separationSpeed = Vector3.Dot(towedBody.GetPointVelocity(towedAnchor) - towerVelocity, direction);
            float acceleration = TowRules.PullAcceleration(length, TowRules.RestLength(maxLength), separationSpeed, Time.fixedDeltaTime);
            if (acceleration > 0f)
            {
                towedBody.AddForceAtPosition(-direction * acceleration, towedAnchor, ForceMode.Acceleration);
            }
            LogSpring(length, acceleration);
        }

        WarnAboutDepth();
    }

    /// <summary>
    /// Moves a vehicle that the game keeps still: the same spring as for a free vehicle (<see cref="TowRules.CarryVelocity" />), applied to a velocity of our own, and the body is moved by that velocity. It
    /// turns toward where it goes, as a pulled vehicle does. The movement reaches the other players as that of any simulated vehicle (the transform). Nothing collides with a vehicle that the game keeps still, so
    /// it does not go straight for the tower (which cuts the corner of a cave through its wall) but for the point on the tower's own path that is a rest length behind its anchor (<see cref="TowTrail" />).
    /// </summary>
    private void Carry(Vector3 cable, float length, Vector3 towerAnchor)
    {
        float maxLength = VehicleUpgradeSettings.TowMaxDistance;
        if (!carrying)
        {
            carrying = true;
            carryVelocity = Vector3.zero;
            trail = new TowTrail(TowRules.TRAIL_SPACING, TowRules.TrailKeepLength(maxLength));
            Vector3 start = towerAnchor + cable;
            trail.Add(start.x, start.y, start.z);
            Log.Info($"[VehicleUpgrades] tow: the game keeps {towed.name} still here (the world is not built around it), the cable carries it along the path of the tower");
        }
        float dt = Time.fixedDeltaTime;
        trail.Add(towerAnchor.x, towerAnchor.y, towerAnchor.z);
        (float X, float Y, float Z)? behind = trail.PointBehind(TowRules.RestLength(maxLength));
        Vector3 target = behind.HasValue ? new Vector3(behind.Value.X, behind.Value.Y, behind.Value.Z) : towerAnchor;
        Vector3 offset = towerAnchor + cable - target;
        float distance = offset.magnitude;
        Vector3 direction = distance > 0.001f ? offset / distance : Vector3.zero;
        Vector3 towerVelocity = towerBody ? towerBody.GetPointVelocity(towerAnchor) : Vector3.zero;
        // the target is already a rest length behind the tower, so the spring has no slack of its own: a rest length of 0 to the target
        (float x, float y, float z) = TowRules.CarryVelocity(carryVelocity.x, carryVelocity.y, carryVelocity.z, towerVelocity.x, towerVelocity.y, towerVelocity.z, direction.x, direction.y, direction.z, distance, 0f, dt);
        carryVelocity = new Vector3(x, y, z);
        towedBody.MovePosition(towedBody.position + carryVelocity * dt);
        if (carryVelocity.sqrMagnitude > TowRules.CARRY_MIN_TURN_SPEED * TowRules.CARRY_MIN_TURN_SPEED)
        {
            Quaternion heading = Quaternion.LookRotation(carryVelocity.normalized, Vector3.up);
            towedBody.MoveRotation(Quaternion.RotateTowards(towedBody.rotation, heading, TowRules.CARRY_TURN_DEGREES_PER_SECOND * dt));
        }
        LogSpring(length, carryVelocity.magnitude);
        WarnAboutDepth();
    }

    /// <summary>
    /// The game lets a carried vehicle go (the world around it is built: it came near the player): says in the log when it stands inside terrain then, so that a tow that ends with a stuck vehicle can be traced
    /// to the carry. A box a little smaller than the vehicle's colliders on the terrain layer only; nothing is moved.
    /// </summary>
    private void LogIfInsideTerrain()
    {
        try
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Collider collider in towed.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && !collider.isTrigger)
                {
                    if (any)
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                    else
                    {
                        bounds = collider.bounds;
                        any = true;
                    }
                }
            }
            if (!any)
            {
                return;
            }
            Collider[] inside = Physics.OverlapBox(bounds.center, bounds.extents * 0.7f, Quaternion.identity, 1 << LayerID.TerrainCollider, QueryTriggerInteraction.Ignore);
            if (inside.Length > 0)
            {
                Log.Warn($"[VehicleUpgrades] tow: {towed.name} was let go by the carry inside the terrain ({inside.Length} collider(s), first {inside[0].name}): it may be stuck");
            }
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] tow: could not check whether the carried vehicle ends up inside the terrain");
        }
    }

    private bool TryGetAnchors(out Vector3 towerAnchor, out Vector3 towedAnchor)
    {
        towedAnchor = default;
        Vector3 towerForward = tower is SubRoot cyclops && cyclops.subAxis ? cyclops.subAxis.forward : towerRoot.forward;
        towerAnchor = towerRoot.position;
        bool haveTower = HullBounds.TryGetAnchor(towerRoot, towerForward, false, out towerAnchor);
        bool haveTowed = HullBounds.TryGetAnchor(towed.transform, towed.transform.forward, true, out towedAnchor);
        if (!haveTowed)
        {
            towedAnchor = towed.transform.position;
        }
        if (!haveTower)
        {
            towerAnchor = towerRoot.position;
        }
        return true;
    }

    /// <summary>The tower's client is the one that hurts the towed vehicle with crush damage, so it is also the one that knows and can warn.</summary>
    private void WarnAboutDepth()
    {
        if (Time.time < nextDepthCheck)
        {
            return;
        }
        nextDepthCheck = Time.time + 1f;
        towed.GetDepth(out int depth, out int crushDepth);
        string state = TowRules.DepthWarning(depth, crushDepth);
        if (state == null)
        {
            lastDepthState = null;
            return;
        }
        if (state != lastDepthState || Time.time - lastDepthWarning > TowRules.DEPTH_WARNING_REPEAT_SECONDS)
        {
            lastDepthState = state;
            lastDepthWarning = Time.time;
            Log.InGame(state == "below" ? $"Tow hook: the towed {towed.GetName()} is below its crush depth!" : $"Tow hook: the towed {towed.GetName()} is nearly at its crush depth.");
        }
    }

    private void LogSpring(float length, float acceleration)
    {
        if (Time.time < nextLog)
        {
            return;
        }
        nextLog = Time.time + 2f;
        Log.Debug($"[VehicleUpgrades] tow: spring L={length:F1} a={acceleration:F1}{(carrying ? " (carried)" : "")}");
    }
}
