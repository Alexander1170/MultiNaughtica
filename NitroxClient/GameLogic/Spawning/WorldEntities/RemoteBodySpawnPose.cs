using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

/// <summary>
/// Where the body of a remote player stands when it is spawned from its <see cref="Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.PlayerEntity" />.
/// The server keeps the pose of the player in <c>Transform.LocalPosition</c> / <c>LocalRotation</c>, updated by every <c>PlayerMovement</c> (world pose, converted into the space of
/// the parent the server reparented the entity to when the player entered a base, a Cyclops or a lifepod) and <c>PlayerInCyclopsMovement</c> (already in the space of the Cyclops, which
/// is also that of the pawn). So the pose is relative to the parent of the entity while it has one and world space while it has none. (The entity of a player who just joined the
/// server in a lifepod starts as 0, 0, 0 relative to the lifepod: the pod's own origin until he reports a pose. The server parents its transform at creation, see <c>JoiningManager.CreatePlayerEntity</c>.)
/// </summary>
internal static class RemoteBodySpawnPose
{
    /// <summary>
    /// A quaternion shorter than this (squared) says nothing about a direction (the default value of an entity that never received a rotation is 0, 0, 0, 0).
    /// </summary>
    private const float MIN_ROTATION_LENGTH_SQUARED = 0.0001f;

    internal enum PoseSpace
    {
        /// <summary>
        /// The position cannot be used: the body stays where it was instantiated and the first movement packet places it.
        /// </summary>
        NONE,
        WORLD,
        /// <summary>
        /// Relative to the parent of the entity: the caller converts with the parent it found.
        /// </summary>
        PARENT
    }

    /// <param name="HasRotation">False if the rotation of the entity is not a direction: the body keeps the rotation it has.</param>
    internal readonly record struct Pose(PoseSpace Space, NitroxVector3 Position, NitroxQuaternion Rotation, bool HasRotation);

    internal static Pose Resolve(bool hasParent, NitroxVector3 position, NitroxQuaternion rotation)
    {
        if (!IsFinite(position.X) || !IsFinite(position.Y) || !IsFinite(position.Z))
        {
            return new Pose(PoseSpace.NONE, NitroxVector3.Zero, NitroxQuaternion.Identity, false);
        }

        PoseSpace space = hasParent ? PoseSpace.PARENT : PoseSpace.WORLD;
        if (!IsDirection(rotation))
        {
            return new Pose(space, position, NitroxQuaternion.Identity, false);
        }
        return new Pose(space, position, NitroxQuaternion.Normalize(rotation), true);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsDirection(NitroxQuaternion rotation)
    {
        if (!IsFinite(rotation.X) || !IsFinite(rotation.Y) || !IsFinite(rotation.Z) || !IsFinite(rotation.W))
        {
            return false;
        }
        float lengthSquared = rotation.X * rotation.X + rotation.Y * rotation.Y + rotation.Z * rotation.Z + rotation.W * rotation.W;
        return lengthSquared >= MIN_ROTATION_LENGTH_SQUARED;
    }
}
