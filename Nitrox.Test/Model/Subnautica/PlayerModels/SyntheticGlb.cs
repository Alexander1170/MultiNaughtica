using System.Text;
using Newtonsoft.Json.Linq;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// A tiny GLB for the tests of the reader: a skinned triangle on a chain of two bones under a root, a material with three images and one animation. Built with the byte layout of the glTF 2.0 binary
/// format (docs/coop/PLAYER_MODELS.md), so that the reader is tested without a model file.
/// </summary>
internal static class SyntheticGlb
{
    public static readonly byte[] BaseColorBytes = [1, 2, 3, 4, 5, 6, 7, 8];
    public static readonly byte[] NormalBytes = [9, 10, 11, 12, 13];
    public static readonly byte[] MetallicRoughnessBytes = [20, 21, 22];

    /// <summary>The GLB; <paramref name="tweak" /> may change the JSON before it is written (to make a damaged or unsupported file).</summary>
    public static byte[] Build(Action<JObject>? tweak = null)
    {
        using MemoryStream bin = new();
        using BinaryWriter writer = new(bin);
        JArray views = [];
        JArray accessors = [];

        int AddView(byte[] bytes)
        {
            while (bin.Length % 4 != 0)
            {
                writer.Write((byte)0);
            }
            int offset = (int)bin.Length;
            writer.Write(bytes);
            views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = bytes.Length });
            return views.Count - 1;
        }

        int AddAccessor(byte[] bytes, int componentType, int count, string type, float[]? min = null, float[]? max = null, bool normalized = false)
        {
            JObject accessor = new() { ["bufferView"] = AddView(bytes), ["componentType"] = componentType, ["count"] = count, ["type"] = type };
            if (min != null)
            {
                accessor["min"] = new JArray(min);
            }
            if (max != null)
            {
                accessor["max"] = new JArray(max);
            }
            if (normalized)
            {
                accessor["normalized"] = true;
            }
            accessors.Add(accessor);
            return accessors.Count - 1;
        }

        static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
        static byte[] UShorts(params ushort[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

        int position = AddAccessor(Floats(0, 0, 0, 1, 0, 0, 0, 1, 0), 5126, 3, "VEC3", [0, 0, 0], [1, 1, 0]);
        int normal = AddAccessor(Floats(0, 0, 1, 0, 0, 1, 0, 0, 1), 5126, 3, "VEC3");
        int uv = AddAccessor(Floats(0, 0, 1, 0, 0, 1), 5126, 3, "VEC2");
        int joints = AddAccessor([0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], 5121, 3, "VEC4");
        int weights = AddAccessor(Floats(1, 0, 0, 0, 1, 0, 0, 0, 0.5f, 0.5f, 0, 0), 5126, 3, "VEC4");
        int indices = AddAccessor(UShorts(0, 1, 2), 5123, 3, "SCALAR");
        int inverseBind = AddAccessor(Floats(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -2, 0, 1), 5126, 2, "MAT4");
        int times = AddAccessor(Floats(0, 1), 5126, 2, "SCALAR", [0], [1]);
        int rotations = AddAccessor(Floats(0, 0, 0, 1, 0, 0.70710678f, 0, 0.70710678f), 5126, 2, "VEC4");
        int normalizedWeights = AddAccessor([255, 0, 0, 0], 5121, 1, "VEC4", normalized: true);
        int baseColorView = AddView(BaseColorBytes);
        int normalView = AddView(NormalBytes);
        int metallicView = AddView(MetallicRoughnessBytes);

        JObject json = new()
        {
            ["asset"] = new JObject { ["version"] = "2.0", ["generator"] = "SyntheticGlb" },
            ["scene"] = 0,
            ["scenes"] = new JArray(new JObject { ["nodes"] = new JArray(0) }),
            ["nodes"] = new JArray(
                new JObject { ["name"] = "root", ["children"] = new JArray(1, 3) },
                new JObject { ["name"] = "boneA", ["translation"] = new JArray(0, 1, 0), ["children"] = new JArray(2) },
                new JObject { ["name"] = "boneB", ["translation"] = new JArray(0, 1, 0), ["rotation"] = new JArray(0, 0.70710678, 0, 0.70710678) },
                new JObject { ["name"] = "body", ["mesh"] = 0, ["skin"] = 0 }),
            ["skins"] = new JArray(new JObject { ["joints"] = new JArray(1, 2), ["inverseBindMatrices"] = inverseBind }),
            ["meshes"] = new JArray(new JObject
            {
                ["primitives"] = new JArray(new JObject
                {
                    ["attributes"] = new JObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights },
                    ["indices"] = indices,
                    ["material"] = 0
                })
            }),
            ["materials"] = new JArray(new JObject
            {
                ["doubleSided"] = true,
                ["normalTexture"] = new JObject { ["index"] = 1 },
                ["pbrMetallicRoughness"] = new JObject
                {
                    ["baseColorTexture"] = new JObject { ["index"] = 0 },
                    ["metallicRoughnessTexture"] = new JObject { ["index"] = 2 },
                    ["metallicFactor"] = 0.5,
                    ["baseColorFactor"] = new JArray(1, 0.5, 0.25, 1)
                }
            }),
            ["textures"] = new JArray(new JObject { ["source"] = 0 }, new JObject { ["source"] = 1 }, new JObject { ["source"] = 2 }),
            ["images"] = new JArray(
                new JObject { ["bufferView"] = baseColorView, ["mimeType"] = "image/png" },
                new JObject { ["bufferView"] = normalView, ["mimeType"] = "image/jpeg" },
                new JObject { ["bufferView"] = metallicView, ["mimeType"] = "image/jpeg" }),
            ["animations"] = new JArray(new JObject
            {
                ["name"] = "Wave",
                ["channels"] = new JArray(new JObject { ["sampler"] = 0, ["target"] = new JObject { ["node"] = 1, ["path"] = "rotation" } }),
                ["samplers"] = new JArray(new JObject { ["input"] = times, ["output"] = rotations, ["interpolation"] = "LINEAR" })
            }),
            ["accessors"] = accessors,
            ["bufferViews"] = views,
            ["buffers"] = new JArray(new JObject { ["byteLength"] = (int)bin.Length })
        };
        json["normalizedWeightsAccessor"] = normalizedWeights;
        tweak?.Invoke(json);
        return Assemble(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)), bin.ToArray());
    }

    /// <summary>The GLB container: header, a JSON chunk and a binary chunk, each padded to four bytes.</summary>
    public static byte[] Assemble(byte[] json, byte[] bin)
    {
        int jsonPadded = (json.Length + 3) & ~3;
        int binPadded = (bin.Length + 3) & ~3;
        int total = 12 + 8 + jsonPadded + 8 + binPadded;
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)total);
        writer.Write((uint)jsonPadded);
        writer.Write(0x4E4F534Au);
        writer.Write(json);
        for (int i = json.Length; i < jsonPadded; i++)
        {
            writer.Write((byte)' ');
        }
        writer.Write((uint)binPadded);
        writer.Write(0x004E4942u);
        writer.Write(bin);
        for (int i = bin.Length; i < binPadded; i++)
        {
            writer.Write((byte)0);
        }
        return stream.ToArray();
    }
}
