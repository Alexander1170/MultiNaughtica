using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// How a model's rig names its bones (docs/coop/PLAYER_MODELS.md, "Adding a new model"). The code speaks of the bones by the names the Mixamo rig gives them without the exporter's prefix ("Hips", "RightHandIndex1");
/// this turns such a name into the name of the node in the file: the prefix (<c>mixamorig:</c> for a model that came out of Mixamo) and the names that a rig spells in its own way.
/// </summary>
public sealed class PlayerModelBoneNames
{
    private readonly IReadOnlyDictionary<string, string>? overrides;

    public PlayerModelBoneNames(string prefix, IReadOnlyDictionary<string, string>? overrides)
    {
        Prefix = prefix;
        this.overrides = overrides;
    }

    /// <summary>The names of a model that came out of Mixamo, which is what a model made with Mixamo has.</summary>
    public static PlayerModelBoneNames Mixamo { get; } = new(PlayerModelRig.MIXAMO_PREFIX, null);

    public string Prefix { get; }

    /// <summary>The name of the node in the file for the bone <paramref name="canonical" /> ("RightHand"): the name that the rig spells differently if there is one, else the prefix and the name.</summary>
    public string Full(string canonical)
    {
        if (overrides != null && overrides.TryGetValue(canonical, out string? spelled))
        {
            return spelled;
        }
        return $"{Prefix}{canonical}";
    }
}

/// <summary>
/// The rest pose of a model's skeleton in the game's space (docs/coop/PLAYER_MODELS.md, "The pose copy"): for every node its name, its parent, where it is and how it is turned (relative to the root of the model, in the
/// model's own units). The calibration of the pose copy works on this, so that it can be tested with a skeleton made up in a test as well as with the one of a file (<see cref="FromGlb" />).
/// </summary>
public sealed class PlayerModelRest
{
    private readonly string[] names;
    private readonly int[] parents;
    private readonly Vector3[] positions;
    private readonly Quaternion[] rotations;
    private readonly Dictionary<string, int> byName = new(StringComparer.Ordinal);

    public PlayerModelRest(IReadOnlyList<string> names, IReadOnlyList<int> parents, IReadOnlyList<Vector3> positions, IReadOnlyList<Quaternion> rotations)
    {
        int count = names.Count;
        if (parents.Count != count || positions.Count != count || rotations.Count != count)
        {
            throw new ArgumentException("every node of the rest pose needs a name, a parent, a position and a rotation");
        }
        this.names = new string[count];
        this.parents = new int[count];
        this.positions = new Vector3[count];
        this.rotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            this.names[i] = names[i] ?? "";
            this.parents[i] = parents[i];
            this.positions[i] = positions[i];
            this.rotations[i] = rotations[i];
            if (!byName.ContainsKey(this.names[i]))
            {
                byName[this.names[i]] = i;
            }
        }
    }

    public int Count => names.Length;

    public string Name(int node) => names[node];

    /// <summary>The index of the parent node, -1 for a root.</summary>
    public int Parent(int node) => parents[node];

    /// <summary>Where the node is at rest, relative to the root of the model, in the game's space.</summary>
    public Vector3 Position(int node) => positions[node];

    /// <summary>How the node is turned at rest (its rotation in the world of the model), in the game's space.</summary>
    public Quaternion Rotation(int node) => rotations[node];

    /// <summary>The index of the first node with this exact name, -1 when there is none.</summary>
    public int Find(string fullName) => byName.TryGetValue(fullName, out int node) ? node : -1;

    /// <summary>The index of the bone that the rig names (through <paramref name="boneNames" />) like the canonical <paramref name="canonical" />, -1 when the file has none.</summary>
    public int Find(PlayerModelBoneNames boneNames, string canonical) => Find(boneNames.Full(canonical));

    /// <summary>The rest pose of the skeleton of a file (the glTF pose mirrored into the game's space, <see cref="PlayerModelRig.MixamoRest" />).</summary>
    public static PlayerModelRest FromGlb(GlbModel glb)
    {
        (Vector3[] restPositions, Quaternion[] restRotations) = PlayerModelRig.MixamoRest(glb);
        string[] nodeNames = new string[glb.Nodes.Count];
        int[] nodeParents = new int[glb.Nodes.Count];
        for (int i = 0; i < nodeNames.Length; i++)
        {
            nodeNames[i] = glb.Nodes[i].Name;
            nodeParents[i] = glb.Nodes[i].Parent;
        }
        return new PlayerModelRest(nodeNames, nodeParents, restPositions, restRotations);
    }
}
