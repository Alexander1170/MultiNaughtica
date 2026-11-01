#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nitrox.Model.DataStructures;
using NitroxClient.Extensions;
using NitroxClient.MonoBehaviours;
using UnityEngine;

namespace NitroxClient.Communication.Packets.Processors;

/// <summary>
///     Sync test harness (Debug builds): the "flag" records of the client story dump that go beyond the goal lists, one per observable piece of story state that a player
///     can see (Aurora, radiation leaks, infection, the gun terminal, the rocket, Precursor doors and teleporters, the incubator, time capsules, scanner and blueprint counts).
///     Three kinds of names, see a harness script that is not part of this repository: plain names are shared state and must be equal on every client, <c>player.*</c> names belong to the player
///     (compared only where a scenario says what to expect), <c>ent.*</c> names exist only for objects that this client has loaded (compared where two clients have them).
/// </summary>
internal static class StoryDumpFlags
{
    public static void Append(StringBuilder text)
    {
        AppendAurora(text);
        AppendInfection(text);
        AppendGunTerminal(text);
        AppendRocket(text);
        AppendTeleporters(text);
        AppendPrecursorObjects(text);
        AppendIncubator(text);
        AppendUnlocks(text);
    }

    private static void Flag(StringBuilder text, string name, object value)
    {
        text.Append($"flag\t{name}\t{value}\n");
    }

    private static string ShortId(Component component)
    {
        return component && component.TryGetNitroxId(out NitroxId id) ? id.ToString().Substring(0, 8) : "noid";
    }

    private static void AppendAurora(StringBuilder text)
    {
        CrashedShipExploder exploder = CrashedShipExploder.main;
        if (exploder)
        {
            Flag(text, "auroraExploded", exploder.IsExploded());
            Flag(text, "auroraCountdownTime", $"{exploder.timeToStartCountdown:F0}");
        }

        LeakingRadiation radiation = LeakingRadiation.main;
        if (!radiation)
        {
            return;
        }

        Flag(text, "radiationFixed", radiation.radiationFixed);
        Flag(text, "radiationRadius", $"{radiation.currentRadius:F0}");
        Flag(text, "leakCount", radiation.leaks.Count);
        // The leaks in a fixed order (the order in which they register is the order in which they spawn): their health is shared state, the radius is not exact (every client grows it itself)
        List<string> leaks = radiation.leaks.Where(leak => leak).Select(leak => $"{ShortId(leak)}={leak.liveMixin.health:F0}").OrderBy(entry => entry, StringComparer.Ordinal).ToList();
        Flag(text, "leakHealth", string.Join(",", leaks));
    }

    private static void AppendInfection(StringBuilder text)
    {
        if (!Player.main || !Player.main.infectedMixin)
        {
            return;
        }

        Flag(text, "player.infection", $"{Player.main.infectedMixin.GetInfectedAmount():F2}");
        Flag(text, "player.infectionRevealed", Player.main.infectionRevealed);
    }

    private static void AppendGunTerminal(StringBuilder text)
    {
        PrecursorDisableGunTerminal terminal = UnityEngine.Object.FindObjectOfType<PrecursorDisableGunTerminal>();
        if (!terminal)
        {
            return;
        }

        Flag(text, "ent.gunTerminalOpened", terminal.opened);
        Flag(text, "ent.gunTerminalPlayerCured", terminal.playerCured);
    }

    private static void AppendRocket(StringBuilder text)
    {
        Rocket rocket = UnityEngine.Object.FindObjectOfType<Rocket>();
        if (!rocket)
        {
            return;
        }

        Flag(text, "ent.rocketStage", rocket.currentRocketStage);
        Flag(text, "ent.rocketElevator", $"{(int)rocket.elevatorState}@{rocket.elevatorPosition:F1}");
        RocketPreflightCheckManager preflight = rocket.GetComponent<RocketPreflightCheckManager>();
        if (preflight)
        {
            Flag(text, "ent.rocketPreflight", string.Join(",", preflight.preflightChecks.Select(check => ((int)check).ToString()).OrderBy(check => check, StringComparer.Ordinal)));
        }
    }

    private static void AppendTeleporters(StringBuilder text)
    {
        if (TeleporterManager.main)
        {
            Flag(text, "teleportersActive", string.Join(",", TeleporterManager.main.activeTeleporters.OrderBy(id => id, StringComparer.Ordinal)));
        }
    }

    private static void AppendPrecursorObjects(StringBuilder text)
    {
        // Doors and key terminals that this client has loaded, by their id: compared for the ones both clients have
        foreach (PrecursorDoorway door in UnityEngine.Object.FindObjectsOfType<PrecursorDoorway>())
        {
            string id = ShortId(door);
            if (id != "noid")
            {
                Flag(text, $"ent.door.{id}", door.isOpen);
            }
        }

        foreach (PrecursorKeyTerminal terminal in UnityEngine.Object.FindObjectsOfType<PrecursorKeyTerminal>())
        {
            string id = ShortId(terminal);
            if (id != "noid")
            {
                Flag(text, $"ent.keyTerminal.{id}", terminal.slotted);
            }
        }

        foreach (PrecursorTeleporter teleporter in UnityEngine.Object.FindObjectsOfType<PrecursorTeleporter>())
        {
            string id = ShortId(teleporter);
            if (id != "noid")
            {
                Flag(text, $"ent.teleporter.{teleporter.teleporterIdentifier}.{id}", teleporter.isOpen);
            }
        }
    }

    private static void AppendIncubator(StringBuilder text)
    {
        Incubator incubator = UnityEngine.Object.FindObjectOfType<Incubator>();
        if (!incubator)
        {
            return;
        }

        Flag(text, "ent.incubatorPowered", incubator.powered);
        Flag(text, "ent.incubatorHatched", incubator.hatched);
    }

    private static void AppendUnlocks(StringBuilder text)
    {
        Flag(text, "knownTechCount", KnownTech.knownTech.Count);
        Flag(text, "scanComplete", PDAScanner.complete.Count);
        Flag(text, "scanPartial", PDAScanner.partial.Count);

        // A time capsule entry that the other player received has to have its text (the content comes from the web service and is shared with the entry)
        int capsulesWithoutContent = 0;
        foreach (string key in PDAEncyclopedia.entries.Keys)
        {
            if (PDAEncyclopedia.GetEntryData(key, out PDAEncyclopedia.EntryData data) && data.kind == PDAEncyclopedia.EntryData.Kind.TimeCapsule && !TimeCapsuleContentProvider.GetData(key, out _, out _, out _))
            {
                capsulesWithoutContent++;
            }
        }
        Flag(text, "timeCapsulesWithoutContent", capsulesWithoutContent);
    }
}
#endif
