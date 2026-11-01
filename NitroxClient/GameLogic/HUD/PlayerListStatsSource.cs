using System;
using UnityEngine;

namespace NitroxClient.GameLogic.HUD;

/// <summary>
/// Reads what the game knows about a remote player and hands it to <see cref="PlayerListStatsRules" /> (item c, the stats line in the PDA's player list). The only place with game types: the
/// body of the remote player (his world position, valid in a Cyclops and in a vehicle too, they are children of the hull or of the vehicle), the vehicle, the sub, the pod and the chair, the local
/// player's position, the height of the sea, and the last vitals that <c>PlayerStatsProcessor</c> kept on the <see cref="RemotePlayer" />. Nothing is sent or asked from the server.
/// </summary>
public static class PlayerListStatsSource
{
    private static PlayerListStatsRules.StatsLabels? labels;

    /// <summary>The words in the language of the game, resolved once and again after a language change (<see cref="ResetLabels" />).</summary>
    public static PlayerListStatsRules.StatsLabels Labels
    {
        get
        {
            labels ??= PlayerListStatsRules.StatsLabels.Resolve(Lookup);
            return labels.Value;
        }
    }

    public static void ResetLabels()
    {
        labels = null;
    }

    /// <summary>The two lines for one remote player, as they are shown.</summary>
    public static PlayerListStatsRules.StatsLine Read(RemotePlayer remote, float now)
    {
        return PlayerListStatsRules.Format(Capture(remote, now), Labels);
    }

    /// <summary>What the line is made of, for the Debug probe as well.</summary>
    public static PlayerListStatsRules.StatsSnapshot Capture(RemotePlayer remote, float now)
    {
        PlayerListStatsRules.Point3 remotePosition = remote.Body ? ToPoint(remote.Body.transform.position) : PlayerListStatsRules.Point3.Unknown;
        PlayerListStatsRules.Point3 localPosition = Player.main ? ToPoint(Player.main.transform.position) : PlayerListStatsRules.Point3.Unknown;
        return PlayerListStatsRules.StatsSnapshot.Create(remote.LastVitals, now, remotePosition, localPosition, Ocean.GetOceanLevel(), Classify(remote), PlayerListStatsRules.PartsFor(remote.PlayerContext.GameMode));
    }

    /// <summary>Where the remote player is, from the state that his packets set on <see cref="RemotePlayer" />.</summary>
    public static PlayerListStatsRules.RemoteVehicleKind Classify(RemotePlayer remote)
    {
        // Unity objects: a bool test, not ?. (a destroyed vehicle is null for Unity and not for C#)
        Vehicle vehicle = remote.Vehicle;
        bool hasVehicle = vehicle;
        bool isSeamoth = hasVehicle && vehicle is SeaMoth;
        bool isPrawn = hasVehicle && vehicle is Exosuit;
        SubRoot subRoot = remote.SubRoot;
        bool inCyclops = remote.PilotingChair || (subRoot && subRoot.isCyclops);
        bool inBase = subRoot && subRoot.isBase;
        bool inLifepod = remote.EscapePod;
        return PlayerListStatsRules.ClassifyVehicle(hasVehicle, isSeamoth, isPrawn, inCyclops, inBase, inLifepod);
    }

    private static PlayerListStatsRules.Point3 ToPoint(Vector3 position)
    {
        return new PlayerListStatsRules.Point3(position.x, position.y, position.z);
    }

    private static string Lookup(string key, string fallback)
    {
        try
        {
            return Language.main && Language.main.TryGet(key, out string text) ? text : fallback;
        }
        catch (Exception)
        {
            // Language.main throws while the game is quitting; the English words are good enough then
            return fallback;
        }
    }
}
