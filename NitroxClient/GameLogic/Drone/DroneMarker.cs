using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.GameLogic.Drone;

/// <summary>
/// The mark key of the Scout Drone (docs/coop/VEHICLE_UPGRADES.md, station 4): the operator aims the drone's view at something and presses the key; a mark is placed where the view meets the world, as a threat when
/// it is a creature and as a resource when it is anything else (the second key gives the other kind, <see cref="DroneMarkRules" />). The mark is one of the Cyclops' navigator marks (<see cref="NavMarks" />, kinds
/// <see cref="CyclopsNavRules.MARK_RESOURCE" /> and <see cref="CyclopsNavRules.MARK_THREAT" />): it travels through the server, which accepts it from the seated operator only, and every player sees it on the HUD. The
/// drone's marks have a limit of their own; at the limit the oldest one gives way.
/// </summary>
public static class DroneMarker
{
    /// <summary>Marks what the drone's view is aimed at. Returns the mark (null when nothing was marked); <paramref name="message" /> says what happened, for the player.</summary>
    public static NavMark TryMark(DroneBay bay, bool otherKind, out string message)
    {
        message = null;
        NitroxId cyclopsId = bay ? bay.CyclopsId : null;
        ScoutDrone drone = bay ? bay.Drone : null;
        MapRoomCamera camera = drone ? drone.Camera : null;
        if (cyclopsId == null || !camera || !bay.IsOperating)
        {
            return null;
        }
        if (!TryAim(camera.transform, bay.SubRoot, out RaycastHit hit))
        {
            message = "Nothing in sight to mark";
            return null;
        }
        Creature creature = hit.collider.GetComponentInParent<Creature>();
        byte kind = DroneMarkRules.Classify(creature, otherKind);
        string seen = NameOfSeen(creature ? creature.gameObject : hit.collider.gameObject);
        string label = DroneMarkRules.LabelFor(kind, seen);

        // the drone's marks have a limit of their own: at the limit the oldest one gives way
        if (NavMarks.CountOfClass(cyclopsId, kind) >= CyclopsNavRules.CapOf(kind))
        {
            NavMark oldest = NavMarks.OldestOfClass(cyclopsId, kind);
            if (oldest != null)
            {
                NavMarks.RemoveByPlayer(cyclopsId, oldest.Id);
            }
        }
        NavMark mark = NavMarks.Place(cyclopsId, hit.point, kind, label);
        message = mark != null ? $"Marked: {mark.Label}" : "The map cannot take another mark";
        return mark;
    }

    /// <summary>The nearest thing in the drone's view that is not the drone, its own Cyclops or the local player.</summary>
    private static bool TryAim(Transform view, SubRoot ownHull, out RaycastHit nearest)
    {
        nearest = default;
        bool found = false;
        float nearestDistance = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(view.position, view.forward, DroneMarkRules.AIM_RANGE, ~0, QueryTriggerInteraction.Ignore))
        {
            Collider collider = hit.collider;
            if (!collider || hit.distance >= nearestDistance)
            {
                continue;
            }
            if (collider.GetComponentInParent<ScoutDrone>() || collider.GetComponentInParent<Player>() || (ownHull && collider.GetComponentInParent<SubRoot>() == ownHull))
            {
                continue;
            }
            nearest = hit;
            nearestDistance = hit.distance;
            found = true;
        }
        return found;
    }

    /// <summary>What the drone saw, by name: the tech type of the thing (localized), else the object's own name.</summary>
    private static string NameOfSeen(GameObject seen)
    {
        TechTag tag = seen.GetComponentInParent<TechTag>();
        TechType tech = tag ? tag.type : CraftData.GetTechType(seen);
        return tech != TechType.None ? NavigatorContacts.NameOf(tech) : seen.name;
    }
}
