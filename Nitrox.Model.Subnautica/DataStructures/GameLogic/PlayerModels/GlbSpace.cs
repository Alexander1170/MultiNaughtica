namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// From glTF space to the game's (docs/coop/PLAYER_MODELS.md, "Conversion rules"). glTF is right-handed with Y up and a character that looks along +Z, with its left side at +X; Unity is left-handed. The change is
/// <b>a mirror of the X axis</b>: x becomes -x everywhere. A character that faces +Z still faces +Z, and its left side is at -X, which is where the left of a character facing +Z is in a left-handed world, so
/// nothing has to be turned. A mirror turns the winding of every triangle around (the triangle indices swap their last two), a rotation about the axis (a, b, c) becomes a rotation about (a, -b, -c) by the same
/// angle (the quaternion x, y, z, w becomes x, -y, -z, w), a matrix M becomes F M F with F = diag(-1, 1, 1, 1), and the V of a texture coordinate counts from the bottom in Unity (1 - v). All pure
/// arithmetic on plain numbers, so that the tests can check it without the game.
/// </summary>
public static class GlbSpace
{
    /// <summary>The position or the direction (x, y, z) in the game's space.</summary>
    public static void Vector(float x, float y, float z, out float gameX, out float gameY, out float gameZ)
    {
        gameX = -x;
        gameY = y;
        gameZ = z;
    }

    /// <summary>The rotation (x, y, z, w) in the game's space.</summary>
    public static void Rotation(float x, float y, float z, float w, out float gameX, out float gameY, out float gameZ, out float gameW)
    {
        gameX = x;
        gameY = -y;
        gameZ = -z;
        gameW = w;
    }

    /// <summary>The V of a texture coordinate in the game's space.</summary>
    public static float V(float v) => 1f - v;

    /// <summary>
    /// A column-major 4x4 matrix of glTF in the game's space (also column-major): F M F, which negates the elements of the first row and of the first column, except the one that is in both.
    /// </summary>
    public static float[] Matrix(float[] matrix, int offset = 0)
    {
        float[] result = new float[16];
        for (int column = 0; column < 4; column++)
        {
            for (int row = 0; row < 4; row++)
            {
                float value = matrix[offset + column * 4 + row];
                bool flip = (row == 0) != (column == 0);
                result[column * 4 + row] = flip ? -value : value;
            }
        }
        return result;
    }

    /// <summary>The triangle indices for the mirrored mesh: every triangle's last two indices swap places. The array is changed in place and returned.</summary>
    public static int[] ReverseWinding(int[] indices)
    {
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }
        return indices;
    }
}
