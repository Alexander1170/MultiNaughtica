using Nitrox.Model.DataStructures.Unity;

namespace NitroxClient.GameLogic.Spawning.WorldEntities;

[TestClass]
public class RemoteBodySpawnPoseTest
{
    private const float TOLERANCE = 0.0001f;

    private static readonly NitroxVector3 position = new(-78f, 12.5f, 371.2f);
    private static readonly NitroxQuaternion rotation = NitroxQuaternion.FromEuler(10f, 135f, 0f);

    [TestMethod]
    public void WithoutParent_PoseIsInWorldSpaceAndUnchanged()
    {
        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(false, position, rotation);

        pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.WORLD);
        pose.Position.Equals(position, TOLERANCE).Should().BeTrue();
        pose.HasRotation.Should().BeTrue();
        pose.Rotation.Equals(rotation, TOLERANCE).Should().BeTrue();
    }

    [TestMethod]
    public void WithParent_PoseIsInParentSpaceAndUnchanged()
    {
        // The position of a player in a Cyclops is the one of his pawn in the space of the Cyclops: it must not be taken for a world position
        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(true, position, rotation);

        pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.PARENT);
        pose.Position.Equals(position, TOLERANCE).Should().BeTrue();
        pose.HasRotation.Should().BeTrue();
        pose.Rotation.Equals(rotation, TOLERANCE).Should().BeTrue();
    }

    [TestMethod]
    public void WithParent_OriginIsAValidPose()
    {
        // A player who just joined the server in a lifepod has (0, 0, 0): the origin of the pod
        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(true, NitroxVector3.Zero, NitroxQuaternion.Identity);

        pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.PARENT);
        pose.Position.Equals(NitroxVector3.Zero, TOLERANCE).Should().BeTrue();
        pose.HasRotation.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(float.NaN, 0f, 0f)]
    [DataRow(0f, float.NaN, 0f)]
    [DataRow(0f, 0f, float.NaN)]
    [DataRow(float.PositiveInfinity, 0f, 0f)]
    [DataRow(0f, float.NegativeInfinity, 0f)]
    public void NonFinitePosition_PlacesNothing(float x, float y, float z)
    {
        foreach (bool hasParent in new[] { false, true })
        {
            RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(hasParent, new NitroxVector3(x, y, z), rotation);

            pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.NONE);
            pose.HasRotation.Should().BeFalse();
        }
    }

    [TestMethod]
    public void RotationThatWasNeverSet_KeepsThePositionButNotTheRotation()
    {
        // default(NitroxQuaternion) is 0, 0, 0, 0: applied to a transform it would give NaNs
        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(false, position, default);

        pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.WORLD);
        pose.Position.Equals(position, TOLERANCE).Should().BeTrue();
        pose.HasRotation.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(float.NaN, 0f, 0f, 1f)]
    [DataRow(0f, float.PositiveInfinity, 0f, 1f)]
    [DataRow(0f, 0f, 0f, float.NaN)]
    public void NonFiniteRotation_KeepsThePositionButNotTheRotation(float x, float y, float z, float w)
    {
        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(true, position, new NitroxQuaternion(x, y, z, w));

        pose.Space.Should().Be(RemoteBodySpawnPose.PoseSpace.PARENT);
        pose.Position.Equals(position, TOLERANCE).Should().BeTrue();
        pose.HasRotation.Should().BeFalse();
    }

    [TestMethod]
    public void UnnormalizedRotation_IsNormalizedAndKeepsItsDirection()
    {
        NitroxQuaternion scaled = new(rotation.X * 3f, rotation.Y * 3f, rotation.Z * 3f, rotation.W * 3f);

        RemoteBodySpawnPose.Pose pose = RemoteBodySpawnPose.Resolve(false, position, scaled);

        pose.HasRotation.Should().BeTrue();
        pose.Rotation.Equals(rotation, TOLERANCE).Should().BeTrue();
    }
}
