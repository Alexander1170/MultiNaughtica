namespace NitroxClient.GameLogic;

/// <summary>
/// What a <c>StarshipDoor</c> shows and reports, by its open method (the nested enum <c>StarshipDoor.OpenMethodEnum</c>: 0 Manual, 1 Sealed, 2 Keycard, 3 Powercell), as the
/// vanilla <c>StarshipDoor.OnHandHover</c> decides it (ildump, 2026-10-25). Plain data, no game types.
/// </summary>
public static class StarshipDoorRules
{
    public const int MANUAL = 0;
    public const int SEALED = 1;
    public const int KEYCARD = 2;
    public const int POWERCELL = 3;

    /// <summary>
    /// The class id of the prefab <c>Wrecks_Starship_doors_sealed</c>, the laser-cut door of the wrecks and of the Aurora (read from the entity data of the world "Test", where the two doors the
    /// players cut carry a SealedDoorMetadata). 15 of them were in that world, 13 as placeholder children of wreck groups.
    /// </summary>
    public const string SEALED_DOOR_CLASS_ID = "6f01d2df-03b8-411f-808f-b3f0f37b0d5c";

    /// <summary>The tag of the log line <c>StarshipDoor_Start_Patch</c> writes for a laser-cut door (<c>grep "\[SealedDoor\]"</c> in the game log).</summary>
    public const string LOG_TAG_SEALED = "SealedDoor";

    /// <summary>The tag of the log line for a manual door that is locked when it starts (<c>grep "\[LockedDoor\]"</c>): the door that shows no hand text at all.</summary>
    public const string LOG_TAG_LOCKED = "LockedDoor";

    /// <summary>What <see cref="DescribeWorldData"/> says about a door whose place and id are the only ones of their kind.</summary>
    public const string WORLD_DATA_OK = "ok";

    /// <summary>
    /// The hand text of a door: the two language keys (or the door's own open / close text for a manual door), nothing when the vanilla code writes nothing.
    /// </summary>
    /// <param name="Main">The first line (<c>HandReticle.TextType.Hand</c>), null when the door writes no hand text at all.</param>
    /// <param name="Sub">The second line (<c>HandSubscript</c>), empty when the door writes none.</param>
    /// <param name="ShowsProgress">A progress ring for the cut is drawn (a sealed door).</param>
    public readonly record struct HandText(string? Main, string Sub, bool ShowsProgress)
    {
        public bool IsEmpty => Main == null;
    }

    /// <summary>
    /// A door that only the Sealed component decides (open method Sealed, a laser-cut door) has no state of its own: <c>OnDoorToggle</c> on it is the sound of the cut being done or a
    /// click on the door, <c>doorOpen</c> never changes. The server keeps ONE metadata per entity, so a <c>StarshipDoorMetadata</c> sent for such a door replaces its
    /// <c>SealedDoorMetadata</c> (the cut progress, or the fact that the door is open) and the next joiner or a restart gets a sealed door again. The other open methods keep their state in
    /// the door itself.
    /// </summary>
    public static bool ReportsOwnState(int openMethod)
    {
        return openMethod != SEALED;
    }

    /// <summary>
    /// <c>StarshipDoor.OnHandHover</c>: a manual door that is locked writes NOTHING (a door that is unlocked by repairing the damaged wall panel next to it looks like a plain
    /// closed door with no interaction, with a laser cutter or without); a sealed door says "Sealed door" / "Cut open to access" with a progress ring, whatever the player holds; keycard
    /// and power cell doors say "Locked door" with their instruction line.
    /// </summary>
    public static HandText Describe(int openMethod, bool locked, bool open, string openText, string closeText)
    {
        switch (openMethod)
        {
            case MANUAL:
                return locked ? new HandText(null, "", false) : new HandText(open ? closeText : openText, "", false);
            case SEALED:
                return new HandText("Sealed_Door", "SealedInstructions", true);
            case KEYCARD:
                return new HandText("Locked_Door", "DoorInstructions_Keycard", false);
            case POWERCELL:
                return new HandText("Locked_Door", "DoorInstructions_Powercell", false);
            default:
                return new HandText(null, "", false);
        }
    }

    /// <summary>
    /// The doors that get a log line when they start (<c>StarshipDoor_Start_Patch</c>), bug 10 of the first play sessions: the two kinds that the players can have meant by "a plain closed door
    /// with no interaction". A laser-cut door (open method Sealed) should say "Sealed door" and show a ring; a manual door that is locked says nothing at all (<see cref="Describe"/>) until
    /// the damaged wall panel next to it is repaired. The two lines tell the next session which kind of door the players stood in front of. Null for every other door.
    /// </summary>
    public static string? StartLogTag(int openMethod, bool locked)
    {
        if (openMethod == SEALED)
        {
            return LOG_TAG_SEALED;
        }

        return openMethod == MANUAL && locked ? LOG_TAG_LOCKED : null;
    }

    /// <summary>
    /// What a live probe says about the place of a door in the world data of this client: <paramref name="stackedCopies"/> other objects of its kind stand within half a metre of it (the same
    /// wreck twice: the server generated a batch a second time with other ids, see a harness script that is not part of this repository), <paramref name="objectsWithSameId"/> objects of the client carry its
    /// NitroxId (two entities with one id: the id registry knows only the last one, a cut or a repair is reported for the wrong object). "ok" when neither is the case.
    /// </summary>
    public static string DescribeWorldData(int stackedCopies, int objectsWithSameId)
    {
        bool stacked = stackedCopies > 0;
        bool sharedId = objectsWithSameId > 1;
        if (stacked && sharedId)
        {
            return "stacked+idshared";
        }

        if (stacked)
        {
            return "stacked";
        }

        return sharedId ? "idshared" : WORLD_DATA_OK;
    }
}
