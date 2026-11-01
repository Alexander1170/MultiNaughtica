using System;

namespace Nitrox.Model.Subnautica.Helper;

/// <summary>
/// The pair state of the Precursor teleporters travels as a story goal per teleporter identifier: goals are saved by the server, relayed once and replayed to every
/// joiner, which the game's own list (<c>TeleporterManager.activeTeleporters</c>, part of the single player save that Nitrox clients do not use) is not. Without it
/// the partner of an activated teleporter stayed dark for every player whose client had never loaded the activated side.
/// </summary>
public static class TeleporterGoalKey
{
    public const string PREFIX = "Nitrox_TeleporterActive:";

    public static string Format(string identifier) => PREFIX + identifier;

    public static bool TryParse(string key, out string identifier)
    {
        if (key != null && key.Length > PREFIX.Length && key.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            identifier = key.Substring(PREFIX.Length);
            return true;
        }

        identifier = null;
        return false;
    }
}
