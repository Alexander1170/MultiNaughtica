using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// The emote clips of one model played on another model that has the same skeleton (docs/coop/PLAYER_MODELS.md, "Shared emotes"): a model that brings no emotes of its own plays the ones of the source model that its profile names. The
/// clip says how each bone of the <b>source</b> model is turned over time; here every bone that both skeletons have (the body and the fingers, by name) gets the world rotation that the source bone has, times a
/// constant that lines the two rest poses up, the way the pose copy follows the vanilla body (<see cref="PlayerModelRig.Constant" />). So a raised arm is raised by the same angle in the world on a model whose
/// rest pose has its arms lower, and the bone lengths and the rest poses may differ. The bones that only the target has (hair, a skirt) stay at their rest pose in the frame of their parent; the height of the hips
/// is the clip's, scaled to the height of the target's hips. All plain numbers (System.Numerics), tested without the game.
/// </summary>
public sealed class PlayerModelEmoteRetarget
{
    /// <summary>Fewer shared bones than this and the two skeletons are not the same kind of skeleton: nothing is retargeted.</summary>
    public const int MIN_MAPPED_BONES = 15;

    private readonly struct Mapping
    {
        public Mapping(int source, int target, Quaternion k)
        {
            Source = source;
            Target = target;
            K = k;
        }

        public int Source { get; }

        public int Target { get; }

        public Quaternion K { get; }
    }

    private readonly Mapping[] mappings;
    private readonly int[] targetToMapping;
    private readonly int[] sourceOrder;
    private readonly int[] targetOrder;
    private readonly int[] sourceParents;
    private readonly int[] targetParents;
    private readonly Quaternion[] sourceRestLocal;
    private readonly Quaternion[] targetRestLocal;
    private readonly int sourceHips;
    private readonly float hipsRatio;

    // what one Sample works in: the rotations of every bone of the two skeletons in the world (this is not made for two threads)
    private readonly Quaternion[] sourceLocal;
    private readonly Quaternion[] sourceWorld;
    private readonly Quaternion[] targetWorld;

    private PlayerModelEmoteRetarget(Mapping[] mappings, int[] targetToMapping, PlayerModelRest source, PlayerModelRest target, int sourceHips, float hipsRatio)
    {
        this.mappings = mappings;
        this.targetToMapping = targetToMapping;
        this.sourceHips = sourceHips;
        this.hipsRatio = hipsRatio;
        sourceParents = Parents(source);
        targetParents = Parents(target);
        sourceOrder = ParentsFirst(sourceParents);
        targetOrder = ParentsFirst(targetParents);
        sourceRestLocal = RestLocal(source, sourceParents);
        targetRestLocal = RestLocal(target, targetParents);
        sourceLocal = new Quaternion[source.Count];
        sourceWorld = new Quaternion[source.Count];
        targetWorld = new Quaternion[target.Count];
        DrivenNodes = mappings.Select(mapping => mapping.Target).ToArray();
    }

    /// <summary>The nodes of the target that a clip turns (the bones both skeletons have): what is put back to its rest pose when the emote is over.</summary>
    public int[] DrivenNodes { get; }

    /// <summary>The number of bones that follow a bone of the source.</summary>
    public int MappedBones => mappings.Length;

    /// <summary>
    /// The bones that two skeletons share, for the log: how many of the bones that the pose copy knows (the body and the fingers) the two have. <see cref="Create" /> uses the same count.
    /// </summary>
    public static int SharedBoneCount(PlayerModelRest source, PlayerModelBoneNames sourceNames, PlayerModelRest target, PlayerModelBoneNames targetNames)
    {
        int count = 0;
        foreach ((string Canonical, string? Aim) entry in BoneList())
        {
            if (source.Find(sourceNames, entry.Canonical) >= 0 && target.Find(targetNames, entry.Canonical) >= 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Lines the two skeletons up. Null with the reason in <paramref name="reason" /> when they do not share <see cref="MIN_MAPPED_BONES" /> bones. <paramref name="sourceNames" /> and
    /// <paramref name="targetNames" /> say how each rig spells its bones.
    /// </summary>
    public static PlayerModelEmoteRetarget? Create(PlayerModelRest source, PlayerModelBoneNames sourceNames, PlayerModelRest target, PlayerModelBoneNames targetNames, out string reason)
    {
        List<Mapping> list = [];
        int[] targetToMapping = new int[target.Count];
        for (int i = 0; i < targetToMapping.Length; i++)
        {
            targetToMapping[i] = -1;
        }
        foreach ((string canonical, string? aim) in BoneList())
        {
            int s = source.Find(sourceNames, canonical);
            int t = target.Find(targetNames, canonical);
            if (s < 0 || t < 0 || targetToMapping[t] >= 0)
            {
                continue;
            }
            Quaternion fix = Quaternion.Identity;
            if (aim != null)
            {
                int sourceAim = source.Find(sourceNames, aim);
                int targetAim = target.Find(targetNames, aim);
                if (sourceAim >= 0 && targetAim >= 0)
                {
                    fix = PlayerModelRig.FromTo(target.Position(targetAim) - target.Position(t), source.Position(sourceAim) - source.Position(s));
                }
            }
            targetToMapping[t] = list.Count;
            list.Add(new Mapping(s, t, PlayerModelRig.Constant(source.Rotation(s), target.Rotation(t), fix)));
        }
        if (list.Count < MIN_MAPPED_BONES)
        {
            reason = $"the two skeletons share only {list.Count} of the bones that an emote turns (at least {MIN_MAPPED_BONES} are needed)";
            return null;
        }

        int sourceHips = source.Find(sourceNames, PlayerModelRig.HIPS);
        int targetHips = target.Find(targetNames, PlayerModelRig.HIPS);
        float ratio = 1f;
        if (sourceHips >= 0 && targetHips >= 0 && source.Position(sourceHips).Y > 0.01f && target.Position(targetHips).Y > 0.01f)
        {
            ratio = target.Position(targetHips).Y / source.Position(sourceHips).Y;
        }
        reason = "";
        return new PlayerModelEmoteRetarget(list.ToArray(), targetToMapping, source, target, sourceHips, ratio);
    }

    /// <summary>
    /// The pose of <paramref name="clip" /> (a clip of the source) at <paramref name="time" />, on the target: the local rotation of every bone that follows a source bone, in the game's space, in
    /// <paramref name="localRotations" /> at the index of the node (and <paramref name="hasRotation" /> says which entries were written; the rest are not touched), and the height of the hips in the target's own units.
    /// </summary>
    public void Sample(GlbClipData clip, float time, Quaternion[] localRotations, bool[] hasRotation, out float hipsHeight, out bool hasHips)
    {
        hipsHeight = 0f;
        hasHips = false;
        Array.Copy(sourceRestLocal, sourceLocal, sourceLocal.Length);
        foreach (GlbTrack track in clip.Tracks)
        {
            if (track.Node < 0 || track.Node >= sourceLocal.Length)
            {
                continue;
            }
            if (track.IsRotation)
            {
                Quaternion q = track.SampleRotation(time);
                GlbSpace.Rotation(q.X, q.Y, q.Z, q.W, out float x, out float y, out float z, out float w);
                sourceLocal[track.Node] = Quaternion.Normalize(new Quaternion(x, y, z, w));
            }
            else if (track.Node == sourceHips)
            {
                Vector3 t = track.SampleTranslation(time);
                GlbSpace.Vector(t.X, t.Y, t.Z, out _, out float height, out _);
                hipsHeight = height * hipsRatio;
                hasHips = true;
            }
        }

        for (int i = 0; i < sourceOrder.Length; i++)
        {
            int node = sourceOrder[i];
            int parent = sourceParents[node];
            sourceWorld[node] = parent < 0 ? sourceLocal[node] : Quaternion.Normalize(sourceWorld[parent] * sourceLocal[node]);
        }

        Array.Clear(hasRotation, 0, Math.Min(hasRotation.Length, targetWorld.Length));
        for (int i = 0; i < targetOrder.Length; i++)
        {
            int node = targetOrder[i];
            int parent = targetParents[node];
            Quaternion parentWorld = parent < 0 ? Quaternion.Identity : targetWorld[parent];
            int mapping = targetToMapping[node];
            if (mapping < 0)
            {
                targetWorld[node] = Quaternion.Normalize(parentWorld * targetRestLocal[node]);
                continue;
            }
            Quaternion world = Quaternion.Normalize(sourceWorld[mappings[mapping].Source] * mappings[mapping].K);
            targetWorld[node] = world;
            if (node < localRotations.Length && node < hasRotation.Length)
            {
                localRotations[node] = Quaternion.Normalize(Quaternion.Inverse(parentWorld) * world);
                hasRotation[node] = true;
            }
        }
    }

    /// <summary>The bones that an emote turns, by the names the Mixamo rig gives them, each with the bone that it points to (the direction to it lines the two rest poses up): the body of <see cref="PlayerModelRig.Pairs" /> and the three joints of every finger.</summary>
    private static IEnumerable<(string Canonical, string? Aim)> BoneList()
    {
        foreach (PlayerModelBonePair pair in PlayerModelRig.Pairs)
        {
            yield return (pair.Mixamo, pair.MixamoAim);
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
            {
                for (int joint = 1; joint <= PlayerModelHands.JOINTS_PER_FINGER; joint++)
                {
                    yield return ($"{side}Hand{finger}{joint}", $"{side}Hand{finger}{joint + 1}");
                }
            }
        }
    }

    private static int[] Parents(PlayerModelRest rest)
    {
        int[] parents = new int[rest.Count];
        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = rest.Parent(i);
        }
        return parents;
    }

    /// <summary>The nodes in an order in which every node comes after its parent (the order in a file is not that).</summary>
    private static int[] ParentsFirst(int[] parents)
    {
        int count = parents.Length;
        int[] order = new int[count];
        bool[] placed = new bool[count];
        int next = 0;
        // a node is placed when its parent is: repeat until nothing is left (a chain is as long as the skeleton is deep)
        while (next < count)
        {
            int before = next;
            for (int i = 0; i < count; i++)
            {
                if (!placed[i] && (parents[i] < 0 || parents[i] >= count || placed[parents[i]]))
                {
                    placed[i] = true;
                    order[next++] = i;
                }
            }
            if (next == before)
            {
                // a loop in the parents (a file that is broken): the rest is placed as it comes
                for (int i = 0; i < count; i++)
                {
                    if (!placed[i])
                    {
                        placed[i] = true;
                        order[next++] = i;
                    }
                }
            }
        }
        return order;
    }

    private static Quaternion[] RestLocal(PlayerModelRest rest, int[] parents)
    {
        Quaternion[] local = new Quaternion[rest.Count];
        for (int i = 0; i < local.Length; i++)
        {
            local[i] = parents[i] < 0 || parents[i] >= local.Length ? rest.Rotation(i) : Quaternion.Normalize(Quaternion.Inverse(rest.Rotation(parents[i])) * rest.Rotation(i));
        }
        return local;
    }
}
