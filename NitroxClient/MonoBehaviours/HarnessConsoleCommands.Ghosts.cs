#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Spawning;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "ghost copies" (a harness script that is not part of this repository, bug 9, the fish in the base that only one player sees). Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs
// (LiveCommand_<name> = command <name>), run with the server's "/console <player> ghosts [radius] [ghosts]" on EACH client, and logs "[SyncHarness] ghosts: ..." lines.
//
//   ghosts [radius] [ghosts]   lists every creature of this client's world within radius metres of the local player (default 60): its id (the whole id, to compare the lists of the two clients),
//                              tech type, verdict (CreatureCopyRules: SimulatedHere, Followed, NeverUpdated, GoneQuiet, Shadowed, NoId), distance, position, the lock this client holds
//                              (none, TRANSIENT, EXCLUSIVE), seconds since the simulating player's last position update reached the copy (never), kinematic state of its body, whether it
//                              is alive and whether a player carries it, and the nearest base with its distance. The first line is the summary with the counts; with the second argument
//                              "ghosts" only the copies that the verdict calls ghosts (Shadowed, GoneQuiet, NoId) are listed.
//                              A ghost that only one client has: the id is in the list of that client and not in the other's, while the other client has the cells loaded too.
public partial class HarnessConsoleCommands
{
    private const float GHOSTS_DEFAULT_RADIUS = 60f;
    private const int GHOSTS_MAX_LINES = 80;

    private void LiveCommand_ghosts(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, GHOSTS_DEFAULT_RADIUS);
            bool onlyGhosts = string.Equals(LiveArgString(n, 1, ""), "ghosts", StringComparison.OrdinalIgnoreCase);
            Vector3 center = Player.main != null ? Player.main.transform.position : Vector3.zero;
            SimulationOwnership ownership = NitroxServiceLocator.LocateService<SimulationOwnership>();

            List<(Vector3 Position, string Name)> bases = [];
            foreach (SubRoot subRoot in UnityEngine.Object.FindObjectsOfType<SubRoot>())
            {
                if (subRoot != null && subRoot.isBase)
                {
                    bases.Add((subRoot.transform.position, subRoot.gameObject.name));
                }
            }

            List<(float Distance, string Line)> items = [];
            Dictionary<CreatureCopyRules.Verdict, int> counts = new Dictionary<CreatureCopyRules.Verdict, int>();
            int copies = 0;
            foreach (Creature creature in UnityEngine.Object.FindObjectsOfType<Creature>())
            {
                if (creature == null)
                {
                    continue;
                }

                Vector3 position = creature.transform.position;
                float distance = Vector3.Distance(position, center);
                if (distance > radius)
                {
                    continue;
                }

                NitroxEntity entity = creature.GetComponent<NitroxEntity>();
                NitroxId id = entity != null ? entity.Id : null;
                bool hasId = id != null;
                bool registeredToThisObject = hasId && NitroxEntity.TryGetObjectFrom(id, out GameObject registered) && registered == creature.gameObject;
                bool simulatedHere = hasId && ownership.HasAnyLockType(id);
                RemotelyControlled remote = creature.GetComponent<RemotelyControlled>();
                bool everUpdated = remote != null && !float.IsNegativeInfinity(remote.LastUpdateTime);
                float updateAge = everUpdated ? Time.time - remote.LastUpdateTime : 0f;
                CreatureCopyRules.Verdict verdict = CreatureCopyRules.Judge(hasId, registeredToThisObject, simulatedHere, everUpdated, updateAge);

                copies++;
                counts[verdict] = counts.TryGetValue(verdict, out int count) ? count + 1 : 1;
                if (onlyGhosts && !CreatureCopyRules.IsGhost(verdict))
                {
                    continue;
                }

                string lockText = hasId && ownership.TryGetLockType(id, out var lockType) ? lockType.ToString() : "none";
                Rigidbody body = creature.GetComponent<Rigidbody>();
                LiveMixin liveMixin = creature.GetComponent<LiveMixin>();
                Pickupable pickupable = creature.GetComponent<Pickupable>();
                string baseText = "none";
                float nearestBase = float.PositiveInfinity;
                foreach ((Vector3 Position, string Name) candidate in bases)
                {
                    float baseDistance = Vector3.Distance(candidate.Position, position);
                    if (baseDistance < nearestBase)
                    {
                        nearestBase = baseDistance;
                        baseText = $"{candidate.Name.Replace(' ', '_')}@{GhostsNumber(baseDistance)}";
                    }
                }

                string line = $"[SyncHarness] ghosts: item id={(hasId ? id.ToString() : "none")} tech={CraftData.GetTechType(creature.gameObject)} verdict={verdict} d={GhostsNumber(distance)} pos={GhostsNumber(position.x)},{GhostsNumber(position.y)},{GhostsNumber(position.z)} lock={lockText} updAge={(everUpdated ? GhostsNumber(updateAge) : "never")} kinematic={(body != null ? body.isKinematic.ToString() : "none")} alive={(liveMixin == null || liveMixin.IsAlive())} carried={(pickupable != null && pickupable.attached)} base={baseText} parent={(creature.transform.parent != null ? creature.transform.parent.name.Replace(' ', '_') : "none")}";
                items.Add((distance, line));
            }

            string summary = $"[SyncHarness] ghosts: summary radius={GhostsNumber(radius)} copies={copies} simulatedHere={GhostsCount(counts, CreatureCopyRules.Verdict.SimulatedHere)} followed={GhostsCount(counts, CreatureCopyRules.Verdict.Followed)} neverUpdated={GhostsCount(counts, CreatureCopyRules.Verdict.NeverUpdated)} goneQuiet={GhostsCount(counts, CreatureCopyRules.Verdict.GoneQuiet)} shadowed={GhostsCount(counts, CreatureCopyRules.Verdict.Shadowed)} noId={GhostsCount(counts, CreatureCopyRules.Verdict.NoId)} listed={Math.Min(items.Count, GHOSTS_MAX_LINES)}";
            Log.Info(summary);
            items.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            for (int i = 0; i < items.Count && i < GHOSTS_MAX_LINES; i++)
            {
                Log.Info(items[i].Line);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] ghosts: failed reason={ex.GetType().Name} {ex.Message}");
        }
    }

    private static string GhostsNumber(float value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static int GhostsCount(Dictionary<CreatureCopyRules.Verdict, int> counts, CreatureCopyRules.Verdict verdict) => counts.TryGetValue(verdict, out int count) ? count : 0;
}
#endif
