#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Nitrox.Model.DataStructures;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sync test harness sampler (only attached in Debug builds, see <see cref="Multiplayer.InitMonoBehaviours" />).
///     Every 100 ms it writes one CSV line per tracked object to <c>%APPDATA%\Nitrox\logs\sync-&lt;player&gt;.csv</c>:
///     the local player, every remote player, every vehicle (Seamoth, Exosuit, Cyclops), every creature within
///     <see cref="CreatureRadius" /> and every resource entity (outcrop, raw material, fragment, dropped item) within
///     <see cref="RESOURCE_RADIUS" /> of the local player, with the position it is drawn at on this client. Two clients'
///     files are diffed offline by a harness script that is not part of this repository.
///     <para>
///         Started and stopped by the server's <c>/sync-start</c> and <c>/sync-stop</c> commands
///         (<see cref="Nitrox.Model.Subnautica.Packets.DebugSyncSamplerControl" />) or automatically after joining when
///         the environment variable <c>NITROX_SYNC_SAMPLER</c> is set to 1.
///     </para>
///     <para>
///         Columns: t_sync (server-synced clock, seconds), t_utc_ms (local wall clock, Unix ms), kind
///         (self|remote-player|cyclops-pawn|vehicle|creature|resource), id (NitroxId), tech (TechType), x y z (world position),
///         qx qy qz qw (rotation), vx vy vz (velocity), parent (NitroxId of the sub/vehicle the object is in, if any),
///         lx ly lz (position in the parent's local frame; for cyclops-pawn the pawn position inside the Cyclops),
///         sim (1 when this client simulates the object).
///     </para>
///     Allocation-free per sample apart from enumerators: numbers are formatted by hand into a reused buffer.
/// </summary>
public class SyncSampler : MonoBehaviour
{
    public const float SAMPLE_INTERVAL = 0.1f;
    public const string HEADER = "t_sync,t_utc_ms,kind,id,tech,x,y,z,qx,qy,qz,qw,vx,vy,vz,parent,lx,ly,lz,sim";
    private const float RESCAN_INTERVAL = 2f;
    private const float FLUSH_INTERVAL = 1f;
    private const string ENV_FLAG = "NITROX_SYNC_SAMPLER";
    private const string KIND_SELF = "self";
    private const string KIND_REMOTE_PLAYER = "remote-player";
    private const string KIND_CYCLOPS_PAWN = "cyclops-pawn";
    private const string KIND_VEHICLE = "vehicle";
    private const string KIND_CREATURE = "creature";
    private const string KIND_RESOURCE = "resource";
    /// <summary>Outcrops, raw materials, fragments and dropped items within this many metres of the local player are sampled.</summary>
    public const float RESOURCE_RADIUS = 30f;

    public static SyncSampler Instance { get; private set; }

    public bool Running { get; private set; }
    public string Label { get; private set; } = "";
    public float CreatureRadius { get; private set; } = 80f;
    public string FilePath { get; private set; } = "";

    private readonly List<Tracked> tracked = new();
    private readonly Dictionary<NitroxId, string> idTextCache = new();
    private readonly Dictionary<TechType, string> techTextCache = new();
    private readonly StringBuilder line = new(512);
    private readonly char[] digits = new char[24];
    private char[] lineBuffer = new char[1024];

    private TimeManager timeManager;
    private PlayerManager playerManager;
    private SimulationOwnership simulationOwnership;
    private IMultiplayerSession multiplayerSession;
    private StreamWriter writer;

    private float nextSample;
    private float nextRescan;
    private float nextFlush;
    private bool autoStartPending;

    public void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        string envFlag = Environment.GetEnvironmentVariable(ENV_FLAG);
        autoStartPending = envFlag is "1" or "true" or "True" or "TRUE";
    }

    public void Update()
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted || !Player.main)
        {
            return;
        }
        if (autoStartPending)
        {
            autoStartPending = false;
            StartSampling("env", CreatureRadius);
        }
        if (!Running)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now >= nextRescan)
        {
            nextRescan = now + RESCAN_INTERVAL;
            Rescan();
        }
        if (now >= nextSample)
        {
            nextSample = now + SAMPLE_INTERVAL;
            try
            {
                Sample();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SyncSampler] sampling failed, stopping");
                StopSampling();
                return;
            }
        }
        if (now >= nextFlush)
        {
            nextFlush = now + FLUSH_INTERVAL;
            writer?.Flush();
        }
    }

    public void OnDestroy()
    {
        StopSampling();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void StartSampling(string label, float creatureRadius)
    {
        StopSampling();

        timeManager = this.Resolve<TimeManager>();
        playerManager = this.Resolve<PlayerManager>();
        simulationOwnership = this.Resolve<SimulationOwnership>();
        multiplayerSession = this.Resolve<IMultiplayerSession>();

        Label = label ?? "";
        CreatureRadius = creatureRadius > 0 ? creatureRadius : 80f;

        string playerName = multiplayerSession.AuthenticationContext?.Username ?? "unknown";
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            playerName = playerName.Replace(invalid, '_');
        }
        FilePath = Path.Combine(Log.LogDirectory, $"sync-{playerName}.csv");

        try
        {
            Directory.CreateDirectory(Log.LogDirectory);
            FileStream stream = new(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            writer = new StreamWriter(stream, new UTF8Encoding(false), 1 << 16) { AutoFlush = false };
            writer.Write($"# player={playerName} label={Label} creature_radius={CreatureRadius.ToString(CultureInfo.InvariantCulture)} sample_interval={SAMPLE_INTERVAL.ToString(CultureInfo.InvariantCulture)} rtt_ms={RoundTripMs()} host={Environment.MachineName} started_utc={DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)}\n");
            writer.Write(HEADER);
            writer.Write('\n');
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"[SyncSampler] could not open {FilePath}");
            writer?.Dispose();
            writer = null;
            return;
        }

        tracked.Clear();
        nextRescan = 0;
        nextSample = 0;
        nextFlush = Time.unscaledTime + FLUSH_INTERVAL;
        Running = true;
        Log.Info($"[SyncSampler] started, label '{Label}', creature radius {CreatureRadius} m, writing {FilePath}");
        Log.InGame($"Sync sampler started ({Label})");
    }

    public void StopSampling()
    {
        if (!Running && writer == null)
        {
            return;
        }
        Running = false;
        tracked.Clear();
        try
        {
            // The round trip time at the end, next to the one in the header: proves the latency the run had
            writer?.Write($"# rtt_ms_stop={RoundTripMs()}\n");
            writer?.Flush();
            writer?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SyncSampler] error closing file");
        }
        writer = null;
        Log.Info($"[SyncSampler] stopped, file {FilePath}");
        Log.InGame("Sync sampler stopped");
    }

    /// <summary>
    ///     Rebuilds the list of vehicles and nearby creatures. Runs every <see cref="RESCAN_INTERVAL" /> seconds, so the
    ///     per-object component lookups stay out of the 10 Hz sampling path.
    /// </summary>
    private void Rescan()
    {
        tracked.Clear();
        Vector3 selfPosition = Player.main.transform.position;
        float radiusSquared = CreatureRadius * CreatureRadius;

        foreach (KeyValuePair<NitroxId, GameObject> pair in NitroxEntity.GetGameObjects())
        {
            GameObject gameObject = pair.Value;
            if (!gameObject)
            {
                continue;
            }
            if (gameObject.TryGetComponent(out Vehicle _))
            {
                AddTracked(pair.Key, gameObject, KIND_VEHICLE, CraftData.GetTechType(gameObject));
            }
            else if (gameObject.TryGetComponent(out SubRoot subRoot))
            {
                if (subRoot.isCyclops)
                {
                    AddTracked(pair.Key, gameObject, KIND_VEHICLE, TechType.Cyclops);
                }
            }
            else if (gameObject.TryGetComponent(out Creature _))
            {
                if ((gameObject.transform.position - selfPosition).sqrMagnitude <= radiusSquared)
                {
                    AddTracked(pair.Key, gameObject, KIND_CREATURE, CraftData.GetTechType(gameObject));
                }
            }
            else if ((gameObject.transform.position - selfPosition).sqrMagnitude <= RESOURCE_RADIUS * RESOURCE_RADIUS &&
                     (gameObject.TryGetComponent(out BreakableResource _) || gameObject.TryGetComponent(out ResourceTracker _) || gameObject.TryGetComponent(out Pickupable _)))
            {
                // Outcrops, raw materials, fragments and dropped items near the player: lets syncdiff check that both
                // clients have the same resource entity set (resource scenario).
                AddTracked(pair.Key, gameObject, KIND_RESOURCE, CraftData.GetTechType(gameObject));
            }
        }
    }

    private void AddTracked(NitroxId id, GameObject gameObject, string kind, TechType techType)
    {
        tracked.Add(new Tracked
        {
            Id = id,
            IdText = IdText(id),
            GameObject = gameObject,
            Transform = gameObject.transform,
            Rigidbody = gameObject.GetComponent<Rigidbody>(),
            Kind = kind,
            Tech = TechText(techType)
        });
    }

    private void Sample()
    {
        double syncTime = timeManager.RealTimeElapsed;
        long utcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        SampleLocalPlayer(syncTime, utcMs);

        foreach (RemotePlayer remotePlayer in playerManager.GetAll())
        {
            SampleRemotePlayer(syncTime, utcMs, remotePlayer);
        }

        for (int i = 0; i < tracked.Count; i++)
        {
            Tracked entry = tracked[i];
            if (!entry.GameObject || !entry.GameObject.activeInHierarchy)
            {
                continue;
            }
            Transform transform = entry.Transform;
            Vector3 velocity = entry.Rigidbody ? entry.Rigidbody.velocity : Vector3.zero;

            string parentText = "";
            bool hasLocal = false;
            Vector3 local = Vector3.zero;
            if (entry.Kind == KIND_VEHICLE)
            {
                // Docked vehicles sit inside a Cyclops or moonpool: report the position relative to that parent too.
                SubRoot parentSub = transform.parent ? transform.parent.GetComponentInParent<SubRoot>() : null;
                if (parentSub && parentSub.gameObject != entry.GameObject)
                {
                    parentText = IdTextOf(parentSub.gameObject);
                    local = parentSub.transform.InverseTransformPoint(transform.position);
                    hasLocal = true;
                }
            }

            WriteLine(syncTime, utcMs, entry.Kind, entry.IdText, entry.Tech, transform.position, transform.rotation, velocity, parentText, hasLocal, local, simulationOwnership.HasAnyLockType(entry.Id));
        }
    }

    private void SampleLocalPlayer(double syncTime, long utcMs)
    {
        Player player = Player.main;
        Transform playerTransform = player.transform;
        Vector3 velocity = player.playerController ? player.playerController.velocity : Vector3.zero;
        // Same rotation source as PlayerMovementBroadcaster so it is comparable with what remote clients receive.
        Quaternion rotation = MainCameraControl.main && MainCameraControl.main.viewModel ? MainCameraControl.main.viewModel.transform.rotation : playerTransform.rotation;

        string idText = Player.mainObject && Player.mainObject.TryGetNitroxId(out NitroxId localId) ? IdText(localId) : "local";

        SubRoot currentSub = player.currentSub;
        Vehicle mountedVehicle = player.currentMountedVehicle;
        string kind = KIND_SELF;
        string parentText = "";
        bool hasLocal = false;
        Vector3 local = Vector3.zero;

        if (currentSub)
        {
            parentText = IdTextOf(currentSub.gameObject);
            if (currentSub.isCyclops && player.TryGetComponent(out CyclopsMotor cyclopsMotor) && cyclopsMotor.Pawn != null && cyclopsMotor.Pawn.Handle)
            {
                kind = KIND_CYCLOPS_PAWN;
                local = cyclopsMotor.Pawn.Handle.transform.localPosition;
            }
            else
            {
                local = currentSub.transform.InverseTransformPoint(playerTransform.position);
            }
            hasLocal = true;
        }
        else if (mountedVehicle)
        {
            parentText = IdTextOf(mountedVehicle.gameObject);
            local = mountedVehicle.transform.InverseTransformPoint(playerTransform.position);
            hasLocal = true;
        }

        WriteLine(syncTime, utcMs, kind, idText, "", playerTransform.position, rotation, velocity, parentText, hasLocal, local, true);
    }

    private void SampleRemotePlayer(double syncTime, long utcMs, RemotePlayer remotePlayer)
    {
        GameObject body = remotePlayer.Body;
        if (body == null || !body)
        {
            return;
        }
        Transform bodyTransform = body.transform;
        Vector3 velocity = remotePlayer.RigidBody ? remotePlayer.RigidBody.velocity : Vector3.zero;

        string kind = KIND_REMOTE_PLAYER;
        string parentText = "";
        bool hasLocal = false;
        Vector3 local = Vector3.zero;

        if (remotePlayer.SubRoot)
        {
            parentText = IdTextOf(remotePlayer.SubRoot.gameObject);
            if (remotePlayer.Pawn != null && remotePlayer.Pawn.Handle)
            {
                kind = KIND_CYCLOPS_PAWN;
                local = remotePlayer.Pawn.Handle.transform.localPosition;
            }
            else
            {
                local = remotePlayer.SubRoot.transform.InverseTransformPoint(bodyTransform.position);
            }
            hasLocal = true;
        }
        else if (remotePlayer.Vehicle)
        {
            parentText = IdTextOf(remotePlayer.Vehicle.gameObject);
            local = remotePlayer.Vehicle.transform.InverseTransformPoint(bodyTransform.position);
            hasLocal = true;
        }

        WriteLine(syncTime, utcMs, kind, IdText(remotePlayer.PlayerContext.PlayerNitroxId), "", bodyTransform.position, bodyTransform.rotation, velocity, parentText, hasLocal, local, false);
    }

    private void WriteLine(double syncTime, long utcMs, string kind, string idText, string tech, Vector3 position, Quaternion rotation, Vector3 velocity, string parentText, bool hasLocal, Vector3 local, bool simulated)
    {
        line.Clear();
        AppendFixed(syncTime, 3);
        line.Append(',');
        AppendLong(utcMs);
        line.Append(',');
        line.Append(kind);
        line.Append(',');
        line.Append(idText);
        line.Append(',');
        line.Append(tech);
        line.Append(',');
        AppendVector(position, 3);
        AppendFixed(rotation.x, 4);
        line.Append(',');
        AppendFixed(rotation.y, 4);
        line.Append(',');
        AppendFixed(rotation.z, 4);
        line.Append(',');
        AppendFixed(rotation.w, 4);
        line.Append(',');
        AppendVector(velocity, 3);
        line.Append(parentText);
        line.Append(',');
        if (hasLocal)
        {
            AppendVector(local, 3);
        }
        else
        {
            line.Append(",,,");
        }
        line.Append(simulated ? '1' : '0');
        line.Append('\n');

        if (lineBuffer.Length < line.Length)
        {
            lineBuffer = new char[line.Length * 2];
        }
        line.CopyTo(0, lineBuffer, 0, line.Length);
        writer.Write(lineBuffer, 0, line.Length);
    }

    /// <summary>Appends "x,y,z," with the given number of decimals.</summary>
    private void AppendVector(Vector3 vector, int decimals)
    {
        AppendFixed(vector.x, decimals);
        line.Append(',');
        AppendFixed(vector.y, decimals);
        line.Append(',');
        AppendFixed(vector.z, decimals);
        line.Append(',');
    }

    /// <summary>Fixed-point formatting without allocating (StringBuilder.Append(double) allocates on Mono).</summary>
    private void AppendFixed(double value, int decimals)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            line.Append("nan");
            return;
        }
        if (value < 0)
        {
            line.Append('-');
            value = -value;
        }
        long scale = 1;
        for (int i = 0; i < decimals; i++)
        {
            scale *= 10;
        }
        double scaled = value * scale + 0.5;
        if (scaled >= 9e18)
        {
            line.Append(value.ToString("F3", CultureInfo.InvariantCulture));
            return;
        }
        long scaledInteger = (long)scaled;
        AppendLong(scaledInteger / scale);
        if (decimals > 0)
        {
            line.Append('.');
            long fraction = scaledInteger % scale;
            for (long digitScale = scale / 10; digitScale > 0; digitScale /= 10)
            {
                line.Append((char)('0' + (fraction / digitScale) % 10));
            }
        }
    }

    private void AppendLong(long value)
    {
        if (value < 0)
        {
            line.Append('-');
            value = -value;
        }
        int length = 0;
        do
        {
            digits[length++] = (char)('0' + value % 10);
            value /= 10;
        } while (value > 0 && length < digits.Length);
        while (length > 0)
        {
            line.Append(digits[--length]);
        }
    }

    private string IdText(NitroxId id)
    {
        if (id == null)
        {
            return "";
        }
        if (!idTextCache.TryGetValue(id, out string text))
        {
            text = id.ToString();
            idTextCache[id] = text;
        }
        return text;
    }

    private string IdTextOf(GameObject gameObject) => gameObject && gameObject.TryGetNitroxId(out NitroxId id) ? IdText(id) : "";

    private string TechText(TechType techType)
    {
        if (!techTextCache.TryGetValue(techType, out string text))
        {
            text = techType.ToString();
            techTextCache[techType] = text;
        }
        return text;
    }

    private int RoundTripMs() => multiplayerSession?.Client is NitroxClient.Communication.NetworkingLayer.LiteNetLib.LiteNetLibClient liteNetLibClient ? liteNetLibClient.RoundTripMs : -1;

    private struct Tracked
    {
        public NitroxId Id;
        public string IdText;
        public GameObject GameObject;
        public Transform Transform;
        public Rigidbody Rigidbody;
        public string Kind;
        public string Tech;
    }
}
#endif
