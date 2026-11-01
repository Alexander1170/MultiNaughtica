using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NitroxClient.GameLogic.PlayerModels;

/// <summary>
/// How the normal map of the model is handed to the game's shader: the plain RGB of the file (what the game's MarmosetUBER shader reads: the live check of 2026-10-29 shows the right armour with it), or the
/// DXT5nm layout that Unity's own shaders unpack (x in the alpha channel, y in the green one), which makes the lighting of MarmosetUBER blotchy. The second one stays for the Debug command <c>vumodel normal</c>.
/// </summary>
public enum NormalMapEncoding
{
    Dxt5nm,
    Rgb
}

/// <summary>
/// A model that was read from its file and turned into what the game needs (docs/coop/PLAYER_MODELS.md): the mesh in the game's space with its bone weights and bind poses, the three textures, and the model
/// as it was read. One per id, shared by every copy that is put in the world (<see cref="PlayerModelBuilder.Instantiate" />).
/// </summary>
public sealed class LoadedPlayerModel
{
    public LoadedPlayerModel(string id, string path, long fileBytes, GlbModel glb, Mesh mesh, Texture2D baseColor, Texture2D normal, Texture2D metallicRoughness, NormalMapEncoding encoding)
    {
        Id = id;
        Path = path;
        FileBytes = fileBytes;
        Glb = glb;
        Mesh = mesh;
        BaseColor = baseColor;
        Normal = normal;
        MetallicRoughness = metallicRoughness;
        Encoding = encoding;
    }

    public string Id { get; }

    public string Path { get; }

    /// <summary>The length of the file: it travels with the id, so that two players who have a file of the same name but not the same file notice.</summary>
    public long FileBytes { get; }

    public GlbModel Glb { get; }

    public Mesh Mesh { get; }

    public Texture2D BaseColor { get; }

    public Texture2D Normal { get; }

    public Texture2D MetallicRoughness { get; }

    public NormalMapEncoding Encoding { get; }

    /// <summary>The height of the mesh in metres (glTF units are metres).</summary>
    public float Height => Glb.BoundsMax[1] - Glb.BoundsMin[1];

    private PlayerModelRest rest;

    /// <summary>The rest pose of the skeleton in the game's space (worked out the first time it is asked for): what the calibration of the pose copy and of the hands work on.</summary>
    public PlayerModelRest Rest => rest ??= PlayerModelRest.FromGlb(Glb);

    /// <summary>What the profile beside the file says about this model (the default one when there is none): the library sets it when the model is loaded.</summary>
    public PlayerModelProfile Profile { get; set; } = PlayerModelProfile.Default;

    private PlayerModelEmoteRetarget emoteRetarget;
    private bool emoteRetargetTried;

    /// <summary>
    /// The retarget that plays the clips of <paramref name="set" /> (the shared emotes) on this model's bones, made the first time it is asked for. Null, with a line in the log, when the two skeletons share too few
    /// bones.
    /// </summary>
    internal PlayerModelEmoteRetarget RetargetFrom(SharedEmoteSet set)
    {
        if (!emoteRetargetTried)
        {
            emoteRetargetTried = true;
            emoteRetarget = PlayerModelEmoteRetarget.Create(set.Rest, set.Names, Rest, Profile.BoneNames, out string reason);
            if (emoteRetarget == null)
            {
                Log.Warn($"[PlayerModels] the emotes of '{set.SourceId}' cannot be played on '{Id}': {reason}");
            }
            else
            {
                Log.Info($"[PlayerModels] the emotes of '{set.SourceId}' are played on '{Id}' by {emoteRetarget.MappedBones} bones");
            }
        }
        return emoteRetarget;
    }

    private readonly System.Collections.Generic.Dictionary<string, GlbClipData> clipCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The keys of the clip with this name (an emote), read the first time they are asked for and kept; null when the model has no clip of that name or it cannot be read (the reason is in the log). One
    /// read takes a few milliseconds on the main thread.
    /// </summary>
    public GlbClipData Clip(string name)
    {
        if (clipCache.TryGetValue(name, out GlbClipData cached))
        {
            return cached;
        }
        GlbClipData data = null;
        int index = Glb.FindClip(name);
        if (index >= 0)
        {
            try
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                data = Glb.ReadClipData(index);
                Log.Info($"[PlayerModels] read the clip '{name}' of '{Id}': {data.Tracks.Count} tracks, {data.Duration:F2} s, in {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"[PlayerModels] the clip '{name}' of '{Id}' could not be read");
            }
        }
        clipCache[name] = data;
        return data;
    }

    public void Destroy()
    {
        foreach (Object asset in new Object[] { Mesh, BaseColor, Normal, MetallicRoughness })
        {
            if (asset)
            {
                Object.Destroy(asset);
            }
        }
    }
}

/// <summary>The outcome of <see cref="PlayerModelBuilder.LoadSteps" />: the model, or why there is none.</summary>
public sealed class ModelLoadResult
{
    public LoadedPlayerModel Model { get; set; }

    public string Failure { get; set; } = "";
}

/// <summary>
/// Builds what the game needs from a <see cref="GlbModel" />:<see cref="Load" /> makes the shared assets (the mesh, the textures), <see cref="Instantiate" /> a copy in the world (the skeleton as a tree of
/// transforms and a <see cref="SkinnedMeshRenderer" /> on the node that carries the mesh). The space is converted with <see cref="GlbSpace" /> (a mirror of the X axis, so that the character keeps facing +Z).
/// The material is a copy of one that the game already uses for the body of a player (the dive suit), so that the shader, its keywords and the way it takes the light of the sea and of the bases stay as the
/// game has them; only the textures change.
/// </summary>
public static class PlayerModelBuilder
{
    /// <summary>How the normal map is encoded for the material (see <see cref="NormalMapEncoding" />); the Debug command <c>vumodel</c> changes it to compare.</summary>
    public static NormalMapEncoding DefaultEncoding { get; set; } = NormalMapEncoding.Rgb;

    public static LoadedPlayerModel Load(string id, string path)
    {
        long fileBytes = new FileInfo(path).Length;
        GlbModel glb = GlbModel.Read(GlbDocument.Load(path));
        Mesh mesh = BuildMesh(id, glb);
        NormalMapEncoding encoding = DefaultEncoding;
        Texture2D baseColor = glb.BaseColorImage >= 0 ? Decode(glb.Document.ReadImage(glb.BaseColorImage), $"{id}_color", false) : null;
        Texture2D normal = glb.NormalImage >= 0 ? Decode(glb.Document.ReadImage(glb.NormalImage), $"{id}_normal", true) : null;
        Texture2D metallicRoughness = glb.MetallicRoughnessImage >= 0 ? Decode(glb.Document.ReadImage(glb.MetallicRoughnessImage), $"{id}_metallic_roughness", true) : null;
        if (normal && encoding == NormalMapEncoding.Dxt5nm)
        {
            ConvertToDxt5nm(normal);
        }
        string textures = $"{Describe(baseColor)} {Describe(normal)} {Describe(metallicRoughness)}";
        float height = glb.BoundsMax[1] - glb.BoundsMin[1];
        Log.Info($"[PlayerModels] loaded '{id}': {glb.VertexCount} vertices, {glb.TriangleCount} triangles, {glb.SkinJoints.Length} bones, {glb.Clips.Count} clips, height {height:0.00} m, textures {textures}, normal map {encoding}");
        return new LoadedPlayerModel(id, path, fileBytes, glb, mesh, baseColor, normal, metallicRoughness, encoding);
    }

    /// <summary>
    /// <see cref="Load" /> in steps, for a model that is wanted while the game runs (a player who wears one joins, or picks one): an enumerator for a coroutine that yields between the steps. The file is read and
    /// parsed on another thread (nothing of Unity is touched there); the mesh and each of the textures are made on the main thread, one in each frame, so that no frame has to do more than the longest of them
    /// (a 2048 pixel JPEG, about a tenth of a second, which is logged). <paramref name="result" /> holds the model when the enumerator ends, or the reason why there is none.
    /// </summary>
    public static IEnumerator LoadSteps(string id, string path, ModelLoadResult result)
    {
        System.Diagnostics.Stopwatch wall = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Stopwatch step = new();
        long fileBytes;
        try
        {
            fileBytes = new FileInfo(path).Length;
        }
        catch (Exception exception)
        {
            result.Failure = exception.Message;
            yield break;
        }

        Task<GlbModel> parse = Task.Run(() => GlbModel.Read(GlbDocument.Load(path)));
        int frames = 0;
        while (!parse.IsCompleted)
        {
            frames++;
            yield return null;
        }
        long parseMilliseconds = wall.ElapsedMilliseconds;
        if (parse.IsFaulted)
        {
            result.Failure = parse.Exception.GetBaseException().Message;
            yield break;
        }
        GlbModel glb = parse.Result;
        NormalMapEncoding encoding = DefaultEncoding;
        long longest = 0;

        string failure = null;
        Mesh mesh = null;
        step.Restart();
        try
        {
            mesh = BuildMesh(id, glb);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
        }
        long meshMilliseconds = step.ElapsedMilliseconds;
        longest = Math.Max(longest, meshMilliseconds);
        if (failure != null)
        {
            result.Failure = failure;
            yield break;
        }
        yield return null;

        (Texture2D baseColor, long colorMilliseconds, string colorFailure) = TimedDecode(glb, glb.BaseColorImage, $"{id}_color", false);
        longest = Math.Max(longest, colorMilliseconds);
        yield return null;
        (Texture2D normal, long normalMilliseconds, string normalFailure) = TimedDecode(glb, glb.NormalImage, $"{id}_normal", true);
        longest = Math.Max(longest, normalMilliseconds);
        yield return null;
        if (normal && encoding == NormalMapEncoding.Dxt5nm)
        {
            step.Restart();
            ConvertToDxt5nm(normal);
            longest = Math.Max(longest, step.ElapsedMilliseconds);
            yield return null;
        }
        (Texture2D metallicRoughness, long metallicMilliseconds, string metallicFailure) = TimedDecode(glb, glb.MetallicRoughnessImage, $"{id}_metallic_roughness", true);
        longest = Math.Max(longest, metallicMilliseconds);
        yield return null;

        failure = colorFailure ?? normalFailure ?? metallicFailure;
        if (failure != null)
        {
            foreach (Object asset in new Object[] { mesh, baseColor, normal, metallicRoughness })
            {
                if (asset)
                {
                    Object.Destroy(asset);
                }
            }
            result.Failure = failure;
            yield break;
        }

        float height = glb.BoundsMax[1] - glb.BoundsMin[1];
        result.Model = new LoadedPlayerModel(id, path, fileBytes, glb, mesh, baseColor, normal, metallicRoughness, encoding);
        StringBuilder timing = new();
        timing.Append($"[PlayerModels] loaded '{id}' in steps: {glb.VertexCount} vertices, {glb.SkinJoints.Length} bones, height {height:0.00} m, {wall.ElapsedMilliseconds} ms in all over {frames + 6} frames ");
        timing.Append($"(file and JSON {parseMilliseconds} ms off the main thread, mesh {meshMilliseconds} ms, colour {colorMilliseconds} ms, normal {normalMilliseconds} ms, metallic {metallicMilliseconds} ms), ");
        timing.Append($"longest step on the main thread {longest} ms");
        Log.Info(timing.ToString());
    }

    private static (Texture2D Texture, long Milliseconds, string Failure) TimedDecode(GlbModel glb, int image, string name, bool linear)
    {
        if (image < 0)
        {
            return (null, 0, null);
        }
        System.Diagnostics.Stopwatch step = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Texture2D texture = Decode(glb.Document.ReadImage(image), name, linear);
            return (texture, step.ElapsedMilliseconds, null);
        }
        catch (Exception exception)
        {
            return (null, step.ElapsedMilliseconds, $"{name}: {exception.Message}");
        }
    }

    private static string Describe(Texture2D texture) => texture ? $"{texture.width}x{texture.height}" : "none";

    private static Mesh BuildMesh(string id, GlbModel glb)
    {
        int count = glb.VertexCount;
        Vector3[] vertices = new Vector3[count];
        Vector3[] normals = glb.Normals.Length > 0 ? new Vector3[count] : null;
        Vector2[] uvs = glb.Uvs.Length > 0 ? new Vector2[count] : null;
        BoneWeight[] weights = new BoneWeight[count];
        for (int i = 0; i < count; i++)
        {
            GlbSpace.Vector(glb.Positions[i * 3], glb.Positions[i * 3 + 1], glb.Positions[i * 3 + 2], out float x, out float y, out float z);
            vertices[i] = new Vector3(x, y, z);
            if (normals != null)
            {
                GlbSpace.Vector(glb.Normals[i * 3], glb.Normals[i * 3 + 1], glb.Normals[i * 3 + 2], out float nx, out float ny, out float nz);
                normals[i] = new Vector3(nx, ny, nz);
            }
            if (uvs != null)
            {
                uvs[i] = new Vector2(glb.Uvs[i * 2], GlbSpace.V(glb.Uvs[i * 2 + 1]));
            }
            weights[i] = WeightsOf(glb, i);
        }

        Matrix4x4[] bindPoses = new Matrix4x4[glb.SkinJoints.Length];
        for (int joint = 0; joint < bindPoses.Length; joint++)
        {
            float[] converted = GlbSpace.Matrix(glb.InverseBindMatrices, joint * 16);
            Matrix4x4 matrix = new();
            for (int column = 0; column < 4; column++)
            {
                matrix.SetColumn(column, new Vector4(converted[column * 4], converted[column * 4 + 1], converted[column * 4 + 2], converted[column * 4 + 3]));
            }
            bindPoses[joint] = matrix;
        }

        Mesh mesh = new() { name = id, indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = vertices;
        if (uvs != null)
        {
            mesh.uv = uvs;
        }
        mesh.triangles = GlbSpace.ReverseWinding((int[])glb.Indices.Clone());
        if (normals != null)
        {
            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }
        mesh.boneWeights = weights;
        mesh.bindposes = bindPoses;
        mesh.RecalculateBounds();
        if (uvs != null)
        {
            mesh.RecalculateTangents();
        }
        return mesh;
    }

    /// <summary>The four influences of a vertex, the strongest first, and a joint of 0 for a weight of 0 (what Unity expects).</summary>
    private static BoneWeight WeightsOf(GlbModel glb, int vertex)
    {
        (int Joint, float Weight)[] influences = Enumerable.Range(0, 4)
                                                          .Select(slot => ((int)glb.Joints[vertex * 4 + slot], glb.Weights[vertex * 4 + slot]))
                                                          .OrderByDescending(influence => influence.Item2)
                                                          .ToArray();
        float sum = influences.Sum(influence => influence.Weight);
        float scale = sum > 0f ? 1f / sum : 1f;
        BoneWeight weight = new();
        weight.boneIndex0 = influences[0].Weight > 0f ? influences[0].Joint : 0;
        weight.weight0 = influences[0].Weight * scale;
        weight.boneIndex1 = influences[1].Weight > 0f ? influences[1].Joint : 0;
        weight.weight1 = influences[1].Weight * scale;
        weight.boneIndex2 = influences[2].Weight > 0f ? influences[2].Joint : 0;
        weight.weight2 = influences[2].Weight * scale;
        weight.boneIndex3 = influences[3].Weight > 0f ? influences[3].Joint : 0;
        weight.weight3 = influences[3].Weight * scale;
        return weight;
    }

    /// <summary>Decodes a JPEG or PNG of the file (the game's own image decoder). Null (and a warning) when it cannot be read.</summary>
    private static Texture2D Decode(byte[] bytes, string name, bool linear)
    {
        Texture2D texture = new(2, 2, TextureFormat.RGBA32, true, linear) { name = name };
        if (!texture.LoadImage(bytes, false))
        {
            Log.Warn($"[PlayerModels] the image {name} could not be decoded");
            Object.Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;
        return texture;
    }

    /// <summary>The layout that Unity's shaders unpack as a normal map (<c>UnpackNormal</c> reads x from the alpha channel and y from the green one): (1, y, 1, x).</summary>
    private static void ConvertToDxt5nm(Texture2D texture)
    {
        Color32[] pixels = texture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            pixels[i] = new Color32(255, pixel.g, 255, pixel.r);
        }
        texture.SetPixels32(pixels);
        texture.Apply(true);
    }

    /// <summary>
    /// A copy of the model in the world under <paramref name="parent" />: the skeleton as transforms (names, hierarchy and pose of the file, converted), the skinned mesh on the node that carries it. The model
    /// stands where the file has it (feet at y = 0, facing +Z of the parent). <paramref name="materialSource" /> is the material of the game whose shader and settings the copy takes over.
    /// </summary>
    public static GameObject Instantiate(LoadedPlayerModel model, Transform parent, Material materialSource) => Instantiate(model, parent, materialSource, out _);

    /// <summary>As <see cref="Instantiate(LoadedPlayerModel, Transform, Material)" />, and the transform of every node of the file: <paramref name="nodes" />[i] belongs to <see cref="GlbModel.Nodes" />[i].</summary>
    public static GameObject Instantiate(LoadedPlayerModel model, Transform parent, Material materialSource, out Transform[] nodes)
    {
        GlbModel glb = model.Glb;
        GameObject root = new($"CustomPlayerModel_{model.Id}");
        root.transform.SetParent(parent, false);

        Transform[] all = new Transform[glb.Nodes.Count];
        nodes = all;
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = new GameObject(glb.Nodes[i].Name).transform;
        }
        for (int i = 0; i < all.Length; i++)
        {
            GlbNode node = glb.Nodes[i];
            all[i].SetParent(node.Parent < 0 ? root.transform : all[node.Parent], false);
            GlbSpace.Vector(node.Translation[0], node.Translation[1], node.Translation[2], out float x, out float y, out float z);
            GlbSpace.Rotation(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3], out float rx, out float ry, out float rz, out float rw);
            all[i].localPosition = new Vector3(x, y, z);
            all[i].localRotation = new Quaternion(rx, ry, rz, rw);
            all[i].localScale = new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]);
        }

        SkinnedMeshRenderer renderer = all[glb.MeshNode].gameObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = model.Mesh;
        renderer.bones = glb.SkinJoints.Select(joint => all[joint]).ToArray();
        renderer.rootBone = all[glb.SkinJoints[0]];
        renderer.sharedMaterial = CreateMaterial(model, materialSource);
        renderer.quality = SkinQuality.Bone4;
        renderer.updateWhenOffscreen = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return root;
    }

    /// <summary>The material of the dive suit body of the local player's body prototype: what the materials of the models are copied from. Null before the prototype exists.</summary>
    public static Material SuitMaterial()
    {
        GameObject prototype = NitroxServiceLocator.LocateService<LocalPlayer>().BodyPrototype;
        if (!prototype)
        {
            return null;
        }
        Transform suit = prototype.transform.Find($"player_view/{PlayerEquipmentConstants.DIVE_SUIT_GAME_OBJECT_NAME}");
        SkinnedMeshRenderer renderer = suit ? suit.GetComponent<SkinnedMeshRenderer>() : prototype.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
        return renderer && renderer.sharedMaterial ? renderer.sharedMaterial : null;
    }

    /// <summary>A copy of the game's material with the textures of the model: colour, normal and a flat, mild specular (the metallic-roughness map is turned into the game's specular map in a later step).</summary>
    public static Material CreateMaterial(LoadedPlayerModel model, Material source)
    {
        Material material = new(source) { name = $"CustomPlayerModel_{model.Id}" };
        if (model.BaseColor)
        {
            material.SetTexture("_MainTex", model.BaseColor);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(model.Glb.BaseColorFactor[0], model.Glb.BaseColorFactor[1], model.Glb.BaseColorFactor[2], 1f));
        }
        if (material.HasProperty("_BumpMap"))
        {
            // a model without a normal map (many models have only a colour map) must not keep the bumps of the dive suit that the material was copied from
            material.SetTexture("_BumpMap", model.Normal ? model.Normal : FlatNormal(model.Encoding));
        }
        if (material.HasProperty("_SpecTex"))
        {
            material.SetTexture("_SpecTex", FlatSpecular());
        }
        if (material.HasProperty("_Illum"))
        {
            material.SetTexture("_Illum", Texture2D.blackTexture);
        }
        return material;
    }

    private static Texture2D flatNormal;
    private static NormalMapEncoding flatNormalEncoding;

    /// <summary>A 4 by 4 normal map that says "straight out" everywhere, in the encoding that the material reads (see <see cref="NormalMapEncoding" />).</summary>
    private static Texture2D FlatNormal(NormalMapEncoding encoding)
    {
        if (!flatNormal || flatNormalEncoding != encoding)
        {
            if (flatNormal)
            {
                Object.Destroy(flatNormal);
            }
            flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "CustomPlayerModel_flat_normal" };
            Color32 pixel = encoding == NormalMapEncoding.Dxt5nm ? new Color32(255, 128, 255, 128) : new Color32(128, 128, 255, 255);
            flatNormal.SetPixels32(Enumerable.Repeat(pixel, 16).ToArray());
            flatNormal.Apply(false);
            flatNormalEncoding = encoding;
        }
        return flatNormal;
    }

    private static Texture2D flatSpecular;

    /// <summary>A 4 by 4 map of a mild grey specular colour and a middling gloss in the alpha channel.</summary>
    private static Texture2D FlatSpecular()
    {
        if (!flatSpecular)
        {
            flatSpecular = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "CustomPlayerModel_flat_specular" };
            Color32[] pixels = Enumerable.Repeat(new Color32(40, 40, 40, 110), 16).ToArray();
            flatSpecular.SetPixels32(pixels);
            flatSpecular.Apply(false);
        }
        return flatSpecular;
    }
}
