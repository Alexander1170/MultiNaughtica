using System.Collections.Generic;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// Says once per session that a Harmony patch on a small method of the game ran. The Mono JIT may inline a small method into its callers before a patch is applied, and then the patch is silently
/// never called; the live check (a harness script that is not part of this repository) looks for these lines instead of assuming that the patches run.
/// </summary>
public static class PatchProbe
{
    private static readonly HashSet<string> fired = [];

    /// <summary>Logs "[VehicleUpgrades] patch fired: name" the first time it is called with that name in this session. Cheap afterwards: one lookup.</summary>
    public static void Fired(string name)
    {
        bool first;
        lock (fired)
        {
            first = fired.Add(name);
        }
        if (first)
        {
            Log.Info($"[VehicleUpgrades] patch fired: {name}");
        }
    }

    public static bool HasFired(string name)
    {
        lock (fired)
        {
            return fired.Contains(name);
        }
    }

    public static void Reset()
    {
        lock (fired)
        {
            fired.Clear();
        }
    }
}
