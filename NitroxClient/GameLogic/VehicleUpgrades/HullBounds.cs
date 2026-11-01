using System.Runtime.CompilerServices;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The box around the colliders of a vehicle or a Cyclops in its own space (so that it stays right while the vehicle turns), found once and kept: the power share measures the distance from a
/// vehicle to the hull of a Cyclops with it, and the tow hooks anchor their cable at the nose and the rear of the hulls (<see cref="HullGeometry" />). The prefabs' sizes cannot be read offline, so
/// the box is measured from the colliders at run time. Colliders that are triggers (the remote players' bodies, the docking triggers) and the colliders of another vehicle docked in this one are no
/// part of the hull.
/// </summary>
public static class HullBounds
{
    /// <summary>Seconds before the colliders are looked for again when none were found (the vehicle may not be built yet).</summary>
    private const float RETRY_SECONDS = 5f;

    private sealed class Holder
    {
        public bool Found;
        public Bounds Local;
        public float NextAttempt;
    }

    private static readonly ConditionalWeakTable<Transform, Holder> cache = new();

    /// <summary>The box around the hull of <paramref name="root" /> in the space of <paramref name="root" />; false when there are no colliders (yet).</summary>
    public static bool TryGet(Transform root, out Bounds local)
    {
        local = default;
        if (!root)
        {
            return false;
        }

        Holder holder = cache.GetOrCreateValue(root);
        if (!holder.Found)
        {
            // The colliders of a docked vehicle are switched off (the hull's own among them): what is left would be taken for the hull and kept for good
            Vehicle vehicle = root.GetComponent<Vehicle>();
            if (vehicle && vehicle.docked)
            {
                return false;
            }
            if (Time.time < holder.NextAttempt)
            {
                return false;
            }
            holder.NextAttempt = Time.time + RETRY_SECONDS;
            holder.Found = Measure(root, out holder.Local);
            if (holder.Found)
            {
                Log.Info($"[VehicleUpgrades] hull box of {root.name}: {holder.Local.size} around {holder.Local.center} (local space)");
            }
        }
        local = holder.Local;
        return holder.Found;
    }

    /// <summary>The distance from a world position to the hull of <paramref name="root" />; false when its hull is not known.</summary>
    public static bool TryDistanceTo(Transform root, Vector3 worldPoint, out float distance)
    {
        distance = 0f;
        if (!TryGet(root, out Bounds local))
        {
            return false;
        }
        Vector3 point = root.InverseTransformPoint(worldPoint);
        distance = PowerShareGeometry.DistanceToBox(point.x, point.y, point.z, local.center.x, local.center.y, local.center.z, local.extents.x, local.extents.y, local.extents.z);
        return true;
    }

    /// <summary>
    /// The cable anchor at the nose or the rear of the hull in the world, along <paramref name="forwardInWorld" />; false when the hull is not known.
    /// </summary>
    public static bool TryGetAnchor(Transform root, Vector3 forwardInWorld, bool nose, out Vector3 anchor)
    {
        anchor = root ? root.position : Vector3.zero;
        if (!TryGet(root, out Bounds local))
        {
            return false;
        }
        Vector3 direction = root.InverseTransformDirection(forwardInWorld);
        (float x, float y, float z) = HullGeometry.EndAnchor(local.center.x, local.center.y, local.center.z, local.extents.x, local.extents.y, local.extents.z, direction.x, direction.y, direction.z, nose);
        anchor = root.TransformPoint(new Vector3(x, y, z));
        return true;
    }

    private static bool Measure(Transform root, out Bounds local)
    {
        bool any = false;
        local = default;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger)
            {
                continue;
            }
            // The colliders of a vehicle docked in this one are no part of its hull (the vehicle's own colliders are: its root is the root we measure)
            Vehicle owner = collider.GetComponentInParent<Vehicle>();
            if (owner && owner.transform != root)
            {
                continue;
            }

            // Each collider's own box, moved into the space of the root: the world box of a collider that is turned (a Cyclops at 45 degrees) is bigger than the collider, and in the space of the root bigger still
            Matrix4x4 toRoot = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            if (TryLocalBox(collider, out Vector3 center, out Vector3 size))
            {
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Encapsulate(ref local, ref any, toRoot.MultiplyPoint3x4(center + Vector3.Scale(size * 0.5f, sign)));
                }
            }
            else
            {
                Bounds world = collider.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Encapsulate(ref local, ref any, root.InverseTransformPoint(world.center + Vector3.Scale(world.extents, sign)));
                }
            }
        }

        return any && HullGeometry.IsPlausibleBox(local.center.x, local.center.y, local.center.z, local.extents.x, local.extents.y, local.extents.z);
    }

    private static void Encapsulate(ref Bounds local, ref bool any, Vector3 point)
    {
        if (!any)
        {
            local = new Bounds(point, Vector3.zero);
            any = true;
        }
        else
        {
            local.Encapsulate(point);
        }
    }

    /// <summary>The collider's own box in its own space (a box, sphere or capsule, or the bounds of a mesh collider's mesh); false for any other kind of collider.</summary>
    private static bool TryLocalBox(Collider collider, out Vector3 center, out Vector3 size)
    {
        switch (collider)
        {
            case BoxCollider box:
                center = box.center;
                size = box.size;
                return true;
            case SphereCollider sphere:
                center = sphere.center;
                size = Vector3.one * (sphere.radius * 2f);
                return true;
            case CapsuleCollider capsule:
                float diameter = capsule.radius * 2f;
                float length = Mathf.Max(capsule.height, diameter);
                center = capsule.center;
                size = capsule.direction == 0 ? new Vector3(length, diameter, diameter) : capsule.direction == 1 ? new Vector3(diameter, length, diameter) : new Vector3(diameter, diameter, length);
                return true;
            case MeshCollider mesh when mesh.sharedMesh:
                center = mesh.sharedMesh.bounds.center;
                size = mesh.sharedMesh.bounds.size;
                return true;
            default:
                center = default;
                size = default;
                return false;
        }
    }
}
