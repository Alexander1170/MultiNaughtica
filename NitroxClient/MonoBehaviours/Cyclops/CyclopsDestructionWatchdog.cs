using System.Collections;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// A Cyclops that took its lethal hit is destroyed 18 s later by a timer (<c>SubRoot.Invoke("DestroyCyclopsSubRoot")</c>) that only exists on the client that took the hit.
/// If that player leaves in those seconds (a crash, a quit, a server stop), the timer is gone and the other client, or the save, keeps a Cyclops at 0 HP that never explodes and cannot be
/// repaired (V0, VD6). This is put on a Cyclops that is known to be at 0 HP: when the destruction has not arrived after the time the timer needs, the client that holds the lock of the
/// Cyclops runs the destruction itself (the normal path: loot, beacon and the announcement for everybody happen once, by that one client).
/// </summary>
public class CyclopsDestructionWatchdog : MonoBehaviour
{
    /// <summary>
    /// The 18 s of the timer, the 2.5 s swap delay of the destruction and a margin for the packet that tells the other clients.
    /// </summary>
    public const float FIRST_CHECK_AFTER_SECONDS = 25f;

    public const float CHECK_INTERVAL_SECONDS = 5f;

    private SubRoot subRoot;

    public static void WatchIfDying(SubRoot subRootToWatch, float health)
    {
        if (health > 0f || subRootToWatch.subDestroyed || subRootToWatch.GetComponent<CyclopsDestructionWatchdog>())
        {
            return;
        }

        subRootToWatch.gameObject.AddComponent<CyclopsDestructionWatchdog>().subRoot = subRootToWatch;
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(FIRST_CHECK_AFTER_SECONDS);

        while (subRoot && !subRoot.subDestroyed && subRoot.live && subRoot.live.health <= 0f)
        {
            if (subRoot.TryGetNitroxId(out NitroxId id) && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id))
            {
                Log.Info($"[{nameof(CyclopsDestructionWatchdog)}] {subRoot.name} is at 0 HP and was not destroyed (the client that took the hit is gone): destroying it here");
                subRoot.DestroyCyclopsSubRoot();
                break;
            }

            yield return new WaitForSeconds(CHECK_INTERVAL_SECONDS);
        }

        Destroy(this);
    }
}
