using System;
using System.Collections.Generic;
using System.Numerics;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;

/// <summary>
/// One animated property of one node of a clip (docs/coop/PLAYER_MODELS.md): the times of its keys and its values, in glTF space. A rotation has four values (x, y, z, w) per key and a translation three.
/// Between two keys the value is interpolated (rotations on the shortest arc), or it stays on the earlier key for a clip that says so (STEP).
/// </summary>
public sealed class GlbTrack
{
    public GlbTrack(int node, bool isRotation, float[] times, float[] values, bool step)
    {
        Node = node;
        IsRotation = isRotation;
        Times = times;
        Values = values;
        Step = step;
    }

    /// <summary>The index of the node (in <see cref="GlbModel.Nodes" />) that the track moves.</summary>
    public int Node { get; }

    public bool IsRotation { get; }

    public float[] Times { get; }

    public float[] Values { get; }

    public bool Step { get; }

    /// <summary>The rotation at <paramref name="time" />: the first key before the first one and the last one after the last one.</summary>
    public Quaternion SampleRotation(float time)
    {
        int count = Times.Length;
        if (count == 0 || Values.Length < count * 4)
        {
            return Quaternion.Identity;
        }
        if (count == 1 || time <= Times[0])
        {
            return RotationAt(0);
        }
        if (time >= Times[count - 1])
        {
            return RotationAt(count - 1);
        }
        int key = KeyBefore(time);
        if (Step)
        {
            return RotationAt(key);
        }
        float span = Times[key + 1] - Times[key];
        float t = span > 1e-6f ? (time - Times[key]) / span : 0f;
        return Quaternion.Normalize(Quaternion.Slerp(RotationAt(key), RotationAt(key + 1), t));
    }

    /// <summary>The translation at <paramref name="time" />: the first key before the first one and the last one after the last one.</summary>
    public Vector3 SampleTranslation(float time)
    {
        int count = Times.Length;
        if (count == 0 || Values.Length < count * 3)
        {
            return Vector3.Zero;
        }
        if (count == 1 || time <= Times[0])
        {
            return TranslationAt(0);
        }
        if (time >= Times[count - 1])
        {
            return TranslationAt(count - 1);
        }
        int key = KeyBefore(time);
        if (Step)
        {
            return TranslationAt(key);
        }
        float span = Times[key + 1] - Times[key];
        float t = span > 1e-6f ? (time - Times[key]) / span : 0f;
        return Vector3.Lerp(TranslationAt(key), TranslationAt(key + 1), t);
    }

    /// <summary>The translation of the first key (where the node starts).</summary>
    public Vector3 FirstTranslation()
    {
        return Times.Length > 0 && Values.Length >= 3 ? TranslationAt(0) : Vector3.Zero;
    }

    private Quaternion RotationAt(int key)
    {
        return new Quaternion(Values[key * 4], Values[key * 4 + 1], Values[key * 4 + 2], Values[key * 4 + 3]);
    }

    private Vector3 TranslationAt(int key)
    {
        return new Vector3(Values[key * 3], Values[key * 3 + 1], Values[key * 3 + 2]);
    }

    /// <summary>The index of the last key at or before <paramref name="time" />, found by bisection (the time is inside the keys).</summary>
    private int KeyBefore(float time)
    {
        int low = 0;
        int high = Times.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (Times[middle] <= time)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }
        return low;
    }
}

/// <summary>
/// A clip of a model with all its keys (docs/coop/PLAYER_MODELS.md): what an emote plays. <see cref="Sample" /> gives the pose at a time, in glTF space (the caller turns it into the game's space with
/// <see cref="GlbSpace" />). Only rotations and translations are read: the clips of the first model have nothing else.
/// </summary>
public sealed class GlbClipData
{
    public GlbClipData(string name, float duration, IReadOnlyList<GlbTrack> tracks, int nodeCount)
    {
        Name = name;
        Duration = duration;
        Tracks = tracks;
        NodeCount = nodeCount;
    }

    public string Name { get; }

    public float Duration { get; }

    public IReadOnlyList<GlbTrack> Tracks { get; }

    /// <summary>The number of nodes of the model: the arrays that <see cref="Sample" /> fills have this length.</summary>
    public int NodeCount { get; }

    /// <summary>
    /// The pose at <paramref name="time" />: the rotation and the translation of every node that the clip moves, in the arrays at the index of the node; the arrays <paramref name="hasRotation" /> and
    /// <paramref name="hasTranslation" /> say which entries were written (the others are not touched, they keep what they had).
    /// </summary>
    public void Sample(float time, Quaternion[] rotations, bool[] hasRotation, Vector3[] translations, bool[] hasTranslation)
    {
        Array.Clear(hasRotation, 0, hasRotation.Length);
        Array.Clear(hasTranslation, 0, hasTranslation.Length);
        foreach (GlbTrack track in Tracks)
        {
            if (track.Node < 0 || track.Node >= rotations.Length)
            {
                continue;
            }
            if (track.IsRotation)
            {
                rotations[track.Node] = track.SampleRotation(time);
                hasRotation[track.Node] = true;
            }
            else
            {
                translations[track.Node] = track.SampleTranslation(time);
                hasTranslation[track.Node] = true;
            }
        }
    }

    /// <summary>The translation of the first key of the node's translation track (where the clip starts it), null when the clip does not move the node.</summary>
    public Vector3? FirstTranslationOf(int node)
    {
        foreach (GlbTrack track in Tracks)
        {
            if (track.Node == node && !track.IsRotation)
            {
                return track.FirstTranslation();
            }
        }
        return null;
    }
}
