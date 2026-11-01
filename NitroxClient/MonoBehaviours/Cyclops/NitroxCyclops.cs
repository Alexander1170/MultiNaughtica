using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// Script responsible for managing all player movement-related interactions.
/// </summary>
public class NitroxCyclops : MonoBehaviour
{
    public VirtualCyclops Virtual;
    private CyclopsMotor cyclopsMotor;
    private SubRoot subRoot;
    private Rigidbody rigidbody;
    private WorldForces worldForces;
    private Stabilizer stabilizer;
    private CyclopsNoiseManager cyclopsNoiseManager;
    private SubControl subControl;

    /// <summary>
    /// A hull which creeps this long (seconds) after the helm was left is news; one that coasts down after a drive is not counted.
    /// </summary>
    private const float COAST_GRACE_SECONDS = 20f;

    private readonly HullParkingRules.Brake linearBrake = HullParkingRules.CreateLinearBrake();
    private readonly HullParkingRules.Brake yawBrake = HullParkingRules.CreateYawBrake();
    private float lastSteeredTime;
    private float lastCreepLogTime = float.NegativeInfinity;

    // What the log line about a hull that nobody steers reports (HullParkingRules.IsCreepNews decides when). Speed the brake took out:
    private float removedDistance;
    private float removedMaxSpeed;
    private int removedSteps;
    private Vector3 removedNet;

    // Movement that did not go through the speed (a write to the pose, the push out of an overlap): the brake can not hold it
    private float unexplainedDistance;
    private float unexplainedMax;
    private int unexplainedSteps;
    private Vector3 lastCenterOfMass;
    private float lastCenterOfMassStep;
    private bool hasLastCenterOfMass;

    // Speed above what the brake takes (a creature, an impact, a current)
    private int fastSteps;
    private float fastMaxSpeed;

    public readonly Dictionary<INitroxPlayer, CyclopsPawn> Pawns = [];

    public static readonly Dictionary<NitroxCyclops, float> ScaledNoiseByCyclops = [];

    /// <summary>
    /// The parking brake of <see cref="HullParkingRules" /> (V4, bug 11). The Debug harness switches it off to compare a run with and without.
    /// </summary>
    public static bool ParkingBrakeEnabled { get; set; } = true;

#if DEBUG
    public bool DebugBraking => linearBrake.Braking || yawBrake.Braking;
#endif

    public void Start()
    {
        cyclopsMotor = Player.mainObject.GetComponent<CyclopsMotor>();
        subRoot = GetComponent<SubRoot>();
        subControl = GetComponent<SubControl>();
        lastSteeredTime = Time.unscaledTime;
        rigidbody = GetComponent<Rigidbody>();
        worldForces = GetComponent<WorldForces>();
        stabilizer = GetComponent<Stabilizer>();
        cyclopsNoiseManager = GetComponent<CyclopsNoiseManager>();

        UWE.Utils.SetIsKinematicAndUpdateInterpolation(rigidbody, false, true);

        WorkaroundColliders();

        ScaledNoiseByCyclops.Add(this, 0f);

        // Measures how high the passengers stand above the floor and logs the odd one (bug 6, PLAYERS.md PL9 (6)); changes nothing
        gameObject.EnsureComponent<PassengerHeightWatch>();
    }

    public void Update()
    {
        MaintainPawns();

        // Calculation from AttackCyclops.UpdateAggression
        ScaledNoiseByCyclops[this] = Mathf.Lerp(0f, 150f, cyclopsNoiseManager.GetNoisePercent());
    }

    public void FixedUpdate()
    {
        ApplyParkingBrake();
    }

    public void OnDestroy()
    {
        ScaledNoiseByCyclops.Remove(this);
    }

    /// <summary>
    /// Takes the speed out of a hull which nobody steers and which moves slowly (<see cref="HullParkingRules" />), on the client that simulates it. A parked Cyclops is a free body in
    /// the water, anything that acts on it for a while, however weakly, makes it wander, and the other players see it wander and snap back with the next snapshot. A copy that
    /// another client simulates is not touched (its movement replicator draws it), nor is a hull that somebody steers, a flooded or a destroyed one.
    /// </summary>
    private void ApplyParkingBrake()
    {
        if (!rigidbody || !subRoot || !subControl)
        {
            return;
        }

        bool steered = IsSteered();
        bool isReplica = TryGetComponent<MovementReplicator>(out _);
        if (steered || isReplica)
        {
            lastSteeredTime = Time.unscaledTime;
        }

        if (!ParkingBrakeEnabled || !HullParkingRules.ShouldBrake(steered, isReplica, rigidbody.isKinematic, subRoot.subDestroyed, subRoot.floodFraction))
        {
            linearBrake.Reset();
            yawBrake.Reset();
            hasLastCenterOfMass = false;
            return;
        }

        if (rigidbody.IsSleeping())
        {
            hasLastCenterOfMass = false;
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        Vector3 velocity = rigidbody.velocity;
        bool coasted = Time.unscaledTime - lastSteeredTime >= COAST_GRACE_SECONDS;
        NoteUnexplainedMovement(velocity, deltaTime, coasted);

        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        float factor = linearBrake.Step(horizontalSpeed, deltaTime);
        if (factor < 1f && horizontalSpeed > 0f)
        {
            rigidbody.velocity = new Vector3(velocity.x * factor, velocity.y, velocity.z * factor);
            if (coasted)
            {
                removedDistance += horizontalSpeed * deltaTime;
                removedMaxSpeed = Mathf.Max(removedMaxSpeed, horizontalSpeed);
                removedSteps++;
                removedNet += transform.InverseTransformDirection(new Vector3(velocity.x, 0f, velocity.z) * deltaTime);
            }
        }

        if (coasted && HullParkingRules.OutrunsBrake(horizontalSpeed, factor))
        {
            fastSteps++;
            fastMaxSpeed = Mathf.Max(fastMaxSpeed, horizontalSpeed);
        }

        Vector3 angularVelocity = rigidbody.angularVelocity;
        float yawFactor = yawBrake.Step(Mathf.Abs(angularVelocity.y), deltaTime);
        if (yawFactor < 1f && angularVelocity.y != 0f)
        {
            rigidbody.angularVelocity = new Vector3(angularVelocity.x, angularVelocity.y * yawFactor, angularVelocity.z);
        }

        ReportCreep();
    }

    /// <summary>
    /// Somebody sits at the helm, or the throttle is on. SubControl only reads input (and only thrusts) in the direct input mode, which <c>Player.EnterPilotingMode</c> sets.
    /// </summary>
    private bool IsSteered()
    {
        if (subControl.controlMode == SubControl.Mode.DirectInput || subControl.appliedThrottle)
        {
            return true;
        }

        Player player = Player.main;
        return player && player.currentSub == subRoot && player.mode == Player.Mode.Piloting;
    }

    /// <summary>
    /// Compares where the hull is with where its speed says it must be after the last physics step: a step moves a body by its new speed times the step, so a difference means that
    /// something moved the hull without going through the speed (a write to the transform or the pose, the push out of an overlap with another collider). The parking brake can not
    /// hold that, the log line says how much of it there was (<see cref="HullParkingRules.UnexplainedStep" />). The center of mass is used because the speed of a body is the
    /// speed of its center of mass, the pivot moves differently while the hull turns.
    /// </summary>
    private void NoteUnexplainedMovement(Vector3 velocity, float deltaTime, bool coasted)
    {
        Vector3 center = rigidbody.worldCenterOfMass;
        if (hasLastCenterOfMass && coasted && HullParkingRules.AreSameStep(lastCenterOfMassStep, deltaTime))
        {
            float unexplained = HullParkingRules.UnexplainedStep((center - lastCenterOfMass - velocity * deltaTime).magnitude);
            if (unexplained > 0f)
            {
                unexplainedDistance += unexplained;
                unexplainedMax = Mathf.Max(unexplainedMax, unexplained);
                unexplainedSteps++;
            }
        }

        lastCenterOfMass = center;
        lastCenterOfMassStep = deltaTime;
        hasLastCenterOfMass = true;
    }

    /// <summary>
    /// Says in the log, at most every <see cref="HullParkingRules.REPORT_INTERVAL" /> seconds, how a hull that nobody steers moved by itself: through its speed (what the brake took
    /// out, the distance, and the net distance in the axes of the hull: a creep in one direction has a net as long as the distance, a hull that swings back and forth has none),
    /// without its speed (a pose write or a push out of an overlap) and faster than the brake takes (a creature, an impact), and who stood in the hull and what its physics were
    /// doing meanwhile. A drift of bug 11 that nobody saw is then in the log of the next session (VEHICLES.md section 7).
    /// </summary>
    private void ReportCreep()
    {
        if (!HullParkingRules.IsCreepNews(Time.unscaledTime - lastCreepLogTime, removedDistance, unexplainedDistance, fastSteps))
        {
            return;
        }

        lastCreepLogTime = Time.unscaledTime;
        Player player = Player.main;
        bool aboard = player && player.currentSub == subRoot;
        string playerText = player && player.rigidBody ? $"mode {player.mode}, body kinematic {player.rigidBody.isKinematic}, detectCollisions {player.rigidBody.detectCollisions}" : "no local player";
        string id = this.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "noid";
        Log.Info($"[{nameof(NitroxCyclops)}] {id}: nobody steers the hull and it moved by itself. By its speed: {removedDistance:F2} m in {removedSteps} physics steps (fastest {removedMaxSpeed:F3} m/s), net {removedNet.magnitude:F2} m, in the hull's axes {removedNet.x:F2}/{removedNet.y:F2}/{removedNet.z:F2}, the parking brake took the speed out each time. Without a speed (pose write, push out of an overlap): {unexplainedDistance:F2} m in {unexplainedSteps} steps (largest {unexplainedMax:F3} m). Faster than the brake takes: {fastSteps} steps (fastest {fastMaxSpeed:F3} m/s). Local player aboard: {aboard} ({playerText}), {DescribePawns()}, physics step {Time.fixedDeltaTime:F3} s, autoSyncTransforms {Physics.autoSyncTransforms}, interpolation {rigidbody.interpolation}");
        removedDistance = 0f;
        removedMaxSpeed = 0f;
        removedSteps = 0;
        removedNet = Vector3.zero;
        unexplainedDistance = 0f;
        unexplainedMax = 0f;
        unexplainedSteps = 0;
        fastSteps = 0;
        fastMaxSpeed = 0f;
    }

    /// <summary>
    /// Who stands in the hull and in what physical state, for the log lines about a hull that moved (the creep line here, the hold of a copy in <c>MovementReplicator</c>, the
    /// <c>hullrest</c> probe): the number of pawns and, for every remote player, whether his body is kinematic (a body that is not is a dynamic child of the hull;
    /// <c>RemotePlayer.SetPilotingChair</c> makes it so when its player leaves the helm and nothing makes it kinematic again inside a Cyclops), whether it detects collisions, its
    /// interpolation and how many enabled colliders that are not triggers it carries (the body only has the trigger that <c>RemotePlayer.SetupBody</c> adds; any other solid collider
    /// would be a candidate for what pushes the hull). VEHICLES.md section 7.
    /// </summary>
    public string DescribePawns()
    {
        List<string> parts = [];
        foreach (INitroxPlayer pawnPlayer in Pawns.Keys)
        {
            if (pawnPlayer is RemotePlayer remotePlayer)
            {
                parts.Add(DescribeRemoteBody(remotePlayer));
            }
            else
            {
                parts.Add($"{pawnPlayer.PlayerName} (local)");
            }
        }

        return $"pawns {Pawns.Count}: {(parts.Count == 0 ? "none" : string.Join(", ", parts))}";
    }

    private static string DescribeRemoteBody(RemotePlayer remotePlayer)
    {
        GameObject body = remotePlayer.Body;
        if (!body)
        {
            return $"{remotePlayer.PlayerName} (remote, no body)";
        }

        int solidColliders = 0;
        foreach (Collider collider in body.GetComponentsInChildren<Collider>(false))
        {
            if (collider.enabled && !collider.isTrigger)
            {
                solidColliders++;
            }
        }

        Rigidbody bodyRigidbody = remotePlayer.RigidBody;
        string state = bodyRigidbody ? $"kinematic {bodyRigidbody.isKinematic}, detectCollisions {bodyRigidbody.detectCollisions}, interpolation {bodyRigidbody.interpolation}" : "no rigidbody";
        return $"{remotePlayer.PlayerName} (remote, {state}, solid colliders {solidColliders})";
    }

    /// <summary>
    /// Triggers required on-remove callbacks on children player objects, including the local player.
    /// </summary>
    public void RemoveAllPlayers()
    {
        // This will call OnLocalPlayerExit
        if (Player.main.currentSub == subRoot)
        {
            Player.main.SetCurrentSub(null);
        }
 
        foreach (RemotePlayerIdentifier remotePlayerIdentifier in GetComponentsInChildren<RemotePlayerIdentifier>(true))
        {
            remotePlayerIdentifier.RemotePlayer.ResetStates();
            OnPlayerExit(remotePlayerIdentifier.RemotePlayer);
        }
    }

    /// <summary>
    /// Parents local player to the cyclops and registers it in the current cyclops.
    /// </summary>
    public void OnLocalPlayerEnter()
    {
        Virtual = VirtualCyclops.Instance;
        Virtual.SetCurrentCyclops(this);

        Player.mainObject.transform.parent = subRoot.transform;
        CyclopsPawn pawn = AddPawnForPlayer(this.Resolve<ILocalNitroxPlayer>());
        cyclopsMotor.SetCyclops(this, subRoot, pawn);
        cyclopsMotor.ToggleCyclopsMotor(true);
    }

    /// <summary>
    /// Unregisters the local player from the current cyclops. Ensures the player is not weirdly rotated when it leaves the cyclops.
    /// </summary>
    public void OnLocalPlayerExit()
    {
        RemovePawnForPlayer(this.Resolve<ILocalNitroxPlayer>());
        Player.main.transform.parent = null;
        Player.main.transform.rotation = Quaternion.identity;
        cyclopsMotor.ToggleCyclopsMotor(false);
        cyclopsMotor.Pawn = null;

        if (Virtual)
        {
            Virtual.SetCurrentCyclops(null);
        }
    }

    /// <summary>
    /// Registers a remote player for it to get a pawn in the current cyclops.
    /// </summary>
    public void OnPlayerEnter(RemotePlayer remotePlayer)
    {
        remotePlayer.Pawn = AddPawnForPlayer(remotePlayer);
    }

    /// <summary>
    /// Unregisters a remote player from the current cyclops.
    /// </summary>
    public void OnPlayerExit(RemotePlayer remotePlayer)
    {
        RemovePawnForPlayer(remotePlayer);
        remotePlayer.Pawn = null;
    }

    public void MaintainPawns()
    {
        foreach (CyclopsPawn pawn in Pawns.Values)
        {
            if (pawn.MaintainPredicate())
            {
                pawn.MaintainPosition();
            }
        }
    }

    public CyclopsPawn AddPawnForPlayer(INitroxPlayer player)
    {
        if (!Pawns.TryGetValue(player, out CyclopsPawn pawn))
        {
            pawn = new(player, this);
            Pawns.Add(player, pawn);
            // The line of the moment of the boarding (bug 6, PLAYERS.md PL9 (6)): where the root is, what is under it; writes only, never throws
            gameObject.EnsureComponent<PassengerHeightWatch>().NotePawnCreated(player, pawn);
        }
        return pawn;
    }

    public void RemovePawnForPlayer(INitroxPlayer player)
    {
        if (Pawns.TryGetValue(player, out CyclopsPawn pawn))
        {
            pawn.Terminate();
        }
        Pawns.Remove(player);
    }

    public void SetBroadcasting()
    {
        worldForces.enabled = true;
        stabilizer.stabilizerEnabled = true;
    }

    public void SetReceiving()
    {
        worldForces.enabled = false;
        stabilizer.stabilizerEnabled = false;
    }

    private void WorkaroundColliders()
    {
        CyclopsSubNameScreen cyclopsSubNameScreen = transform.GetComponentInChildren<CyclopsSubNameScreen>(true);
        TriggerWorkaround subNameTriggerWorkaround = cyclopsSubNameScreen.gameObject.AddComponent<TriggerWorkaround>();
        subNameTriggerWorkaround.Initialize(this,cyclopsSubNameScreen.animator, cyclopsSubNameScreen.ContentOn, nameof(CyclopsSubNameScreen.ContentOff), cyclopsSubNameScreen);

        CyclopsLightingPanel cyclopsLightingPanel = transform.GetComponentInChildren<CyclopsLightingPanel>(true);
        TriggerWorkaround lightingTriggerWorkaround = cyclopsLightingPanel.gameObject.AddComponent<TriggerWorkaround>();
        lightingTriggerWorkaround.Initialize(this, cyclopsLightingPanel.uiPanel, cyclopsLightingPanel.ButtonsOn, nameof(CyclopsLightingPanel.ButtonsOff), cyclopsLightingPanel);
    }
}
