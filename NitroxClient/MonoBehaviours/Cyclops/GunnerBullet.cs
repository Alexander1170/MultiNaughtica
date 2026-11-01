using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// Marks the stasis sphere of a gunner station with the Cyclops it is fired from (docs/coop/VEHICLE_UPGRADES.md, station 2). The game's sphere looks for what it hits with a sphere cast that counts triggers, and a
/// Cyclops is wrapped in them (the moonpool's box is 9 by 14 by 53 m round the whole hull, the damage points and the entry hatch are triggers too): a sphere that starts above the hull and goes down would explode
/// on the top of that box, a few metres from the mount. For a marked sphere <see cref="SphereCast" /> (which <c>Bullet.Update</c> calls instead of <c>Physics.SphereCast</c>, see <c>Bullet_Update_Gunner_Patch</c>)
/// skips everything that belongs to the owner Cyclops, solid or trigger, and finds the first other thing; a sphere that is not marked casts as the game does.
/// </summary>
public class GunnerBullet : MonoBehaviour
{
    private static readonly RaycastHit[] buffer = new RaycastHit[48];

    /// <summary>The Cyclops the sphere is fired from; its colliders do not stop the sphere.</summary>
    public SubRoot Owner { get; set; }

    /// <summary>The cast of <c>Bullet.Update</c>, with the bullet that makes it: the game's own for any bullet that is not a gunner's.</summary>
    public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask, Bullet bullet)
    {
        if (!bullet || !bullet.TryGetComponent(out GunnerBullet marked) || !marked.Owner)
        {
            return Physics.SphereCast(origin, radius, direction, out hit, maxDistance, layerMask);
        }

        int count = Physics.SphereCastNonAlloc(origin, radius, direction, buffer, maxDistance, layerMask, QueryTriggerInteraction.Collide);
        hit = default;
        float nearest = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider collider = buffer[i].collider;
            if (!collider || buffer[i].distance >= nearest || collider.GetComponentInParent<SubRoot>() == marked.Owner)
            {
                continue;
            }
            nearest = buffer[i].distance;
            hit = buffer[i];
            found = true;
        }
        return found;
    }
}
