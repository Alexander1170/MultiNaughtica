using System.Numerics;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The change from glTF space to the game's (docs/coop/PLAYER_MODELS.md, "Conversion rules"): a mirror of the X axis, which keeps a character that faces +Z facing +Z. The tests check the algebra that makes it
/// right: a mirrored rotation turns a mirrored vector the way the original turns the original, a mirrored matrix moves a mirrored point where the original moves the point, and a mirrored triangle is wound
/// the other way round.
/// </summary>
[TestClass]
public class GlbSpaceTest
{
    private static readonly Vector3[] Points = [new(0.2f, -1.3f, 0.8f), new(1f, 0f, 0f), new(0f, 2f, -3f), new(-0.7f, 0.4f, 1.9f)];

    private static Vector3 Mirror(Vector3 v)
    {
        GlbSpace.Vector(v.X, v.Y, v.Z, out float x, out float y, out float z);
        return new Vector3(x, y, z);
    }

    private static Quaternion MirrorRotation(Quaternion q)
    {
        GlbSpace.Rotation(q.X, q.Y, q.Z, q.W, out float x, out float y, out float z, out float w);
        return new Quaternion(x, y, z, w);
    }

    private static void AssertClose(Vector3 actual, Vector3 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-4f);
        actual.Y.Should().BeApproximately(expected.Y, 1e-4f);
        actual.Z.Should().BeApproximately(expected.Z, 1e-4f);
    }

    [TestMethod]
    public void APositionIsMirroredInXAndNothingElseChanges()
    {
        GlbSpace.Vector(1f, 2f, 3f, out float x, out float y, out float z);

        (x, y, z).Should().Be((-1f, 2f, 3f));
        AssertClose(Mirror(Mirror(new Vector3(1f, 2f, 3f))), new Vector3(1f, 2f, 3f));
    }

    [TestMethod]
    public void ACharacterThatFacesPlusZStillFacesPlusZAndItsLeftSideIsAtMinusX()
    {
        // in glTF the character's left side is at +X and it looks along +Z (a Mixamo LeftHand is at x = 0.565)
        Vector3 leftHand = Mirror(new Vector3(0.565f, 1.066f, 0.074f));
        Vector3 forward = Mirror(new Vector3(0f, 0f, 1f));

        leftHand.X.Should().BeLessThan(0f);
        AssertClose(forward, new Vector3(0f, 0f, 1f));
    }

    [TestMethod]
    public void AMirroredRotationTurnsAMirroredVectorTheWayTheOriginalTurnsTheOriginal()
    {
        foreach (Quaternion raw in new[] { new Quaternion(0.3f, -0.5f, 0.7f, 0.4f), new Quaternion(0.1f, 0.9f, -0.2f, 0.3f), new Quaternion(0.7f, 0f, 0f, 0.7f), new Quaternion(0f, 0f, 0.5f, 0.8f) })
        {
            Quaternion q = Quaternion.Normalize(raw);
            foreach (Vector3 v in Points)
            {
                Vector3 expected = Mirror(Vector3.Transform(v, q));
                Vector3 actual = Vector3.Transform(Mirror(v), MirrorRotation(q));

                AssertClose(actual, expected);
            }
        }
    }

    [TestMethod]
    public void AMirroredMatrixMovesAMirroredPointWhereTheOriginalMovesThePoint()
    {
        Matrix4x4 original = Matrix4x4.CreateScale(1.2f, 0.9f, 1.1f) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new Quaternion(0.3f, -0.5f, 0.7f, 0.4f))) * Matrix4x4.CreateTranslation(0.5f, -2f, 1.5f);
        // glTF keeps the 16 numbers of a column-major matrix of column vectors: the row-major order of the row-vector matrix of System.Numerics
        float[] gltf = ToArray(original);

        Matrix4x4 converted = FromArray(GlbSpace.Matrix(gltf));

        foreach (Vector3 v in Points)
        {
            AssertClose(Vector3.Transform(Mirror(v), converted), Mirror(Vector3.Transform(v, original)));
        }
    }

    [TestMethod]
    public void ConvertingAMatrixTwiceGivesTheMatrixBack()
    {
        float[] gltf = ToArray(Matrix4x4.CreateFromYawPitchRoll(0.3f, -0.8f, 1.1f) * Matrix4x4.CreateTranslation(1f, 2f, 3f));

        float[] twice = GlbSpace.Matrix(GlbSpace.Matrix(gltf));

        twice.Should().BeEquivalentTo(gltf, options => options.Using<float>(context => context.Subject.Should().BeApproximately(context.Expectation, 1e-5f)).WhenTypeIs<float>());
    }

    [TestMethod]
    public void AMatrixAtAnOffsetOfASkinsMatricesIsConvertedOnItsOwn()
    {
        float[] two = ToArray(Matrix4x4.CreateTranslation(1f, 2f, 3f)).Concat(ToArray(Matrix4x4.CreateTranslation(4f, 5f, 6f))).ToArray();

        float[] second = GlbSpace.Matrix(two, 16);

        Matrix4x4 converted = FromArray(second);
        AssertClose(Vector3.Transform(Vector3.Zero, converted), new Vector3(-4f, 5f, 6f));
    }

    [TestMethod]
    public void AMirroredTriangleIsWoundTheOtherWayRound()
    {
        int[] indices = [0, 1, 2, 3, 4, 5];

        GlbSpace.ReverseWinding(indices).Should().Equal(0, 2, 1, 3, 5, 4);
    }

    [TestMethod]
    public void TheVOfATextureCoordinateCountsFromTheBottomInTheGame()
    {
        GlbSpace.V(0.25f).Should().Be(0.75f);
        GlbSpace.V(0f).Should().Be(1f);
    }

    private static float[] ToArray(Matrix4x4 m)
    {
        return [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    }

    private static Matrix4x4 FromArray(float[] a)
    {
        return new Matrix4x4(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8], a[9], a[10], a[11], a[12], a[13], a[14], a[15]);
    }
}
