namespace NitroxClient.GameLogic.Navigator;

/// <summary>How much the navigator's map warns about a creature (<see cref="CreatureDangerRules.Classify" />).</summary>
public enum CreatureDanger
{
    /// <summary>Not shown on the map.</summary>
    None = 0,

    /// <summary>Shown only when the map is asked for the small stuff as well (the T key of the console): it bites, but a Cyclops does not care (Stalker, Sandshark, Crashfish and the like).</summary>
    Minor = 1,

    /// <summary>Orange: the dangers that are shown all the time (Warper, Crabsquid, Ampeel, Spine Eel, Bone Shark, Crabsnake, Lava Lizard).</summary>
    Dangerous = 2,

    /// <summary>Red: the leviathans (Reaper, Ghost, Sea Dragon and the like).</summary>
    Leviathan = 3
}

/// <summary>
/// Which creature is a danger for the navigator's map (docs/coop/VEHICLE_UPGRADES.md, the navigator console). Decided on the creature's <see cref="TechType" /> and not on the game's own behaviour type, which
/// counts the player, the Seamoth and the Prawn Suit as sharks. Pure, so that the table is tested; a wrong entry shows a wrong colour and nothing else.
/// </summary>
public static class CreatureDangerRules
{
    /// <summary>
    /// The danger of a creature of this tech type. The leviathans are those of the game's own creature table (<c>CreatureData</c>: Reaper, Ghost with its juvenile, Sea Dragon and the Sea Emperor family
    /// with its juvenile, whose claws hurt); the baby Sea Emperor is harmless. The Ampeel is the game's <c>Shocker</c>.
    /// </summary>
    public static CreatureDanger Classify(TechType tech)
    {
        switch (tech)
        {
            case TechType.ReaperLeviathan:
            case TechType.GhostLeviathan:
            case TechType.GhostLeviathanJuvenile:
            case TechType.SeaDragon:
            case TechType.SeaEmperor:
            case TechType.SeaEmperorLeviathan:
            case TechType.SeaEmperorJuvenile:
                return CreatureDanger.Leviathan;

            case TechType.Warper:
            case TechType.CrabSquid:
            case TechType.Shocker:
            case TechType.SpineEel:
            case TechType.Crabsnake:
            case TechType.LavaLizard:
            case TechType.BoneShark: // (it attacks the Cyclops: always shown)
                return CreatureDanger.Dangerous;

            // (an earlier build showed a map full of orange triangles with the T key doing nothing: these are the small ones that were in the orange group)
            case TechType.Stalker:
            case TechType.Sandshark:
            case TechType.Mesmer:
            case TechType.Bleeder:
            case TechType.Crash:
            case TechType.CaveCrawler:
            case TechType.RockPuncher:
            case TechType.Biter:
            case TechType.Gasopod:
            case TechType.PrecursorDroid:
                return CreatureDanger.Minor;

            default:
                return CreatureDanger.None;
        }
    }

    /// <summary>Whether a creature of this danger is drawn, given whether the map was asked for the minor ones too.</summary>
    public static bool IsShown(CreatureDanger danger, bool includeMinor)
    {
        return danger >= CreatureDanger.Dangerous || (danger == CreatureDanger.Minor && includeMinor);
    }
}
