using System.Collections;
using System.Collections.Generic;
using NitroxClient.Communication.Abstract;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using WorldStreaming;

namespace NitroxClient.GameLogic;

public class Terrain
{
    private readonly IMultiplayerSession multiplayerSession;
    private readonly IPacketSender packetSender;

    private readonly HashSet<AbsoluteEntityCell> visibleCells = [];
    private readonly List<AbsoluteEntityCell> addedCells = [];
    private readonly List<AbsoluteEntityCell> removedCells = [];
    // We use a tuple rather than AbsoluteEntityCell for lightweight fetching by IsCellFullySpawned
    private readonly HashSet<(Int3, Int3, int)> fullySpawnedCells = [];

    private bool cellsPendingSync;
    private float bufferedTime = 0f;
    private const float TIME_BUFFER = 0.05f;

    /// <summary>
    /// A teleport only has to wait for the cells around its destination
    /// </summary>
    public const float TELEPORT_WORLD_LOAD_TIMEOUT = 10f;

    /// <summary>
    /// The first load of the world is much slower than a teleport (the loading screen is up meanwhile), but the server gives up on a player after 5 minutes of initial sync
    /// </summary>
    public const float INITIAL_SYNC_WORLD_LOAD_TIMEOUT = 60f;

    public Terrain(IMultiplayerSession multiplayerSession, IPacketSender packetSender)
    {
        this.multiplayerSession = multiplayerSession;
        this.packetSender = packetSender;
    }

    public void CellLoaded(Int3 batchId, Int3 cellId, int level)
    {
        AbsoluteEntityCell cell = new(batchId.ToDto(), cellId.ToDto(), level);

        if (!visibleCells.Contains(cell))
        {
            removedCells.Remove(cell);
            visibleCells.Add(cell);
            addedCells.Add(cell);
            cellsPendingSync = true;
        }
    }

    public void CellUnloaded(Int3 batchId, Int3 cellId, int level)
    {
        AbsoluteEntityCell cell = new(batchId.ToDto(), cellId.ToDto(), level);

        if (visibleCells.Contains(cell))
        {
            visibleCells.Remove(cell);
            removedCells.Add(cell);
            cellsPendingSync = true;
            fullySpawnedCells.Remove((batchId, cellId, level));
        }
    }

    public void AddFullySpawnedCell(AbsoluteEntityCell cell)
    {
        if (visibleCells.Contains(cell))
        {
            fullySpawnedCells.Add((cell.BatchId.ToUnity(), cell.CellId.ToUnity(), cell.Level));
        }
    }

    public bool IsCellFullySpawned(Int3 batchId, Int3 cellId, int level)
    {
        return fullySpawnedCells.Contains((batchId, cellId, level));
    }

    public void UpdateVisibility()
    {
        bufferedTime += Time.deltaTime;
        if (bufferedTime >= TIME_BUFFER)
        {
            if (cellsPendingSync)
            {
                CellVisibilityChanged cellsChanged = new(multiplayerSession.Reservation.SessionId, addedCells, removedCells);
                packetSender.Send(cellsChanged);

                addedCells.Clear();
                removedCells.Clear();

                cellsPendingSync = false;
            }

            bufferedTime = 0f;
        }
    }

    /// <summary>
    /// True if the terrain and the cells around the position are loaded and built already, so that a player put there has nothing to wait for and cannot fall through
    /// anything. This is the check that creatures use to know whether they can land on the ground (<c>DescendToGround.IsGroundLoaded</c>).
    /// </summary>
    public static bool IsLoadedAt(Vector3 position)
    {
        LargeWorldStreamer streamer = LargeWorldStreamer.main;
        return streamer && streamer.IsReady() && streamer.cellManager != null && streamer.IsRangeActiveAndBuilt(new Bounds(position, Vector3.zero));
    }

    /// <summary>
    /// Forces world streamer's to load the terrain around the MainCamera and waits until it's done to unfreeze the player. The player is unfrozen when the wait
    /// ends, however it ends: when the world did not settle within <paramref name="timeoutSeconds" /> (unscaled) or when something throws, the player is released
    /// anyway because a frozen player cannot do anything (no walking, no hand), while one released too early only risks falling.
    /// </summary>
    private const float SETTLE_REPORT_SECONDS = 5f;

    public static IEnumerator WaitForWorldLoad(float timeoutSeconds)
    {
        try
        {
            // In WorldStreamer.CreateStreamers() three coroutines are created to constantly call UpdateCenter() on the streamers
            // We force these updates so that the world streamer gets busy instantly
            WorldStreamer streamerV2 = LargeWorldStreamer.main.streamerV2;
            streamerV2.UpdateStreamingCenter(MainCamera.camera.transform.position);
            streamerV2.octreesStreamer.UpdateCenter(streamerV2.streamingCenter);
            streamerV2.lowDetailOctreesStreamer.UpdateCenter(streamerV2.streamingCenter);
            streamerV2.clipmapStreamer.UpdateCenter(streamerV2.streamingCenter);

            BoundedWait wait = new(timeoutSeconds, () => Time.unscaledTime);
            float nextReport = Time.unscaledTime + SETTLE_REPORT_SECONDS;
            while (wait.KeepWaiting(LargeWorldStreamer.main.IsWorldSettled))
            {
                if (Time.unscaledTime >= nextReport)
                {
                    // which of the four parts of IsWorldSettled is the one that is not true: the play-test of Build 3 waited the whole 60 s and the log could not say
                    nextReport = Time.unscaledTime + SETTLE_REPORT_SECONDS;
                    LargeWorldStreamer streamer = LargeWorldStreamer.main;
                    Log.Info($"[WorldSettle] still waiting after {wait.Waited:0} s: inited={streamer.inited} streamerIdle={streamer.isIdle} streamerV2Idle={(streamer.streamerV2 && streamer.streamerV2.IsIdle())} cellManagerIdle={(streamer.cellManager != null && streamer.cellManager.IsIdle())}");
                }
                yield return null;
            }

            if (wait.TimedOut)
            {
                Log.Warn($"The world did not settle within {timeoutSeconds:0.#} s, releasing the player at {MainCamera.camera.transform.position.ToString("F0")} anyway");
            }
            else
            {
                Log.Info($"The world settled after {wait.Waited:0.0} s");
            }
        }
        finally
        {
            if (Player.main)
            {
                Player.main.cinematicModeActive = false;
            }
        }
    }

    public static IEnumerator SafeWaitForWorldLoad(float timeoutSeconds)
    {
        // WaitForWorldLoad releases the player itself, also when it throws
        yield return WaitForWorldLoad(timeoutSeconds).OnYieldError(e => Log.Warn($"Something wrong happened while waiting for the terrain to load.\n{e}"));
    }
}
