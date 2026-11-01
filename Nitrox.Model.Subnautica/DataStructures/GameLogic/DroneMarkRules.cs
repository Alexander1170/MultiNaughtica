using System;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// What the Scout Drone's mark key does (docs/coop/VEHICLE_UPGRADES.md, station 4): the operator aims the drone's view at something and presses the key; what is aimed at decides the kind of the mark (a creature is a
/// threat, anything else a resource) and its name. A second key makes the other kind of mark of the same thing. Pure rules, so that they are tested.
/// </summary>
public static class DroneMarkRules
{
    /// <summary>How far the drone's view reaches for a mark (m).</summary>
    public const float AIM_RANGE = 300f;

    public const string THREAT_LABEL = "Threat";
    public const string RESOURCE_LABEL = "Resource";

    /// <summary>
    /// The kind of the mark: a creature that the view is aimed at is a threat, anything else a resource; <paramref name="otherKind" /> (the second key) gives the other one of the two.
    /// </summary>
    public static byte Classify(bool aimedAtCreature, bool otherKind)
    {
        bool threat = aimedAtCreature != otherKind;
        return threat ? CyclopsNavRules.MARK_THREAT : CyclopsNavRules.MARK_RESOURCE;
    }

    /// <summary>The name of the mark: what the drone saw (a creature's or a resource's name) cleaned for the map and the HUD, or "Threat" or "Resource" when it saw nothing with a name.</summary>
    public static string LabelFor(byte kind, string seenName)
    {
        string label = CyclopsNavRules.SanitizeLabel(StripClone(seenName));
        if (label.Length > 0)
        {
            return label;
        }
        return kind == CyclopsNavRules.MARK_THREAT ? THREAT_LABEL : RESOURCE_LABEL;
    }

    /// <summary>An object's name without the "(Clone)" the engine adds to what it instantiates.</summary>
    public static string StripClone(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }
        const string CLONE = "(Clone)";
        string text = name.Trim();
        while (text.EndsWith(CLONE, StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - CLONE.Length).TrimEnd();
        }
        return text;
    }
}
