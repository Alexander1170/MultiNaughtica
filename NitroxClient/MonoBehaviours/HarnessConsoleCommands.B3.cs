#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check B3 : the water of a base is sent by the holder of its lock and applied by the other clients.
// Two Debug console commands, registered by HarnessConsoleCommands.LiveBatch.cs from their names (LiveCommand_<name>):
//   b3floodstate                      logs, for every loaded base (no ghosts) on THIS client, the lock, the state of its flood simulation and the water of its cells
//   b3floodpump <level> [seconds]     keeps the used cells of every loaded base at <level> on THIS client for that long (default 300 s); "b3floodpump 0" stops it
// "Used cells" are the occupied cells of the base (Base.OccupiedCellIndexes) that are interior (flowData has Base.CellUsedMask): the cells the vanilla console command "baseflood <level>"
// (BaseFloodSim.OnConsoleCommand_baseflood, registered by every BaseFloodSim in Start) sets and the simulation (BaseFloodSim.Step) moves water between. That command sets the level once;
// the pump stands in for a leak that keeps a room wet for minutes, because a room without a leak drains by 1/60 per second (BaseFloodSim.ApplyDraining).
public partial class HarnessConsoleCommands
{
    private int b3PumpRun;

    private static string B3Format(float value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static bool B3IsUsedCell(Base @base, float[] levels, int index)
    {
        byte[] flow = @base.flowData;
        return index >= 0 && index < levels.Length && flow != null && index < flow.Length && (flow[index] & Base.CellUsedMask) != 0;
    }

    private void LiveCommand_b3floodstate(NotificationCenter.Notification n)
    {
        // One line per base: "[SyncHarness] b3floodstate: base <id8> lock <True|False> init <bool> shape <cells of the grid> occupied <used cells> sum <all cells> used <sum of the used cells>
        // mean <used/occupied> max <highest> wet <used cells above 0.01> all <occupied cells of any kind> levels [<index>:<level>,...]"
        // sum is the total that BaseFloodRules.Sum(flatValueGrid) compares; mean, max and wet are over the used cells
        int listed = 0;
        foreach (Base @base in FindObjectsOfType<Base>())
        {
            if (!@base || @base.isGhost)
            {
                continue;
            }

            string baseId = "noid";
            bool hasLock = false;
            if (@base.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id))
            {
                baseId = id.ToString().Substring(0, 8);
                hasLock = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>().HasAnyLockType(id);
            }

            BaseFloodSim sim = @base.GetComponent<BaseFloodSim>();
            bool initialized = false;
            int shapeSize = 0;
            float[] levels = null;
            if (sim)
            {
                initialized = sim.IsInitialized();
                shapeSize = sim.shape.Size;
                levels = sim.cellWaterLevel;
            }

            float total = 0f;
            if (levels != null)
            {
                foreach (float value in levels)
                {
                    total += value;
                }
            }

            List<int> occupiedCells = @base.OccupiedCellIndexes;
            int allOccupied = occupiedCells != null ? occupiedCells.Count : 0;
            int occupied = 0;
            float used = 0f;
            float max = 0f;
            int wet = 0;
            StringBuilder cellText = new();
            if (levels != null && occupiedCells != null)
            {
                foreach (int index in occupiedCells)
                {
                    if (!B3IsUsedCell(@base, levels, index))
                    {
                        continue;
                    }

                    float cellLevel = levels[index];
                    used += cellLevel;
                    max = cellLevel > max ? cellLevel : max;
                    wet += cellLevel > 0.01f ? 1 : 0;
                    if (occupied < 12)
                    {
                        cellText.Append(occupied > 0 ? "," : "").Append(index).Append(':').Append(B3Format(cellLevel));
                    }

                    occupied++;
                }
            }

            float mean = occupied > 0 ? used / occupied : 0f;
            string line = $"[SyncHarness] b3floodstate: base {baseId} lock {hasLock} init {initialized} shape {shapeSize} occupied {occupied} sum {B3Format(total)} used {B3Format(used)} mean {B3Format(mean)} max {B3Format(max)} wet {wet} all {allOccupied} levels [{cellText}]";
            Log.Info(line);
            listed++;
        }

        Log.Info($"[SyncHarness] b3floodstate: {listed} bases listed");
    }

    private void LiveCommand_b3floodpump(NotificationCenter.Notification n)
    {
        float level = LiveArgFloat(n, 0, 1f);
        float seconds = LiveArgFloat(n, 1, 300f);
        b3PumpRun++;
        if (level <= 0f || seconds <= 0f)
        {
            Log.Info($"[SyncHarness] b3floodpump: stopped (run {b3PumpRun})");
            return;
        }

        Log.Info($"[SyncHarness] b3floodpump: started run {b3PumpRun}, level {B3Format(level)} for {B3Format(seconds)} s");
        StartCoroutine(B3PumpAsync(b3PumpRun, level, seconds));
    }

    private IEnumerator B3PumpAsync(int run, float level, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        int ticks = 0;
        while (run == b3PumpRun && Time.realtimeSinceStartup < until)
        {
            // A leak holds a room at the level of the sea outside with a small wobble (it adds below that level and removes above it). The wobble matters: the holder only sends
            // when the total of the grid changed, so a level that is exactly constant would be sent once and then only every 30 s.
            float target = level - (ticks % 8) * 0.0005f;
            foreach (Base @base in FindObjectsOfType<Base>())
            {
                B3RaiseLevels(@base, target);
            }

            ticks++;
            yield return new WaitForSeconds(0.2f);
        }

        Log.Info($"[SyncHarness] b3floodpump: run {run} ended after {ticks} ticks");
    }

    private static void B3RaiseLevels(Base @base, float target)
    {
        if (!@base || @base.isGhost)
        {
            return;
        }

        BaseFloodSim sim = @base.GetComponent<BaseFloodSim>();
        List<int> occupiedCells = @base.OccupiedCellIndexes;
        if (!sim || !sim.IsInitialized() || sim.cellWaterLevel == null || occupiedCells == null)
        {
            return;
        }

        float[] levels = sim.cellWaterLevel;
        foreach (int index in occupiedCells)
        {
            if (B3IsUsedCell(@base, levels, index) && levels[index] < target)
            {
                levels[index] = target;
            }
        }
    }
}
#endif
