using System.Collections.Generic;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public class PlayerMovementBroadcaster : MonoBehaviour
{
    /// <summary>
    /// At most this often (30 Hz): the remote side extrapolates between snapshots, there is no gain in sending one per rendered frame.
    /// </summary>
    private const float BROADCAST_PERIOD = 1f / 30f;

    /// <summary>
    /// A player who did not move is still announced this often so that the others know the player is there (and where).
    /// </summary>
    private const float KEEP_ALIVE_PERIOD = 0.5f;

    private LocalPlayer localPlayer;
    private TimeManager timeManager;
    private double lastBroadcastTime;
    private Vector3 lastPosition;
    private Vector3 lastVelocity;
    private Quaternion lastBodyRotation;
    private Quaternion lastAimingRotation;
    private Vector3 lastLocalPosition;
    private Vector3 lastLocalVelocity;
    private Quaternion lastLocalRotation;
    private readonly List<(double Time, Vector3 Position)> pawnHistory = [];

    public void Awake()
    {
        localPlayer = this.Resolve<LocalPlayer>();
        timeManager = this.Resolve<TimeManager>();
    }

    public void Update()
    {
        // TODO: Replace this temporary fix. Mostly prevents server console being spammed with warnings when a client is in the queue.
        // There should be a way to block all packets from being sent when in the join queue or during initial sync.
        if (!Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        // Freecam does disable main camera control
        // But it's also disabled when driving the cyclops through a cyclops camera (content.activeSelf is only true when controlling through a cyclops camera)
        if (!MainCameraControl.main.isActiveAndEnabled &&
            !uGUI_CameraCyclops.main.content.activeSelf)
        {
            return;
        }

        if (BroadcastPlayerInCyclopsMovement())
        {
            return;
        }

        if (Player.main.isPiloting)
        {
            return;
        }

        Vector3 currentPosition = Player.main.transform.position;
        Vector3 playerVelocity = Player.main.playerController.velocity;

        // IDEA: possibly only CameraRotation is of interest, because bodyrotation is extracted from that.
        // Position and rotations are always world space, also inside a base: the receiver sets world transforms, the server stores the world rotation and gives it back at the
        // next join. (The position used to be made relative to the base and back, which changes nothing, and the rotations were sent relative to the base while the server
        // stored them as if they were world rotations, so a player who logged out in a base that is not rotated by 0 degrees came back facing the wrong way.)
        Quaternion bodyRotation = MainCameraControl.main.viewModel.transform.rotation;
        Quaternion aimingRotation = Player.main.camRoot.GetAimingTransform().rotation;

        double now = MovementClock.Now(timeManager.RealTimeElapsed);
        if (PlayerMovementTuning.Throttle)
        {
            if (now - lastBroadcastTime < BROADCAST_PERIOD)
            {
                return;
            }

            // The velocity counts too: the first packet after a stop (zero velocity) must go out, otherwise the others keep extrapolating the last movement
            bool changed = (currentPosition - lastPosition).sqrMagnitude > 0.0001f ||
                           (playerVelocity - lastVelocity).sqrMagnitude > 0.0025f ||
                           Quaternion.Angle(lastBodyRotation, bodyRotation) > 0.5f ||
                           Quaternion.Angle(lastAimingRotation, aimingRotation) > 0.5f;
            if (!changed && now - lastBroadcastTime < KEEP_ALIVE_PERIOD)
            {
                return;
            }
        }

        lastBroadcastTime = now;
        lastPosition = currentPosition;
        lastVelocity = playerVelocity;
        lastBodyRotation = bodyRotation;
        lastAimingRotation = aimingRotation;
        localPlayer.BroadcastLocation(currentPosition, playerVelocity, bodyRotation, aimingRotation, now);
    }

    /// <summary>
    /// Inside a Cyclops the player is a pawn walking in a motionless copy of it: position and rotation are those of the pawn in the space of the Cyclops, the velocity is the walking
    /// velocity in that space. Sent like <see cref="localPlayer" /> movements (30 Hz, idle suppression, timestamp) so that the others can extrapolate the walk.
    /// </summary>
    /// <summary>
    /// How far back (seconds) the velocity of the pawn is measured: long enough to span several physics steps of the motor (a step moves the pawn, a frame between two steps does not).
    /// </summary>
    private const float PAWN_VELOCITY_WINDOW = 0.08f;

    /// <summary>
    /// The velocity the pawn really has, from where it was a moment ago. Not the motor's velocity: that one adds up gravity for as long as the motor thinks the pawn is in the air
    /// (standing on something its ground check does not count, it reached hundreds of meters per second) and the others extrapolate with it.
    /// </summary>
    private Vector3 MeasurePawnVelocity(double now, Vector3 localPosition)
    {
        pawnHistory.Add((now, localPosition));
        while (pawnHistory.Count > 2 && now - pawnHistory[1].Time >= PAWN_VELOCITY_WINDOW)
        {
            pawnHistory.RemoveAt(0);
        }

        (double time, Vector3 position) = pawnHistory[0];
        float age = MovementTimeRules.Age(now, time);
        return age > 0.02f ? (localPosition - position) / age : Vector3.zero;
    }

    private bool BroadcastPlayerInCyclopsMovement()
    {
        if (Player.main.isPiloting || !Player.main.TryGetComponent(out CyclopsMotor cyclopsMotor) || cyclopsMotor.Pawn == null)
        {
            pawnHistory.Clear();
            return false;
        }

        Transform pawnTransform = cyclopsMotor.Pawn.Handle.transform;
        Vector3 localPosition = pawnTransform.localPosition;
        Quaternion localRotation = pawnTransform.localRotation;
        double now = MovementClock.Now(timeManager.RealTimeElapsed);
        Vector3 localVelocity = MeasurePawnVelocity(now, localPosition);
        if (PlayerMovementTuning.Throttle)
        {
            if (now - lastBroadcastTime < BROADCAST_PERIOD)
            {
                return true;
            }

            bool changed = (localPosition - lastLocalPosition).sqrMagnitude > 0.0001f ||
                           (localVelocity - lastLocalVelocity).sqrMagnitude > 0.0025f ||
                           Quaternion.Angle(lastLocalRotation, localRotation) > 0.5f;
            if (!changed && now - lastBroadcastTime < KEEP_ALIVE_PERIOD)
            {
                return true;
            }
        }

        lastBroadcastTime = now;
        lastLocalPosition = localPosition;
        lastLocalVelocity = localVelocity;
        lastLocalRotation = localRotation;
        this.Resolve<IPacketSender>().Send(new PlayerInCyclopsMovement(localPlayer.SessionId.Value, localPosition.ToDto(), localVelocity.ToDto(), localRotation.ToDto(), now));
        return true;
    }
}
