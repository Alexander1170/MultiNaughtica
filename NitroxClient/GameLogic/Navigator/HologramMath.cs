using System;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>A rotation as a 3x3 matrix, row by row, for the math of the hologram as plain numbers (the aligner turns the game's quaternion into it).</summary>
public readonly struct Rot3
{
    public readonly float M00, M01, M02, M10, M11, M12, M20, M21, M22;

    public Rot3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
    {
        M00 = m00;
        M01 = m01;
        M02 = m02;
        M10 = m10;
        M11 = m11;
        M12 = m12;
        M20 = m20;
        M21 = m21;
        M22 = m22;
    }

    public static Rot3 Identity => new(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f);

    /// <summary>A turn about the vertical, in degrees, the way Unity counts it: +z turns towards +x.</summary>
    public static Rot3 AboutY(float degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);
        return new Rot3(c, 0f, s, 0f, 1f, 0f, -s, 0f, c);
    }

    /// <summary>A turn about x, in degrees, the way Unity counts it (y turns towards z).</summary>
    public static Rot3 AboutX(float degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);
        return new Rot3(1f, 0f, 0f, 0f, c, -s, 0f, s, c);
    }

    /// <summary>The turn back as a turn of its own (the transpose, which is the inverse of a rotation).</summary>
    public Rot3 Transposed => new(M00, M10, M20, M01, M11, M21, M02, M12, M22);

    /// <summary>A turn about z, in degrees, the way Unity counts it (x turns towards y).</summary>
    public static Rot3 AboutZ(float degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        float c = (float)Math.Cos(radians);
        float s = (float)Math.Sin(radians);
        return new Rot3(c, -s, 0f, s, c, 0f, 0f, 0f, 1f);
    }

    /// <summary>This turn after <paramref name="first" />: the product of the two matrices.</summary>
    public Rot3 After(Rot3 first)
    {
        return new Rot3(
            M00 * first.M00 + M01 * first.M10 + M02 * first.M20, M00 * first.M01 + M01 * first.M11 + M02 * first.M21, M00 * first.M02 + M01 * first.M12 + M02 * first.M22,
            M10 * first.M00 + M11 * first.M10 + M12 * first.M20, M10 * first.M01 + M11 * first.M11 + M12 * first.M21, M10 * first.M02 + M11 * first.M12 + M12 * first.M22,
            M20 * first.M00 + M21 * first.M10 + M22 * first.M20, M20 * first.M01 + M21 * first.M11 + M22 * first.M21, M20 * first.M02 + M21 * first.M12 + M22 * first.M22);
    }

    public (float X, float Y, float Z) Apply((float X, float Y, float Z) v)
    {
        return (M00 * v.X + M01 * v.Y + M02 * v.Z, M10 * v.X + M11 * v.Y + M12 * v.Z, M20 * v.X + M21 * v.Y + M22 * v.Z);
    }

    /// <summary>The turn back (the transpose, which is the inverse of a rotation).</summary>
    public (float X, float Y, float Z) ApplyInverse((float X, float Y, float Z) v)
    {
        return (M00 * v.X + M10 * v.Y + M20 * v.Z, M01 * v.X + M11 * v.Y + M21 * v.Z, M02 * v.X + M12 * v.Y + M22 * v.Z);
    }
}

/// <summary>
/// The mapping between the real world and the hologram (<see cref="NitroxClient.MonoBehaviours.Cyclops.TerrainHologramAligner" /> runs it), and the ray tests that pick a place on the hologram's terrain, as plain
/// numbers so that they are tested. A real position goes to the hologram by its offset from the pivot (the Cyclops' hull), scaled to the hologram's size and turned by <c>Turn</c>, from the anchor (the middle
/// of the hologram): <c>map = anchor + Turn * ((world - pivot) * scale)</c>. It is a full three-dimensional mapping: a place deeper than the Cyclops is below the hologram's flat reference plane, and
/// <see cref="ToWorld" /> brings any point of the hologram back with its real depth, which is how a click on the relief becomes a place in the water and not on the sea's surface.
/// </summary>
public static class HologramMath
{
    public static (float X, float Y, float Z) ToMap((float X, float Y, float Z) world, (float X, float Y, float Z) pivot, (float X, float Y, float Z) anchor, Rot3 turn, float scale)
    {
        (float X, float Y, float Z) turned = turn.Apply(((world.X - pivot.X) * scale, (world.Y - pivot.Y) * scale, (world.Z - pivot.Z) * scale));
        return (anchor.X + turned.X, anchor.Y + turned.Y, anchor.Z + turned.Z);
    }

    public static (float X, float Y, float Z) ToWorld((float X, float Y, float Z) map, (float X, float Y, float Z) pivot, (float X, float Y, float Z) anchor, Rot3 turn, float scale)
    {
        (float X, float Y, float Z) back = turn.ApplyInverse((map.X - anchor.X, map.Y - anchor.Y, map.Z - anchor.Z));
        return (pivot.X + back.X / scale, pivot.Y + back.Y / scale, pivot.Z + back.Z / scale);
    }

    /// <summary>The metres that a chunk of the terrain hologram covers on a side (32 mesh units of 4 m).</summary>
    public const float CHUNK_METRES = 128f;

    /// <summary>
    /// The chunk of the terrain hologram that a real place is in: the game cuts the world into cubes of <see cref="CHUNK_METRES" /> in the frame of its Voxeland (whose origin is <paramref name="landOrigin" />,
    /// (-2048, -3040, -2048) in the game), and a chunk mesh is named after its cube. <paramref name="edgeDistance" /> is how far the place is from the nearest face of its cube, in metres.
    /// </summary>
    public static (int X, int Y, int Z) ChunkOf((float X, float Y, float Z) world, (float X, float Y, float Z) landOrigin, out float edgeDistance)
    {
        float x = world.X - landOrigin.X;
        float y = world.Y - landOrigin.Y;
        float z = world.Z - landOrigin.Z;
        int cx = (int)Math.Floor(x / CHUNK_METRES);
        int cy = (int)Math.Floor(y / CHUNK_METRES);
        int cz = (int)Math.Floor(z / CHUNK_METRES);
        edgeDistance = Math.Min(Edge(x, cx), Math.Min(Edge(y, cy), Edge(z, cz)));
        return (cx, cy, cz);
    }

    private static float Edge(float local, int cell)
    {
        float inside = local - cell * CHUNK_METRES;
        return Math.Min(inside, CHUNK_METRES - inside);
    }

    /// <summary>The cube in a chunk mesh's name ("Chunk-14-23-20"); false for any other name.</summary>
    public static bool TryParseChunkName(string name, out (int X, int Y, int Z) chunk)
    {
        chunk = default;
        string[] parts = name?.Split('-');
        if (parts == null || parts.Length != 4 || parts[0] != "Chunk" || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y) || !int.TryParse(parts[3], out int z))
        {
            return false;
        }
        chunk = (x, y, z);
        return true;
    }

    /// <summary>The depth in metres of a place in the world: the sea's surface is y 0 and the depth grows downwards.</summary>
    public static float DepthOf(float worldY)
    {
        return -worldY;
    }

    /// <summary>
    /// Where the ray meets a triangle (Moeller and Trumbore; either side, in front of the origin): the distance along the direction (in units of the direction's length). False when it does not, or the
    /// triangle is degenerate.
    /// </summary>
    public static bool RayTriangle((float X, float Y, float Z) origin, (float X, float Y, float Z) direction, (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c, out float t)
    {
        t = 0f;
        float e1x = b.X - a.X, e1y = b.Y - a.Y, e1z = b.Z - a.Z;
        float e2x = c.X - a.X, e2y = c.Y - a.Y, e2z = c.Z - a.Z;
        float px = direction.Y * e2z - direction.Z * e2y;
        float py = direction.Z * e2x - direction.X * e2z;
        float pz = direction.X * e2y - direction.Y * e2x;
        float determinant = e1x * px + e1y * py + e1z * pz;
        if (Math.Abs(determinant) < 1e-12f)
        {
            return false;
        }
        float inverse = 1f / determinant;
        float tx = origin.X - a.X, ty = origin.Y - a.Y, tz = origin.Z - a.Z;
        float u = (tx * px + ty * py + tz * pz) * inverse;
        if (u < 0f || u > 1f)
        {
            return false;
        }
        float qx = ty * e1z - tz * e1y;
        float qy = tz * e1x - tx * e1z;
        float qz = tx * e1y - ty * e1x;
        float v = (direction.X * qx + direction.Y * qy + direction.Z * qz) * inverse;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }
        float distance = (e2x * qx + e2y * qy + e2z * qz) * inverse;
        if (distance <= 0f)
        {
            return false;
        }
        t = distance;
        return true;
    }

    /// <summary>
    /// The part of the ray that is inside a sphere: the distances (in units of the direction's length) at which it enters and leaves, the entry clamped to 0 when the origin is inside. False when it misses the sphere or
    /// the sphere is behind the origin. The hologram shows terrain inside a sphere only, and a pick must not meet the triangles of the chunks outside it, which are not drawn.
    /// </summary>
    public static bool RaySphere((float X, float Y, float Z) origin, (float X, float Y, float Z) direction, (float X, float Y, float Z) center, float radius, out float entry, out float exit)
    {
        entry = 0f;
        exit = 0f;
        float ox = origin.X - center.X, oy = origin.Y - center.Y, oz = origin.Z - center.Z;
        float a = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
        if (a < 1e-12f || radius <= 0f)
        {
            return false;
        }
        float b = 2f * (ox * direction.X + oy * direction.Y + oz * direction.Z);
        float c = ox * ox + oy * oy + oz * oz - radius * radius;
        float discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
        {
            return false;
        }
        float root = (float)Math.Sqrt(discriminant);
        float t0 = (-b - root) / (2f * a);
        float t1 = (-b + root) / (2f * a);
        if (t1 < 0f)
        {
            return false;
        }
        entry = Math.Max(t0, 0f);
        exit = t1;
        return true;
    }

    /// <summary>Whether the ray meets the box (the slab test): the distance at which it enters it, 0 when the origin is inside. False when it misses or the box is behind the origin.</summary>
    public static bool RayBox((float X, float Y, float Z) origin, (float X, float Y, float Z) direction, (float X, float Y, float Z) min, (float X, float Y, float Z) max, out float t)
    {
        t = 0f;
        float near = 0f;
        float far = float.MaxValue;
        if (!Slab(origin.X, direction.X, min.X, max.X, ref near, ref far) || !Slab(origin.Y, direction.Y, min.Y, max.Y, ref near, ref far) || !Slab(origin.Z, direction.Z, min.Z, max.Z, ref near, ref far))
        {
            return false;
        }
        t = near;
        return near <= far;
    }

    private static bool Slab(float origin, float direction, float min, float max, ref float near, ref float far)
    {
        if (Math.Abs(direction) < 1e-12f)
        {
            return origin >= min && origin <= max;
        }
        float t1 = (min - origin) / direction;
        float t2 = (max - origin) / direction;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }
        near = Math.Max(near, t1);
        far = Math.Min(far, t2);
        return near <= far;
    }
}
