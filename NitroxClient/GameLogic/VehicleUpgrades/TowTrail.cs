using System;
using System.Collections.Generic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The path that the tower's rear anchor has taken, as breadcrumbs half a metre apart, for the tow of a vehicle that the game keeps still (<see cref="TowRules.CarryVelocity" />): a vehicle that nothing
/// collides with, because the world is not built around it, has to follow the line the tower itself went along and not the straight line to the tower, which cuts a corner of a cave through its wall. The
/// point on the path a given distance behind the tower is where the carried vehicle should be. Pure numbers (tuples, no Unity types), so that it is tested.
/// </summary>
public sealed class TowTrail
{
    private readonly List<(float X, float Y, float Z)> crumbs = [];
    private readonly float spacing;
    private readonly float keepLength;
    private (float X, float Y, float Z) head;
    private bool hasHead;

    /// <param name="spacing">Metres between two breadcrumbs.</param>
    /// <param name="keepLength">Metres of path kept behind the tower; older breadcrumbs are dropped.</param>
    public TowTrail(float spacing, float keepLength)
    {
        this.spacing = Math.Max(0.05f, spacing);
        this.keepLength = Math.Max(1f, keepLength);
    }

    /// <summary>The breadcrumbs left behind the head (the head itself is not one until the tower has moved on).</summary>
    public int Count => crumbs.Count;

    public bool HasHead => hasHead;

    /// <summary>The tower's anchor is at this point now. A new breadcrumb is left where the head was when it has moved a spacing away from the last one.</summary>
    public void Add(float x, float y, float z)
    {
        (float X, float Y, float Z) point = (x, y, z);
        if (!hasHead)
        {
            head = point;
            hasHead = true;
            crumbs.Add(point);
            return;
        }
        head = point;
        if (Distance(crumbs[crumbs.Count - 1], point) >= spacing)
        {
            crumbs.Add(point);
            Prune();
        }
    }

    /// <summary>
    /// The point on the path <paramref name="distance" /> metres of path behind the head; the oldest breadcrumb when the path is not that long yet, and null (no point) when nothing was added. Measured along the
    /// path from the head over the breadcrumbs, so a corner counts with both its legs.
    /// </summary>
    public (float X, float Y, float Z)? PointBehind(float distance)
    {
        if (!hasHead)
        {
            return null;
        }
        float remaining = Math.Max(0f, distance);
        (float X, float Y, float Z) from = head;
        for (int i = crumbs.Count - 1; i >= 0; i--)
        {
            (float X, float Y, float Z) to = crumbs[i];
            float segment = Distance(from, to);
            if (segment >= remaining)
            {
                if (segment <= 0.0001f)
                {
                    return to;
                }
                float t = remaining / segment;
                return (from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t, from.Z + (to.Z - from.Z) * t);
            }
            remaining -= segment;
            from = to;
        }
        return crumbs[0];
    }

    /// <summary>Metres of path from the head back to the oldest breadcrumb.</summary>
    public float Length
    {
        get
        {
            if (!hasHead)
            {
                return 0f;
            }
            float length = 0f;
            (float X, float Y, float Z) from = head;
            for (int i = crumbs.Count - 1; i >= 0; i--)
            {
                length += Distance(from, crumbs[i]);
                from = crumbs[i];
            }
            return length;
        }
    }

    public void Clear()
    {
        crumbs.Clear();
        hasHead = false;
    }

    private void Prune()
    {
        // Drop the oldest breadcrumbs while the path without them is still long enough (the second-oldest becomes the oldest)
        while (crumbs.Count > 2 && PathLengthFrom(1) >= keepLength)
        {
            crumbs.RemoveAt(0);
        }
    }

    private float PathLengthFrom(int index)
    {
        float length = 0f;
        (float X, float Y, float Z) from = head;
        for (int i = crumbs.Count - 1; i >= index; i--)
        {
            length += Distance(from, crumbs[i]);
            from = crumbs[i];
        }
        return length;
    }

    private static float Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
