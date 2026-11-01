using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// A skeleton that a test can pose the way the game poses the model's bones: a bone's world rotation is set and its children go along with it (what <c>Transform.rotation</c> does), the positions follow from the
/// rotations and the rest offsets, and the whole is as big as the scale says (the root scale that the pose copy gives the model). It lets the tests of the hands run the arithmetic of the game without the game.
/// </summary>
internal sealed class RigSim
{
    private readonly PlayerModelRest rest;
    private readonly float scale;
    private readonly Vector3[] localPosition;
    private readonly Vector3[] restLocalPosition;
    private readonly Quaternion[] localRotation;

    public RigSim(PlayerModelRest rest, float scale)
    {
        this.rest = rest;
        this.scale = scale;
        localPosition = new Vector3[rest.Count];
        localRotation = new Quaternion[rest.Count];
        for (int i = 0; i < rest.Count; i++)
        {
            int parent = rest.Parent(i);
            if (parent < 0)
            {
                localPosition[i] = rest.Position(i);
                localRotation[i] = rest.Rotation(i);
                continue;
            }
            Quaternion inverse = Quaternion.Inverse(rest.Rotation(parent));
            localPosition[i] = Vector3.Transform(rest.Position(i) - rest.Position(parent), inverse);
            localRotation[i] = Quaternion.Normalize(inverse * rest.Rotation(i));
        }
        restLocalPosition = (Vector3[])localPosition.Clone();
    }

    /// <summary>Makes the bone's offset from its parent this many times as long as at rest (what the hands do to an arm that is too short: its joints are moved out along the bones); 1 is the rest.</summary>
    public void Stretch(int node, float factor)
    {
        localPosition[node] = restLocalPosition[node] * factor;
    }

    public PlayerModelRest Rest => rest;

    public Quaternion Rotation(int node)
    {
        int parent = rest.Parent(node);
        return parent < 0 ? localRotation[node] : Quaternion.Normalize(Rotation(parent) * localRotation[node]);
    }

    public Vector3 Position(int node)
    {
        int parent = rest.Parent(node);
        if (parent < 0)
        {
            return localPosition[node] * scale;
        }
        return Position(parent) + Vector3.Transform(localPosition[node] * scale, Rotation(parent));
    }

    /// <summary>Gives the bone this rotation in the frame of its parent (what a clip does to a bone).</summary>
    public void SetLocalRotation(int node, Quaternion local)
    {
        localRotation[node] = Quaternion.Normalize(local);
    }

    /// <summary>Turns the bone so that its world rotation is <paramref name="world" />; what hangs on it goes along.</summary>
    public void SetRotation(int node, Quaternion world)
    {
        int parent = rest.Parent(node);
        localRotation[node] = parent < 0 ? Quaternion.Normalize(world) : Quaternion.Normalize(Quaternion.Inverse(Rotation(parent)) * world);
    }

    public Vector3 Middle(IEnumerable<int> nodes)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        foreach (int node in nodes)
        {
            sum += Position(node);
            count++;
        }
        return sum / count;
    }
}

/// <summary>Models of other proportions made from the rest pose of the real one (so that the hands, the fingers and the sides are right by construction): taller, shorter, longer arms, bigger hands, wider shoulders, another rest pose.</summary>
internal static class RestVariants
{
    private static string Bare(PlayerModelRest rest, int node) => rest.Name(node).StartsWith(PlayerModelRig.MIXAMO_PREFIX, StringComparison.Ordinal) ? rest.Name(node).Substring(PlayerModelRig.MIXAMO_PREFIX.Length) : rest.Name(node);

    private static PlayerModelRest Rebuild(PlayerModelRest rest, Func<int, float> segmentFactor)
    {
        Vector3[] positions = new Vector3[rest.Count];
        bool[] done = new bool[rest.Count];

        void Resolve(int node)
        {
            if (done[node])
            {
                return;
            }
            int parent = rest.Parent(node);
            if (parent < 0)
            {
                positions[node] = rest.Position(node);
            }
            else
            {
                Resolve(parent);
                positions[node] = positions[parent] + ((rest.Position(node) - rest.Position(parent)) * segmentFactor(node));
            }
            done[node] = true;
        }

        for (int i = 0; i < rest.Count; i++)
        {
            Resolve(i);
        }
        return new PlayerModelRest(Enumerable.Range(0, rest.Count).Select(rest.Name).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Parent).ToArray(), positions, Enumerable.Range(0, rest.Count).Select(rest.Rotation).ToArray());
    }

    /// <summary>The whole model bigger or smaller by this factor.</summary>
    public static PlayerModelRest Scaled(PlayerModelRest rest, float factor)
    {
        return new PlayerModelRest(Enumerable.Range(0, rest.Count).Select(rest.Name).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Parent).ToArray(),
                                   Enumerable.Range(0, rest.Count).Select(node => rest.Position(node) * factor).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Rotation).ToArray());
    }

    /// <summary>The upper arm, the forearm and the hand (with its fingers) longer or shorter by these factors, on both sides.</summary>
    public static PlayerModelRest WithLimbLengths(PlayerModelRest rest, float upperArm, float foreArm, float hand)
    {
        return Rebuild(rest, node =>
        {
            string name = Bare(rest, node);
            return name switch
            {
                "LeftForeArm" or "RightForeArm" => upperArm,
                "LeftHand" or "RightHand" => foreArm,
                _ => name.StartsWith("LeftHand", StringComparison.Ordinal) || name.StartsWith("RightHand", StringComparison.Ordinal) ? hand : 1f
            };
        });
    }

    /// <summary>The shoulders wider or narrower: the distance from each collar bone's joint to the joint of the arm.</summary>
    public static PlayerModelRest WithShoulderWidth(PlayerModelRest rest, float factor)
    {
        return Rebuild(rest, node =>
        {
            string name = Bare(rest, node);
            return name is "LeftArm" or "RightArm" ? factor : 1f;
        });
    }

    /// <summary>Another rest pose: both arms turned about the forward axis at the shoulder by this many degrees (a model that stands in a T instead of an A, or with its arms down).</summary>
    public static PlayerModelRest WithArmsRaised(PlayerModelRest rest, float degrees)
    {
        Vector3[] positions = Enumerable.Range(0, rest.Count).Select(rest.Position).ToArray();
        Quaternion[] rotations = Enumerable.Range(0, rest.Count).Select(rest.Rotation).ToArray();
        foreach ((string armName, float sign) in new[] { ("LeftArm", 1f), ("RightArm", -1f) })
        {
            int arm = rest.Find($"{PlayerModelRig.MIXAMO_PREFIX}{armName}");
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sign * degrees * (float)Math.PI / 180f);
            Vector3 pivot = rest.Position(arm);
            for (int node = 0; node < rest.Count; node++)
            {
                bool below = false;
                for (int up = node; up >= 0; up = rest.Parent(up))
                {
                    if (up == arm)
                    {
                        below = true;
                        break;
                    }
                }
                if (below)
                {
                    positions[node] = pivot + Vector3.Transform(rest.Position(node) - pivot, turn);
                    rotations[node] = Quaternion.Normalize(turn * rest.Rotation(node));
                }
            }
        }
        return new PlayerModelRest(Enumerable.Range(0, rest.Count).Select(rest.Name).ToArray(), Enumerable.Range(0, rest.Count).Select(rest.Parent).ToArray(), positions, rotations);
    }
}

/// <summary>Reads the bind pose of the vanilla body that <see cref="VanillaBindFixture" /> holds.</summary>
internal static class VanillaBindReader
{
    private static readonly Regex bindLine = new(@"^(\S+) pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) rot=\(([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\)$");

    private static float F(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    public static Dictionary<string, PlayerModelBind> Bind()
    {
        Dictionary<string, PlayerModelBind> bind = new();
        foreach (string raw in VanillaBindFixture.BIND.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            Match m = bindLine.Match(line);
            m.Success.Should().BeTrue($"the fixture line '{line}' has to be readable");
            bind[m.Groups[1].Value] = new PlayerModelBind(new Vector3(F(m.Groups[2].Value), F(m.Groups[3].Value), F(m.Groups[4].Value)),
                                                          Quaternion.Normalize(new Quaternion(F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value), F(m.Groups[8].Value))));
        }
        return bind;
    }

    public static (Vector3 Position, Quaternion Rotation) Attach()
    {
        Match m = Regex.Match(VanillaBindFixture.ATTACH, @"pos=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) rot=\(([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\)");
        m.Success.Should().BeTrue();
        return (new Vector3(F(m.Groups[1].Value), F(m.Groups[2].Value), F(m.Groups[3].Value)),
                Quaternion.Normalize(new Quaternion(F(m.Groups[4].Value), F(m.Groups[5].Value), F(m.Groups[6].Value), F(m.Groups[7].Value))));
    }
}
