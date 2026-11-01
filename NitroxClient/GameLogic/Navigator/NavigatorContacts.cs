using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Extensions;
using NitroxClient.Extensions;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.GameLogic.Navigator;

/// <summary>One thing on the navigator's map. It follows its object, so that the icon moves smoothly between two refreshes of the list.</summary>
public sealed class Contact
{
    public Contact(string key, ContactKind kind, string label, Color color, Transform source)
    {
        Key = key;
        Kind = kind;
        Label = label;
        Color = color;
        Source = source;
    }

    /// <summary>Stable between refreshes, so that the map keeps the icon of a contact.</summary>
    public string Key { get; }

    public ContactKind Kind { get; }
    public CreatureDanger Danger { get; set; }
    public string Label { get; }
    public Color Color { get; }
    public Transform Source { get; }

    /// <summary>The icon is the contact's own colour, drawn with a ring: a contact of a teammate.</summary>
    public bool Highlight { get; set; }

    public bool Alive => Source;

    /// <summary>For a <see cref="ContactKind.Mark" />: its id (so that the console can remove it) and kind.</summary>
    public NitroxId MarkId { get; set; }

    public byte MarkKind { get; set; }
    public Vector3 Position => Source ? Source.position : Vector3.zero;
}

/// <summary>
/// The things that the navigator's map and, later, the helm's marks and the clearance warnings know about, found from what the game and the fork already keep: the game's own ping list (vehicles, other
/// Cyclopses, beacons, lifepods), the teammates, the bases and the dangerous creatures. The creatures are registered when they start (<c>Creature_Start_Patch</c>), because the game keeps no list of them.
/// A creature appears on a client only once that client has loaded the cells it is in, so the map shows the dangers that are near enough to be loaded; a server-fed radar is not part of this milestone.
/// <para>
/// The list is rebuilt by <see cref="Collect" />, which the console calls twice a second while it is open; nothing runs while it is closed.
/// </para>
/// </summary>
public static class NavigatorContacts
{
    private sealed class CreatureEntry
    {
        public Creature Creature;
        public CreatureDanger Danger;
        public string Label;
        public TechType Tech;
    }

    private static readonly Dictionary<int, CreatureEntry> creatures = [];
    private static readonly HashSet<SubRoot> bases = [];
    private static readonly List<int> stale = [];
    private static bool seeded;
    private static bool subscribed;

    public static int RegisteredCreatures => creatures.Count;
    public static int RegisteredBases => bases.Count;

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: nothing of this session may be left over for the next one.</summary>
    public static void Reset()
    {
        creatures.Clear();
        bases.Clear();
        stale.Clear();
        seeded = false;
    }

    /// <summary>A creature has started (or is found by the first look around): kept only if the map would ever draw it.</summary>
    public static void RegisterCreature(Creature creature)
    {
        if (!creature)
        {
            return;
        }
        int id = creature.GetInstanceID();
        if (creatures.ContainsKey(id))
        {
            return;
        }
        TechType tech = CraftData.GetTechType(creature.gameObject);
        CreatureDanger danger = CreatureDangerRules.Classify(tech);
        if (danger == CreatureDanger.None)
        {
            return;
        }
        creatures[id] = new CreatureEntry { Creature = creature, Danger = danger, Tech = tech, Label = NameOf(tech) };
    }

    public static void RegisterBase(SubRoot baseRoot)
    {
        if (baseRoot && baseRoot.isBase)
        {
            bases.Add(baseRoot);
        }
    }

    /// <summary>
    /// Rebuilds the list of what is within <paramref name="range" /> metres of <paramref name="cyclops" />. The Cyclops itself is always the first contact. Never more than
    /// <see cref="ContactRules.MAX_CONTACTS" />: the least important and farthest are left out.
    /// </summary>
    public static void Collect(SubRoot cyclops, float range, bool includeMinor, List<Contact> into)
    {
        into.Clear();
        if (!cyclops)
        {
            return;
        }
        SeedOnce();
        Vector3 here = cyclops.transform.position;
        into.Add(new Contact("cyclops", ContactKind.Cyclops, cyclops.name, Color.white, cyclops.transform));

        CollectMarks(cyclops, here, range, into);
        CollectCreatures(here, range, includeMinor, into);
        CollectPings(cyclops, here, range, into);
        CollectTeammates(cyclops, here, range, into);
        CollectBases(here, range, into);

        if (into.Count > ContactRules.MAX_CONTACTS)
        {
            into.Sort((a, b) => Rank(b, here).CompareTo(Rank(a, here)));
            into.RemoveRange(ContactRules.MAX_CONTACTS, into.Count - ContactRules.MAX_CONTACTS);
        }
    }

    /// <summary>Important first, and nearer first among equals.</summary>
    private static float Rank(Contact contact, Vector3 here)
    {
        return ContactRules.Priority(contact.Kind, contact.Danger) - Mathf.Min(Vector3.Distance(here, contact.Position), 999f) / 1000f;
    }

    private static void SeedOnce()
    {
        if (seeded)
        {
            return;
        }
        seeded = true;
        foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
        {
            RegisterCreature(creature);
        }
        foreach (SubRoot sub in UnityEngine.Object.FindObjectsOfType<SubRoot>())
        {
            RegisterBase(sub);
        }
    }

    private static void CollectMarks(SubRoot cyclops, Vector3 here, float range, List<Contact> into)
    {
        if (!cyclops.TryGetNitroxId(out NitroxId id))
        {
            return;
        }
        foreach (NavMark mark in NavMarks.MarksOf(id))
        {
            // the ping object carries the mark's place; it exists once the Cyclops' console has synced the marks. A mark beyond the range is kept: the screen draws it on the rim, smaller, as a direction to go in
            if (!mark.PingObject)
            {
                continue;
            }
            Color color = mark.Kind switch
            {
                CyclopsNavRules.MARK_HAZARD => new Color(1f, 0.55f, 0.1f),
                CyclopsNavRules.MARK_TARGET => new Color(1f, 0.85f, 0.2f),
                CyclopsNavRules.MARK_THREAT => new Color(1f, 0.25f, 0.2f),
                CyclopsNavRules.MARK_RESOURCE => new Color(0.25f, 0.9f, 0.7f),
                _ => new Color(0.3f, 0.85f, 1f)
            };
            into.Add(new Contact(mark.Key, ContactKind.Mark, CyclopsNavRules.PingText(mark.OwnerName, mark.Label), color, mark.PingObject.transform) { MarkId = mark.Id, MarkKind = mark.Kind });
        }
    }

    private static void CollectCreatures(Vector3 here, float range, bool includeMinor, List<Contact> into)
    {
        stale.Clear();
        foreach (KeyValuePair<int, CreatureEntry> pair in creatures)
        {
            CreatureEntry entry = pair.Value;
            if (!entry.Creature)
            {
                stale.Add(pair.Key);
                continue;
            }
            if (!entry.Creature.isActiveAndEnabled || !CreatureDangerRules.IsShown(entry.Danger, includeMinor))
            {
                continue;
            }
            if (entry.Creature.liveMixin && !entry.Creature.liveMixin.IsAlive())
            {
                continue;
            }
            Transform transform = entry.Creature.transform;
            if (!ContactRules.WithinRange(Vector3.Distance(here, transform.position), range))
            {
                continue;
            }
            Color color = entry.Danger == CreatureDanger.Leviathan ? new Color(1f, 0.15f, 0.12f) : new Color(1f, 0.55f, 0.1f);
            into.Add(new Contact($"creature:{pair.Key}", ContactKind.Creature, entry.Label, color, transform) { Danger = entry.Danger });
        }
        foreach (int id in stale)
        {
            creatures.Remove(id);
        }
    }

    private static void CollectPings(SubRoot cyclops, Vector3 here, float range, List<Contact> into)
    {
        PlayerManager players = NitroxServiceLocator.LocateService<PlayerManager>();
        Dictionary<string, PingInstance>.Enumerator pings = PingManager.GetEnumerator();
        while (pings.MoveNext())
        {
            PingInstance ping = pings.Current.Value;
            if (!ping || !ping.visible || !ping.origin)
            {
                continue;
            }
            ContactKind kind;
            switch (ping.pingType)
            {
                case PingType.Seamoth:
                case PingType.Exosuit:
                    kind = ContactKind.Vehicle;
                    break;
                case PingType.Cyclops:
                    kind = ContactKind.OtherCyclops;
                    break;
                case PingType.Beacon:
                    kind = ContactKind.Beacon;
                    break;
                case PingType.Lifepod:
                    kind = ContactKind.Lifepod;
                    break;
                default:
                    continue;
            }
            Transform origin = ping.origin;
            if (origin == cyclops.transform || origin.IsChildOf(cyclops.transform) || origin.GetComponentInParent<VirtualCyclops>())
            {
                continue;
            }
            if (kind == ContactKind.Vehicle && IsDocked(origin))
            {
                continue;
            }
            if (!ContactRules.WithinRange(Vector3.Distance(here, origin.position), range))
            {
                continue;
            }
            string label = ping.GetLabel();
            Color color = kind switch
            {
                ContactKind.Beacon => new Color(1f, 0.85f, 0.2f),
                ContactKind.Lifepod => new Color(0.4f, 1f, 0.6f),
                ContactKind.OtherCyclops => new Color(0.75f, 0.85f, 1f),
                _ => new Color(0.55f, 0.85f, 1f)
            };
            bool highlight = false;
            if (kind == ContactKind.Vehicle)
            {
                Vehicle vehicle = origin.GetComponentInParent<Vehicle>();
                foreach (RemotePlayer remote in players.GetAll())
                {
                    if (vehicle && remote.Vehicle == vehicle)
                    {
                        color = remote.PlayerSettings.PlayerColor.ToUnity();
                        label = string.IsNullOrEmpty(remote.PlayerName) ? label : $"{remote.PlayerName} ({label})";
                        highlight = true;
                        break;
                    }
                }
            }
            into.Add(new Contact($"ping:{pings.Current.Key}", kind, label, color, origin) { Highlight = highlight });
        }
    }

    private static void CollectTeammates(SubRoot cyclops, Vector3 here, float range, List<Contact> into)
    {
        foreach (RemotePlayer remote in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
        {
            // Aboard a vehicle or a sub they are that vehicle's or sub's contact; the map shows the ones who swim or walk outside
            if (remote.Vehicle || remote.SubRoot || remote.EscapePod || remote.Body == null)
            {
                continue;
            }
            Transform body = remote.Body.transform;
            if (!ContactRules.WithinRange(Vector3.Distance(here, body.position), range))
            {
                continue;
            }
            into.Add(new Contact($"player:{remote.PlayerName}", ContactKind.Teammate, remote.PlayerName, remote.PlayerSettings.PlayerColor.ToUnity(), body) { Highlight = true });
        }
    }

    private static void CollectBases(Vector3 here, float range, List<Contact> into)
    {
        foreach (SubRoot baseRoot in bases)
        {
            if (!baseRoot)
            {
                continue;
            }
            if (ContactRules.WithinRange(Vector3.Distance(here, baseRoot.transform.position), range))
            {
                into.Add(new Contact($"base:{baseRoot.GetInstanceID()}", ContactKind.Base, "Base", new Color(0.6f, 0.9f, 0.9f), baseRoot.transform));
            }
        }
        bases.RemoveWhere(baseRoot => !baseRoot);
    }

    private static bool IsDocked(Transform origin)
    {
        Vehicle vehicle = origin.GetComponentInParent<Vehicle>();
        return vehicle && vehicle.docked;
    }

    public static string NameOf(TechType tech)
    {
        try
        {
            string key = TechTypeExtensions.AsString(tech, false);
            if (Language.main && Language.main.TryGet(key, out string name) && !string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch (Exception exception)
        {
            Log.Debug($"[VehicleUpgrades] navigator: could not read the name of {tech}: {exception.Message}");
        }
        return tech.ToString();
    }
}
