using System.Diagnostics;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>What the clearance scan of a Cyclops found (<see cref="CyclopsClearance.Current" />).</summary>
public struct ClearanceReport
{
    public ClearanceSide Side;

    /// <summary>Metres between the hull and the terrain on that side; NaN when nothing was found within the warning distance.</summary>
    public float Gap;

    public ClearanceLevel Level;

    /// <summary>"Clearance 4 m, port", or empty when there is nothing to say.</summary>
    public string Text;
}

/// <summary>
/// The clearance warnings of a Cyclops with the Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md, station 1): while a player is aboard, five times a second, spheres are cast from the faces of the hull's box
/// (port, starboard, above, below, at five places along the keel) as far as the warning distance (<c>ClearanceWarningDistance</c>), and three more ahead along the way the Cyclops goes (astern while it reverses) as far
/// as five seconds of travel, against the terrain's colliders; <b>ahead the warning distance is that look-ahead</b> (never less than the setting), so that the faster the Cyclops goes the earlier a wall is
/// reported; the tightest gap, in proportion to its own distance, is "Clearance 4 m, port" on the pilot's screen (<see cref="RouteHud" />), at the console and, when it gets worse, in the game's message list. It is computed by every client that shows it
/// and is not synced. <b>The game only builds terrain colliders near the local player's camera</b>, so a gap farther from the player than that is not seen (an unbuilt place is never reported as clear: it is
/// left out). The rules (sides, levels, text) are <see cref="ClearanceRules" />. The cost of a scan is logged every ten seconds while it runs.
/// </summary>
public class CyclopsClearance : MonoBehaviour
{
    private const float POLL_SECONDS = 0.2f;
    private const float SPHERE_RADIUS = 1f;
    private const float AHEAD_MAX_RADIUS = 4f;
    private const float COST_LOG_SECONDS = 10f;
    private static readonly float[] STATIONS = [-0.8f, -0.4f, 0f, 0.4f, 0.8f];
    private static readonly float[] LATERALS = [-0.6f, 0f, 0.6f];
    private static readonly Vector3[] DIRECTIONS = [Vector3.right, Vector3.left, Vector3.up, Vector3.down];

    private SubRoot subRoot;
    private RouteGuide guide;
    private float nextPoll;
    private ClearanceLevel level;
    private ClearanceLevel announced;
    private float lastAnnounced = float.NegativeInfinity;
    private bool layerLogged;
    private int scans;
    private long totalTicks;
    private long maxTicks;
    private float nextCostLog;
    private ClearanceReport current;

    public ClearanceReport Current => current;

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        guide = GetComponent<RouteGuide>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;

        float warning = VehicleUpgradeSettings.ClearanceWarningDistance;
        bool aboard = Player.main && Player.main.currentSub == subRoot;
        if (!aboard || warning <= 0f || !TerrainSonar.IsAvailable(subRoot))
        {
            current = default;
            level = ClearanceLevel.Clear;
            announced = ClearanceLevel.Clear;
            return;
        }
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            Scan(warning);
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] clearance: the scan failed");
            current = default;
        }
        watch.Stop();
        Account(watch.ElapsedTicks);
    }

    private void Scan(float warning)
    {
        Transform root = subRoot.transform;
        if (!HullBounds.TryGet(root, out Bounds box))
        {
            current = default;
            return;
        }
        if (!layerLogged)
        {
            layerLogged = true;
            Log.Info($"[VehicleUpgrades] clearance: casting against layer {LayerID.TerrainCollider} ({LayerMask.LayerToName(LayerID.TerrainCollider)}), the game's own proximity sensors use layer 30; warning distance {warning:F0} m");
        }
        int mask = 1 << LayerID.TerrainCollider;
        Transform axis = subRoot.subAxis ? subRoot.subAxis : root;
        Worst worst = default;
        worst.Ratio = float.MaxValue;
        // once there is a warning, a gap a little over the limit is still part of it (the hysteresis of the level), so the candidates are taken up to that
        float slack = level != ClearanceLevel.Clear ? ClearanceRules.HYSTERESIS : 1f;

        // port, starboard, above, below: from the middle of each face, at five places along the keel
        foreach (Vector3 local in DIRECTIONS)
        {
            Vector3 worldDirection = root.TransformDirection(local);
            Vector3 inAxis = axis.InverseTransformDirection(worldDirection);
            ClearanceSide side = ClearanceRules.SideOfDirection(inAxis.x, inAxis.y, inAxis.z);
            float extent = Mathf.Abs(local.x) > 0.5f ? box.extents.x : box.extents.y;
            foreach (float station in STATIONS)
            {
                Vector3 startLocal = box.center + local * extent + Vector3.forward * (station * box.extents.z);
                float gap = CastGap(root.TransformPoint(startLocal), worldDirection, warning * slack, SPHERE_RADIUS, mask, true);
                worst.Consider(gap, warning, slack, side);
            }
        }

        // ahead: three casts from the face of the box that the Cyclops moves towards (the bow, or the stern while it reverses), along the way it goes, as far as five seconds of travel. The warning distance
        // for these is that look-ahead: five seconds of travel at its speed, never less than the setting. They start without the "already inside" test: a wall beside the nose is a side, not ahead
        Vector3 travel = guide && guide.Speed >= 1f ? guide.Velocity.normalized : axis.forward;
        float lookAhead = ClearanceRules.LookAheadDistance(guide ? guide.Speed : 0f, warning);
        float faceZ = root.InverseTransformDirection(travel).z;
        float bowSign = faceZ >= 0f ? 1f : -1f;
        Vector3 travelInAxis = axis.InverseTransformDirection(travel);
        ClearanceSide aheadSide = ClearanceRules.SideOfDirection(travelInAxis.x, travelInAxis.y, travelInAxis.z);
        float aheadRadius = Mathf.Min(AHEAD_MAX_RADIUS, box.extents.x);
        foreach (float lateral in LATERALS)
        {
            Vector3 startLocal = box.center + new Vector3(lateral * box.extents.x, 0f, bowSign * box.extents.z);
            float gap = CastGap(root.TransformPoint(startLocal), travel, lookAhead * slack, aheadRadius, mask, false);
            worst.Consider(gap, lookAhead, slack, aheadSide);
        }

        bool found = worst.Side != ClearanceSide.None;
        ClearanceLevel now = found ? ClearanceRules.LevelFor(level, worst.Gap, worst.Limit) : ClearanceLevel.Clear;
        current = new ClearanceReport
        {
            Side = found && now != ClearanceLevel.Clear ? worst.Side : ClearanceSide.None,
            Gap = found ? worst.Gap : float.NaN,
            Level = now,
            Text = found && now != ClearanceLevel.Clear ? ClearanceRules.WarningText(worst.Side, worst.Gap) : string.Empty
        };
        // said aloud when it got worse than what was said last, and no more often than every few seconds; a worsening that came too soon after the last one waits for the next scan instead of being forgotten
        if (ClearanceRules.ShouldAnnounce(announced, now, Time.unscaledTime - lastAnnounced))
        {
            lastAnnounced = Time.unscaledTime;
            announced = now;
            ErrorMessage.AddMessage(current.Text);
        }
        else if (now < announced)
        {
            announced = now;
        }
        level = now;
        if (current.Text.Length > 0)
        {
            RouteHud.FeedWarning(current.Text, now);
        }
    }

    /// <summary>The tightest of the gaps that the casts found, in proportion to the distance each of them looks for walls within.</summary>
    private struct Worst
    {
        public float Ratio;
        public float Gap;
        public float Limit;
        public ClearanceSide Side;

        public void Consider(float gap, float limit, float slack, ClearanceSide side)
        {
            if (gap >= limit * slack || limit <= 0f)
            {
                return;
            }
            float ratio = gap / limit;
            if (ratio < Ratio)
            {
                Ratio = ratio;
                Gap = gap;
                Limit = limit;
                Side = side;
            }
        }
    }

    /// <summary>The gap that a sphere cast finds: 0 when it starts inside terrain (if asked to look: a cast that starts inside something finds nothing), the distance and the sphere's own radius when it hits, MaxValue when it hits nothing.</summary>
    private static float CastGap(Vector3 start, Vector3 direction, float distance, float radius, int mask, bool checkStart)
    {
        if (checkStart && Physics.CheckSphere(start, radius, mask, QueryTriggerInteraction.Ignore))
        {
            return 0f;
        }
        if (Physics.SphereCast(start, radius, direction, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            return ClearanceRules.GapFromSphereHit(hit.distance, radius, 0f);
        }
        return float.MaxValue;
    }

    private void Account(long ticks)
    {
        scans++;
        totalTicks += ticks;
        maxTicks = System.Math.Max(maxTicks, ticks);
        if (Time.unscaledTime < nextCostLog)
        {
            return;
        }
        nextCostLog = Time.unscaledTime + COST_LOG_SECONDS;
        double toMicroseconds = 1000000.0 / Stopwatch.Frequency;
        Log.Info($"[VehicleUpgrades] clearance: {scans} scans in the last {COST_LOG_SECONDS:F0} s, average {totalTicks * toMicroseconds / System.Math.Max(1, scans):F0} us, longest {maxTicks * toMicroseconds:F0} us; now {(string.IsNullOrEmpty(current.Text) ? "clear" : current.Text)}");
        scans = 0;
        totalTicks = 0;
        maxTicks = 0;
    }
}
