using System.Collections.Generic;
using Nitrox.Model.DataStructures;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// "Something happened to this vehicle until then": a short list of marks with an end time, which the on-screen lines of the power share ("Charging from Cyclops") and the pause of the game's
/// docking charge use. The clock is passed in, so that it can be tested.
/// </summary>
public sealed class TimedMarks
{
    private readonly Dictionary<NitroxId, (double Until, string Detail)> marks = new();

    public int Count => marks.Count;

    /// <summary>Marks <paramref name="id" /> until <paramref name="now" /> plus <paramref name="seconds" /> (a later call extends it).</summary>
    public void Mark(NitroxId id, double now, double seconds, string detail = "")
    {
        marks[id] = (now + seconds, detail);
    }

    public bool IsMarked(NitroxId id, double now)
    {
        return TryGet(id, now, out _);
    }

    /// <summary>True while the mark is on; an ended mark is removed.</summary>
    public bool TryGet(NitroxId id, double now, out string detail)
    {
        detail = "";
        if (id == null || !marks.TryGetValue(id, out (double Until, string Detail) mark))
        {
            return false;
        }
        if (now >= mark.Until)
        {
            marks.Remove(id);
            return false;
        }
        detail = mark.Detail;
        return true;
    }

    public void Clear()
    {
        marks.Clear();
    }
}
