using System.Globalization;
using System.Numerics;
using System.Text;
using Newtonsoft.Json.Linq;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// Models for the tests that need a whole Mixamo skeleton: a GLB is built from the node tables of <see cref="RigFixtureData" /> (the skeleton, a skin over all its joints with the right inverse bind matrices and one
/// skinned triangle), so that nothing is read from a model file. The game ships no model.
/// </summary>
internal static class RigFixture
{
    /// <summary>The skeleton that the pose-copy tests use.</summary>
    public static GlbModel Reference() => Model(RigFixtureData.REFERENCE);

    /// <summary>A Mixamo rig with other proportions and a T pose.</summary>
    public static GlbModel Alternate() => Model(RigFixtureData.ALTERNATE);

    /// <summary>A Mixamo rig that has helper bones the reference does not.</summary>
    public static GlbModel Extras() => Model(RigFixtureData.EXTRAS);

    public static PlayerModelRest ReferenceRest() => PlayerModelRest.FromGlb(Reference());

    public static PlayerModelRest AlternateRest() => PlayerModelRest.FromGlb(Alternate());

    public static PlayerModelRest ExtrasRest() => PlayerModelRest.FromGlb(Extras());

    public static GlbModel Model(string table) => GlbModel.Read(GlbDocument.Parse(Build(table)));

    public static byte[] Build(string table)
    {
        string[] lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int count = lines.Length;
        string[] names = new string[count];
        int[] parents = new int[count];
        float[][] translation = new float[count][];
        float[][] rotation = new float[count][];
        float[][] scale = new float[count][];
        bool[] carriesMesh = new bool[count];
        for (int i = 0; i < count; i++)
        {
            string[] parts = lines[i].Split('|');
            names[i] = parts[0];
            parents[i] = int.Parse(parts[1], CultureInfo.InvariantCulture);
            translation[i] = Numbers(parts[2]);
            rotation[i] = Numbers(parts[3]);
            scale[i] = Numbers(parts[4]);
            carriesMesh[i] = parts[5] == "M";
        }
        int meshNode = Array.IndexOf(carriesMesh, true);
        int[] joints = Enumerable.Range(0, count).Where(i => !carriesMesh[i] && parents[i] >= 0).ToArray();

        Matrix4x4[] world = new Matrix4x4[count];
        bool[] done = new bool[count];
        Matrix4x4 World(int node)
        {
            if (done[node])
            {
                return world[node];
            }
            Matrix4x4 local = Matrix4x4.CreateScale(scale[node][0], scale[node][1], scale[node][2])
                              * Matrix4x4.CreateFromQuaternion(new Quaternion(rotation[node][0], rotation[node][1], rotation[node][2], rotation[node][3]))
                              * Matrix4x4.CreateTranslation(translation[node][0], translation[node][1], translation[node][2]);
            world[node] = parents[node] < 0 ? local : local * World(parents[node]);
            done[node] = true;
            return world[node];
        }

        using MemoryStream bin = new();
        using BinaryWriter writer = new(bin);
        JArray views = [];
        JArray accessors = [];

        int AddAccessor(byte[] bytes, int componentType, int accessorCount, string type, float[]? min = null, float[]? max = null)
        {
            while (bin.Length % 4 != 0)
            {
                writer.Write((byte)0);
            }
            int offset = (int)bin.Length;
            writer.Write(bytes);
            views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length });
            JObject accessor = new() { ["bufferView"] = views.Count - 1, ["componentType"] = componentType, ["count"] = accessorCount, ["type"] = type };
            if (min != null)
            {
                accessor["min"] = new JArray(min);
            }
            if (max != null)
            {
                accessor["max"] = new JArray(max);
            }
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        static byte[] Floats(IEnumerable<float> values) => values.SelectMany(BitConverter.GetBytes).ToArray();

        float[] inverseBinds = joints.SelectMany(joint =>
        {
            Matrix4x4.Invert(World(joint), out Matrix4x4 inverse);
            Matrix4x4 m = World(meshNode) * inverse;
            return new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
        }).ToArray();

        int position = AddAccessor(Floats([0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f]), 5126, 3, "VEC3", [0, 0, 0], [1, 1, 0]);
        int normal = AddAccessor(Floats([0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f]), 5126, 3, "VEC3");
        int uv = AddAccessor(Floats([0f, 0f, 1f, 0f, 0f, 1f]), 5126, 3, "VEC2");
        int jointIds = AddAccessor(new byte[12], 5121, 3, "VEC4");
        int weights = AddAccessor(Floats([1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f]), 5126, 3, "VEC4");
        int indices = AddAccessor([0, 0, 1, 0, 2, 0], 5123, 3, "SCALAR");
        int inverseBind = AddAccessor(Floats(inverseBinds), 5126, joints.Length, "MAT4");

        JArray nodes = [];
        for (int i = 0; i < count; i++)
        {
            JObject node = new() { ["name"] = names[i], ["translation"] = new JArray(translation[i]), ["rotation"] = new JArray(rotation[i]), ["scale"] = new JArray(scale[i]) };
            int[] children = Enumerable.Range(0, count).Where(child => parents[child] == i).ToArray();
            if (children.Length > 0)
            {
                node["children"] = new JArray(children);
            }
            if (i == meshNode)
            {
                node["mesh"] = 0;
                node["skin"] = 0;
            }
            nodes.Add(node);
        }

        JObject json = new()
        {
            ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "RigFixture" },
            ["scene"] = 0,
            ["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(Enumerable.Range(0, count).Where(i => parents[i] < 0).ToArray()) }),
            ["nodes"] = nodes,
            ["skins"] = new JArray(new JObject { ["joints"] = new JArray(joints), ["inverseBindMatrices"] = inverseBind }),
            ["meshes"] = new JArray(new JObject
            {
                ["primitives"] = new JArray(new JObject
                {
                    ["attributes"] = new JObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv, ["JOINTS_0"] = jointIds, ["WEIGHTS_0"] = weights },
                    ["indices"] = indices
                })
            }),
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JArray(new JObject { ["byteLength"] = (int)bin.Length })
        };
        return SyntheticGlb.Assemble(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)), bin.ToArray());
    }

    private static float[] Numbers(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();
}
