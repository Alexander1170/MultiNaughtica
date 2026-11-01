using NitroxClient.GameLogic;
using NitroxClient.Unity.Smoothing;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public class RemotelyControlled : MonoBehaviour
{
    private const float SQR_TELEPORT_DISTANCE = 25f; // 5 m
    private const float SQR_CORRECTION_MIN_DISTANCE = 0.25f; // 0.5 m, smaller offsets are not worth moving the creature for
    private const float CORRECTION_STRENGTH = 0.5f; // part of the offset removed per update (every 0.25 s)
    private static readonly float CORRECTION_DURATION = EntityPositionBroadcaster.BROADCAST_INTERVAL; // the correction is spread over the time until the next update

    /// <summary>
    /// What is left of the last position correction, applied a bit every frame so that the creature glides instead of jumping.
    /// </summary>
    private Vector3 pendingCorrection;
    private float correctionTimeLeft;

    /// <summary>
    /// <see cref="Time.time" /> of the last position the simulating player sent for this copy, to tell a copy that is followed from one that nobody updates (the ghost probe of the live check, bug 9).
    /// </summary>
    public float LastUpdateTime { get; private set; } = float.NegativeInfinity;

    private readonly SmoothVector smoothPosition = new SmoothVector();
    private readonly SmoothRotation smoothRotation = new SmoothRotation();

    private SwimBehaviour swimBehaviour;
    private WalkBehaviour walkBehaviour;
    private Rigidbody rigidbody;

    public void Awake()
    {
        swimBehaviour = gameObject.GetComponent<SwimBehaviour>();
        walkBehaviour = gameObject.GetComponent<WalkBehaviour>();
        rigidbody = gameObject.GetComponent<Rigidbody>();
    }

    public void Update()
    {
        if (correctionTimeLeft <= 0f)
        {
            return;
        }

        // Apply the share of the remaining correction this frame covers of the remaining time
        float share = Mathf.Min(1f, Time.deltaTime / correctionTimeLeft);
        transform.position += pendingCorrection * share;
        pendingCorrection -= pendingCorrection * share;
        correctionTimeLeft -= Time.deltaTime;
    }

    public void FixedUpdate()
    {
        if (swimBehaviour || walkBehaviour || !rigidbody)
        {
            return;
        }

        smoothPosition.FixedUpdate();
        smoothRotation.FixedUpdate();

        rigidbody.isKinematic = false;
        rigidbody.velocity = MovementHelper.GetCorrectedVelocity(smoothPosition.Current, Vector3.zero, gameObject, EntityPositionBroadcaster.BROADCAST_INTERVAL);
        rigidbody.angularVelocity = MovementHelper.GetCorrectedAngularVelocity(smoothRotation.Current, Vector3.zero, gameObject, EntityPositionBroadcaster.BROADCAST_INTERVAL);
    }

    public void UpdateOrientation(Vector3 position, Quaternion rotation)
    {
        LastUpdateTime = Time.time;
        TeleportIfTooFar(position, rotation);

        if (swimBehaviour)
        {
            swimBehaviour.SwimTo(position, 3f);
        }

        Transform selfTransform = transform;

        // Entities can lose their swimBehavior (such as if they get killed).  Keep these up-to-date incase that happens.
        smoothPosition.Current = selfTransform.position;
        smoothRotation.Current = selfTransform.rotation;
        smoothPosition.Target = position;
        smoothRotation.Target = rotation;
    }

    public void UpdateKnownSplineUser(Vector3 currentPosition, Quaternion currentRotation, Vector3 destination, Vector3 destinationDirection, float velocity)
    {
        LastUpdateTime = Time.time;
        CorrectPosition(currentPosition, currentRotation);

        if (swimBehaviour)
        {
            // First lines of SwimBehaviour.SwimToInternal
            swimBehaviour.originalTargetPosition = destination;
            swimBehaviour.originalTargetDirection = destinationDirection;
            swimBehaviour.originalVelocity = velocity;
            // Only the useful part of the methods called in SwimBehaviour.SwimToInternal
            swimBehaviour.splineFollowing.GoTo(destination, destinationDirection, velocity);
        }

        if (walkBehaviour)
        {
            walkBehaviour.GoToInternal(destination, destinationDirection, velocity);
        }
    }

    /// <summary>
    /// Our copy follows its own spline from wherever it is, so it slowly drifts away from the simulating player's creature.
    /// Pull it back a bit with every update instead of waiting for it to be far enough to be teleported.
    /// </summary>
    private void CorrectPosition(Vector3 position, Quaternion rotation)
    {
        Transform selfTransform = transform;
        float sqrDistance = (selfTransform.position - position).sqrMagnitude;

        if (sqrDistance > SQR_TELEPORT_DISTANCE)
        {
            selfTransform.position = position;
            selfTransform.rotation = rotation;
            pendingCorrection = Vector3.zero;
            correctionTimeLeft = 0f;
        }
        else if (sqrDistance > SQR_CORRECTION_MIN_DISTANCE)
        {
            // Not applied at once (that was a 0.25-2.5 m jump four times a second): replaces any correction still pending and is spread over the next window
            pendingCorrection = (position - selfTransform.position) * CORRECTION_STRENGTH;
            correctionTimeLeft = CORRECTION_DURATION;
        }
        else
        {
            pendingCorrection = Vector3.zero;
            correctionTimeLeft = 0f;
        }
    }

    private void TeleportIfTooFar(Vector3 position, Quaternion rotation)
    {
        Transform selfTransform = transform;

        if ((selfTransform.position - position).sqrMagnitude > 25) // Optimized 5m distance test
        {
            selfTransform.position = position;
            selfTransform.rotation = rotation;
        }
    }

    public static RemotelyControlled Ensure(GameObject gameObject)
    {
        if (gameObject.TryGetComponent(out RemotelyControlled remotelyControlled))
        {
            return remotelyControlled;
        }
        if (gameObject.GetComponent<PipeSurfaceFloater>())
        {
            return gameObject.AddComponent<RemotelyControlledPipeFloater>();
        }
        return gameObject.AddComponent<RemotelyControlled>();
    }
}
