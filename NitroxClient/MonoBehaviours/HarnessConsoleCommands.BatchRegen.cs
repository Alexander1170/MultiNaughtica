#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "batch regeneration" (the design notes (not part of this repository)): the client side of the proof that a server restart does not double the world. Debug builds only. Registered by
// HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command>" and logs one "[SyncHarness] <name>: key=value ..." line.
//
//   idaudit [radius]   counts what this client's world holds, within radius metres of the local player (default 200): nitroxEntities = objects that carry a NitroxId, distinctIds = different ids among
//                      them, duplicateIds = objects that share an id with another object, shadowed = objects whose id is registered to another object, twinObjects = pairs of objects of the same
//                      prefab at the same spot (0.01 m) with different ids, twinCreatures = the same for creatures (a ghost copy). The server's [WorldAudit] line says the same for its registry;
//                      after a restart both must show 0 duplicates and no more twins than before the restart (identical resources of the baked game data are twinObjects too).
public partial class HarnessConsoleCommands
{
    private const float IDAUDIT_DEFAULT_RADIUS = 200f;

    private void LiveCommand_idaudit(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, IDAUDIT_DEFAULT_RADIUS);
            Vector3 center = Player.main != null ? Player.main.transform.position : Vector3.zero;
            NitroxEntity[] entities = UnityEngine.Object.FindObjectsOfType<NitroxEntity>();

            Dictionary<NitroxId, int> usersById = new Dictionary<NitroxId, int>();
            Dictionary<string, int> objectsBySpot = new Dictionary<string, int>();
            Dictionary<string, int> creaturesBySpot = new Dictionary<string, int>();
            int counted = 0;
            int shadowed = 0;
            foreach (NitroxEntity entity in entities)
            {
                if (entity == null || entity.Id == null || (entity.transform.position - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                counted++;
                usersById.TryGetValue(entity.Id, out int users);
                usersById[entity.Id] = users + 1;
                if (NitroxEntity.TryGetObjectFrom(entity.Id, out GameObject registered) && registered != entity.gameObject)
                {
                    shadowed++;
                }

                PrefabIdentifier prefab = entity.GetComponent<PrefabIdentifier>();
                if (prefab == null)
                {
                    continue;
                }

                Vector3 position = entity.transform.position;
                string spot = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}", prefab.ClassId, Mathf.Round(position.x * 100f), Mathf.Round(position.y * 100f), Mathf.Round(position.z * 100f));
                objectsBySpot.TryGetValue(spot, out int atSpot);
                objectsBySpot[spot] = atSpot + 1;
                if (entity.GetComponent<Creature>() != null)
                {
                    creaturesBySpot.TryGetValue(spot, out int creaturesAtSpot);
                    creaturesBySpot[spot] = creaturesAtSpot + 1;
                }
            }

            int duplicateIds = 0;
            foreach (KeyValuePair<NitroxId, int> pair in usersById)
            {
                if (pair.Value > 1)
                {
                    duplicateIds += pair.Value;
                }
            }

            Log.Info($"[SyncHarness] idaudit: radius={radius.ToString("F0", CultureInfo.InvariantCulture)} nitroxEntities={counted} distinctIds={usersById.Count} duplicateIds={duplicateIds} shadowed={shadowed} twinObjects={CountExtraCopies(objectsBySpot)} twinCreatures={CountExtraCopies(creaturesBySpot)}");
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] idaudit: failed reason={ex.GetType().Name}");
        }
    }

    private static int CountExtraCopies(Dictionary<string, int> bySpot)
    {
        int extra = 0;
        foreach (KeyValuePair<string, int> pair in bySpot)
        {
            if (pair.Value > 1)
            {
                extra += pair.Value - 1;
            }
        }
        return extra;
    }
}
#endif
