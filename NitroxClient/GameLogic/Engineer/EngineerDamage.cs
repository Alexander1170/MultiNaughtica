using System.Collections.Generic;
using UnityEngine;

namespace NitroxClient.GameLogic.Engineer;

/// <summary>
/// The damage-control side of the engineer console (docs/coop/VEHICLE_UPGRADES.md, station 3), read from the game on this client: the rooms of the Cyclops with the fires in them, the hull breaches (the damage
/// points that are in use) and the fire suppression system. Nothing here is synced: the fires and the damage points are entities that every client has already, and the fire suppression is the game's own
/// button, which Nitrox already relays (<c>CyclopsFireSuppressionButton_StartCooldown_Patch</c>). The positions come out in the hull's frame, which is what the drawing (<see cref="CutawayRules" />) takes.
/// </summary>
public static class EngineerDamage
{
    /// <summary>A room of the Cyclops: its number in the game's <c>CyclopsRooms</c>, the box round its fire spawn points (hull frame) and the game's fire value (stale by up to three seconds).</summary>
    public readonly record struct RoomBox(int Room, float MinZ, float MaxZ, float MinY, float MaxY, int FireValue);

    /// <summary>What the cutaway draws.</summary>
    public sealed class Picture
    {
        public readonly List<RoomBox> Rooms = [];
        public readonly List<(float X, float Y, float Z)> Fires = [];
        public readonly List<(float X, float Y, float Z)> Breaches = [];

        public void Clear()
        {
            Rooms.Clear();
            Fires.Clear();
            Breaches.Clear();
        }
    }

    /// <summary>The fire suppression system: whether the Cyclops has it, whether it can be started now and how long its cooldown still runs.</summary>
    public readonly record struct Suppression(bool Installed, bool Ready, bool Running, float CooldownRemaining);

    private const float ROOM_PADDING_METRES = 1.5f;

    /// <summary>Fills <paramref name="picture" /> with what the Cyclops has now.</summary>
    public static void Collect(SubRoot cyclops, Picture picture)
    {
        picture.Clear();
        if (!cyclops)
        {
            return;
        }
        Transform hull = cyclops.transform;
        SubFire fire = cyclops.GetComponentInChildren<SubFire>(true);
        if (fire != null && fire.roomFires != null)
        {
            foreach (KeyValuePair<CyclopsRooms, SubFire.RoomFire> pair in fire.roomFires)
            {
                SubFire.RoomFire room = pair.Value;
                if (room == null || room.spawnNodes == null || room.spawnNodes.Length == 0)
                {
                    continue;
                }
                float minZ = float.MaxValue, maxZ = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (Transform node in room.spawnNodes)
                {
                    if (!node)
                    {
                        continue;
                    }
                    Vector3 local = hull.InverseTransformPoint(node.position);
                    minZ = Mathf.Min(minZ, local.z);
                    maxZ = Mathf.Max(maxZ, local.z);
                    minY = Mathf.Min(minY, local.y);
                    maxY = Mathf.Max(maxY, local.y);
                }
                if (minZ <= maxZ)
                {
                    picture.Rooms.Add(new RoomBox((int)pair.Key, minZ - ROOM_PADDING_METRES, maxZ + ROOM_PADDING_METRES, minY - ROOM_PADDING_METRES, maxY + ROOM_PADDING_METRES, room.fireValue));
                }
            }

            // The live fires: an extinguished one stays in its node for a few seconds
            List<GameObject> fires = fire.GetAllFires();
            if (fires != null)
            {
                foreach (GameObject node in fires)
                {
                    Fire burning = node ? node.GetComponentInChildren<Fire>() : null;
                    if (burning && !burning.IsExtinguished())
                    {
                        Vector3 local = hull.InverseTransformPoint(burning.transform.position);
                        picture.Fires.Add((local.x, local.y, local.z));
                    }
                }
            }
        }

        // A damage point is a breach while it is switched on (the game switches all off at the start, a hit switches one on and the repair switches it off again)
        CyclopsExternalDamageManager damage = cyclops.GetComponentInChildren<CyclopsExternalDamageManager>(true);
        if (damage != null && damage.damagePoints != null)
        {
            foreach (CyclopsDamagePoint point in damage.damagePoints)
            {
                if (point && point.gameObject.activeSelf)
                {
                    Vector3 local = hull.InverseTransformPoint(point.transform.position);
                    picture.Breaches.Add((local.x, local.y, local.z));
                }
            }
        }
    }

    // ---- the fire suppression system

    public static bool HasSuppressionModule(SubRoot cyclops)
    {
        return cyclops && cyclops.upgradeConsole && cyclops.upgradeConsole.modules != null && cyclops.upgradeConsole.modules.GetCount(TechType.CyclopsFireSuppressionModule) > 0;
    }

    public static Suppression SuppressionOf(SubRoot cyclops)
    {
        if (!HasSuppressionModule(cyclops))
        {
            return new Suppression(false, false, false, 0f);
        }
        CyclopsFireSuppressionSystemButton button = cyclops.GetComponentInChildren<CyclopsFireSuppressionSystemButton>(true);
        if (!button)
        {
            return new Suppression(true, false, false, 0f);
        }
        bool powered = cyclops.powerRelay && cyclops.powerRelay.IsPowered();
        float remaining = button.active ? 0f : Mathf.Max(0f, cyclops.fireSuppressionCD - (Time.time - button.fireSuppressionSystemStartTime));
        return new Suppression(true, button.active && powered && !cyclops.fireSuppressionState, cyclops.fireSuppressionState, remaining);
    }

    /// <summary>Starts the suppression the game's way (its own button's click: the voice line, the three seconds, the dousing and the cooldown; Nitrox relays it to the others).</summary>
    public static bool TrySuppress(SubRoot cyclops)
    {
        if (!SuppressionOf(cyclops).Ready)
        {
            return false;
        }
        CyclopsFireSuppressionSystemButton button = cyclops.GetComponentInChildren<CyclopsFireSuppressionSystemButton>(true);
        button.OnClick();
        return true;
    }
}
