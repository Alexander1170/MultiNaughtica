using System.Text;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.PlayerLogic;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// What the quick ping key looks at (docs/coop/QUICK_PING.md): a ray from the camera, up to <see cref="QuickPingRules.AIM_RANGE" /> metres, that skips the player's own body and tools, the vehicle the player
/// sits in, and, at the helm of a Cyclops, the hull of that Cyclops (so that the ping reaches what is outside the windows). The first thing past those is the target: a creature is named after its kind,
/// a resource, an item, a vehicle or a teammate after what they are, and anything else (rock, sea floor, a wall of a base) is "Look here". The decisions about names live in
/// <see cref="QuickPingRules" />, which the tests cover; this class only asks the game.
/// </summary>
internal static class QuickPingAim
{
    /// <summary>The most colliders of the player's own that one ray steps past before it gives up (a diver has a handful: body, helmet, tool).</summary>
    private const int MAX_SKIPS = 24;

    private const float STEP_PAST_HIT = 0.02f;

    /// <summary>What the ray found.</summary>
    internal readonly struct Target
    {
        public Target(Vector3 point, Vector3 normal, float distance, string label, string kind, GameObject hitObject)
        {
            Point = point;
            Normal = normal;
            Distance = distance;
            Label = label;
            Kind = kind;
            HitObject = hitObject;
        }

        /// <summary>Where the ray met it.</summary>
        public Vector3 Point { get; }

        public Vector3 Normal { get; }

        public float Distance { get; }

        /// <summary>What the ping says ("Reaper Leviathan", "Limestone Outcrop", "Look here").</summary>
        public string Label { get; }

        /// <summary>"creature", "thing", "player" or "place": for the log of the harness.</summary>
        public string Kind { get; }

        /// <summary>The object that the ray hit (its collider), for the log and the harness.</summary>
        public GameObject HitObject { get; }

        /// <summary>The position of the marker: a little off the surface, so that it does not sit inside it.</summary>
        public Vector3 MarkerPosition => Point + Normal * 0.5f;
    }

    /// <summary>The ray of the key: from the camera through the middle of the screen, where the crosshair is.</summary>
    public static bool TryGetCameraRay(out Ray ray)
    {
        Camera camera = Camera.main;
        if (!camera)
        {
            ray = default;
            return false;
        }
        ray = new Ray(camera.transform.position, camera.transform.forward);
        return true;
    }

    private static readonly RaycastHit[] teammateHits = new RaycastHit[32];

    public static bool TryAim(Ray ray, out Target target)
    {
        bool solid = TryAimAtSolid(ray, out target, out float solidDistance);

        // A teammate's body is a trigger (RemotePlayer.SetupBody), which the ray of the solid things ignores: ask for triggers on the layer of the body, and take the teammate when they stand in front of what the ray hit
        if (TryAimAtTeammate(ray, solid ? solidDistance : QuickPingRules.AIM_RANGE, out RaycastHit teammate))
        {
            target = Describe(teammate, teammate.distance);
            return true;
        }
        return solid;
    }

    private static bool TryAimAtTeammate(Ray ray, float maxDistance, out RaycastHit nearest)
    {
        nearest = default;
        float nearestDistance = float.MaxValue;
        int count = Physics.RaycastNonAlloc(new Ray(ray.origin, ray.direction.normalized), teammateHits, maxDistance, 1 << LayerID.Useable, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = teammateHits[i];
            if (hit.distance < nearestDistance && QuickPingRules.IsAimDistance(hit.distance) && hit.collider && hit.collider.GetComponentInParent<RemotePlayerIdentifier>())
            {
                nearest = hit;
                nearestDistance = hit.distance;
            }
        }
        return nearestDistance < float.MaxValue;
    }

    private static bool TryAimAtSolid(Ray ray, out Target target, out float hitDistance)
    {
        target = default;
        hitDistance = QuickPingRules.AIM_RANGE;
        int mask = ~LayerMask.GetMask("UI", "Ignore Raycast");
        Vector3 origin = ray.origin;
        Vector3 direction = ray.direction.normalized;
        Vehicle ownVehicle = Player.main ? Player.main.GetVehicle() : null;
        SubRoot pilotedHull = Player.main && Player.main.IsPiloting() ? Player.main.currentSub : null;
        float travelled = 0f;

        for (int i = 0; i < MAX_SKIPS && travelled < QuickPingRules.AIM_RANGE; i++)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, QuickPingRules.AIM_RANGE - travelled, mask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            float distance = travelled + hit.distance;
            if (IsOwn(hit.collider, ownVehicle, pilotedHull) || !QuickPingRules.IsAimDistance(distance))
            {
                travelled = distance + STEP_PAST_HIT;
                origin = hit.point + direction * STEP_PAST_HIT;
                continue;
            }
            target = Describe(hit, distance);
            hitDistance = distance;
            return true;
        }
        return false;
    }

    private static bool IsOwn(Collider collider, Vehicle ownVehicle, SubRoot pilotedHull)
    {
        if (collider.GetComponentInParent<Player>())
        {
            return true;
        }
        if (ownVehicle && collider.GetComponentInParent<Vehicle>() == ownVehicle)
        {
            return true;
        }
        return pilotedHull && collider.GetComponentInParent<SubRoot>() == pilotedHull;
    }

    private static Target Describe(RaycastHit hit, float distance)
    {
        Collider collider = hit.collider;
        string kind = "place";
        string creatureName = null;
        string thingName = null;

        RemotePlayerIdentifier teammate = collider.GetComponentInParent<RemotePlayerIdentifier>();
        if (teammate && teammate.RemotePlayer != null)
        {
            kind = "player";
            thingName = teammate.RemotePlayer.PlayerName;
        }
        else
        {
            Creature creature = collider.GetComponentInParent<Creature>();
            if (creature)
            {
                kind = "creature";
                creatureName = NameOf(CraftData.GetTechType(creature.gameObject), creature.gameObject.name);
            }
            else
            {
                TechType tech = CraftData.GetTechType(collider.gameObject);
                if (tech != TechType.None)
                {
                    kind = "thing";
                    thingName = NameOf(tech, null);
                }
            }
        }
        return new Target(hit.point, hit.normal, distance, QuickPingRules.TargetLabel(creatureName, thingName), kind, collider.gameObject);
    }

    /// <summary>The name of a kind of thing as the game shows it; the name of the object, spaced, when it has no kind.</summary>
    private static string NameOf(TechType tech, string objectName)
    {
        if (tech != TechType.None)
        {
            string name = NavigatorContacts.NameOf(tech);
            return name == tech.ToString() ? Spaced(name) : name;
        }
        return string.IsNullOrEmpty(objectName) ? null : Spaced(objectName.Replace("(Clone)", string.Empty).Trim());
    }

    /// <summary>"ReaperLeviathan" to "Reaper Leviathan": a space before every capital that follows a lower case letter.</summary>
    private static string Spaced(string name)
    {
        StringBuilder builder = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
            {
                builder.Append(' ');
            }
            builder.Append(name[i]);
        }
        return builder.ToString();
    }
}
