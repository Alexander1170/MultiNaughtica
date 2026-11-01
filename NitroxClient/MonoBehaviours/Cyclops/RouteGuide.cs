using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using NitroxClient.Extensions;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>What the guidance of a Cyclops' route says right now (<see cref="RouteGuide.Current" />).</summary>
public struct RouteGuidance
{
    /// <summary>There is a route with a waypoint still to go to.</summary>
    public bool Active;

    /// <summary>There is a route and every waypoint of it is reached.</summary>
    public bool Finished;

    /// <summary>The index of the next waypoint, and the number of waypoints.</summary>
    public int Index;

    public int Count;
    public Vector3 Target;

    /// <summary>Metres from the hull (its box, not its middle) to the next waypoint.</summary>
    public float HullDistance;

    /// <summary>The direction of the next waypoint in the Cyclops' frame (x to starboard, y up, z ahead), as the angles of an arrow in degrees: positive yaw to starboard, positive pitch up.</summary>
    public float Yaw;

    public float Pitch;
    public bool OffCourse;

    /// <summary>Metres from the line between the last waypoint and the next (NaN on the first leg, which has no line).</summary>
    public float CrossTrack;

    public float Speed;
    public float Eta;

    /// <summary>Counts up with every new set of numbers (the guide works ten times a second), so that what is drawn from them can wait for a new one.</summary>
    public int Revision;
}

/// <summary>
/// The guidance of the route of a Cyclops (docs/coop/VEHICLE_UPGRADES.md, station 1), on every client for every real Cyclops: from the route the navigators set (<see cref="NavRoute" />) and where the Cyclops is and
/// goes it works out the next waypoint, how far it is from the hull, which way to turn, whether the Cyclops is off course, and, on the client of the player who holds the Cyclops, when a waypoint is reached
/// (the route then goes on to the next, which the server tells the others). For the players aboard it shows the next waypoint as a ping on the HUD (the edge arrow and the distance of the game's own pings), a line in the
/// water from the bow to the next waypoints, and the numbers (<see cref="RouteHud" />); the line turns orange when the Cyclops is off course. It ticks ten times a second from its own <c>Update</c>: the game's
/// own update of a far Cyclops does not run.
/// </summary>
public class RouteGuide : MonoBehaviour
{
    private const float POLL_SECONDS = 0.1f;
    private const float ADVANCE_PAUSE_SECONDS = 1.5f;
    private const float MAX_LINE_METRES = 400f;

    private SubRoot subRoot;
    private NitroxId cyclopsId;
    private float nextPoll;
    private float lastTime;
    private Vector3 lastPosition;
    private Vector3 velocity;
    private bool hasLast;
    private bool offCourse;
    private OffCourseReason offReason;
    private int revision;
    private NitroxId legTargetId;
    private Vector3 legStart;
    private float advanceBlockedUntil;
    private RouteGuidance current;

    // the next waypoint as a ping on the HUD (made again when what it says changes)
    private GameObject pingObject;
    private (int Index, int Count, bool Off) pingKey;
    private int pingGeneration;

    // the line in the water: a halo and a core for each of the two legs
    private LineRenderer[] halos;
    private LineRenderer[] cores;
    private GameObject lineObject;

    public RouteGuidance Current => current;

    /// <summary>How the Cyclops moves, measured from where it was ten times a second (the same on every client).</summary>
    public Vector3 Velocity => velocity;

    public float Speed => velocity.magnitude;
    public NitroxId CyclopsId => cyclopsId;
    public SubRoot SubRoot => subRoot;
    public bool Aboard => Player.main && Player.main.currentSub == subRoot;

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        NavRoute.Subscribe();
    }

    private void OnDestroy()
    {
        DestroyPing();
        if (lineObject)
        {
            Destroy(lineObject);
        }
        if (cyclopsId != null)
        {
            NavRoute.ForgetCyclops(cyclopsId);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        Tick();
    }

    private void Tick()
    {
        if (cyclopsId == null && !subRoot.TryGetNitroxId(out cyclopsId))
        {
            return;
        }
        TrackVelocity();
        bool usable = VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received && TerrainSonar.HasModule(subRoot);
        NavWaypoint next = usable ? NavRoute.NextWaypoint(cyclopsId) : null;
        int count = usable ? NavRoute.CountOf(cyclopsId) : 0;
        if (next == null)
        {
            current = new RouteGuidance { Finished = usable && NavRoute.IsFinished(cyclopsId), Count = count, Index = count, Revision = ++revision };
            offCourse = false;
            offReason = OffCourseReason.None;
            legTargetId = null;
            return;
        }
        if (!next.Id.Equals(legTargetId))
        {
            // a new leg: the first one starts where the Cyclops is when the guide first sees the waypoint (the later ones start at the waypoint before)
            legTargetId = next.Id;
            legStart = subRoot.transform.position;
        }
        current = Work(next, count);
        if (HoldsLock() && Time.time >= advanceBlockedUntil && ShouldAdvance(next))
        {
            advanceBlockedUntil = Time.time + ADVANCE_PAUSE_SECONDS;
            NavRoute.AdvanceByHolder(cyclopsId);
        }
    }

    private void TrackVelocity()
    {
        Vector3 position = subRoot.transform.position;
        float now = Time.time;
        if (hasLast && now > lastTime)
        {
            Vector3 measured = (position - lastPosition) / (now - lastTime);
            velocity = Vector3.Lerp(velocity, measured, 0.4f);
        }
        lastPosition = position;
        lastTime = now;
        hasLast = true;
    }

    private Transform Axis => subRoot.subAxis ? subRoot.subAxis : subRoot.transform;

    private RouteGuidance Work(NavWaypoint next, int count)
    {
        Transform axis = Axis;
        Vector3 local = axis.InverseTransformPoint(next.AimPosition);
        (float yaw, float pitch) = NavigationRules.ArrowAngles(local.x, local.y, local.z);
        if (!HullBounds.TryDistanceTo(subRoot.transform, next.AimPosition, out float hullDistance))
        {
            hullDistance = Vector3.Distance(subRoot.transform.position, next.AimPosition);
        }
        float speed = velocity.magnitude;
        Vector3 localVelocity = axis.InverseTransformDirection(velocity);
        // the heading is a horizontal angle: it means something while the Cyclops goes forwards or sideways, not while it only rises or sinks
        float horizontalSpeed = Mathf.Sqrt(localVelocity.x * localVelocity.x + localVelocity.z * localVelocity.z);
        float bearingError = horizontalSpeed >= NavigationRules.MIN_HEADING_SPEED ? NavigationRules.WrapDegrees(yaw - Mathf.Atan2(localVelocity.x, localVelocity.z) * Mathf.Rad2Deg) : float.NaN;
        float crossTrack = CrossTrackOf(next, out _, out _);
        offReason = NavigationRules.OffCourse(offReason, crossTrack, bearingError, horizontalSpeed);
        offCourse = offReason != OffCourseReason.None;
        return new RouteGuidance
        {
            Active = true,
            Index = NavRoute.NextOf(cyclopsId),
            Count = count,
            Target = next.AimPosition,
            HullDistance = hullDistance,
            Yaw = yaw,
            Pitch = pitch,
            OffCourse = offCourse,
            CrossTrack = crossTrack,
            Speed = speed,
            Eta = NavigationRules.EtaSeconds(hullDistance, speed),
            Revision = ++revision
        };
    }

    /// <summary>The distance from the Cyclops to the line from the waypoint before the next one to the next one; NaN on the first leg. <paramref name="along" /> and <paramref name="length" /> are how far along it the Cyclops is and how long it is.</summary>
    private float CrossTrackOf(NavWaypoint next, out float along, out float length)
    {
        along = 0f;
        length = 0f;
        int index = NavRoute.NextOf(cyclopsId);
        IReadOnlyList<NavWaypoint> points = NavRoute.PointsOf(cyclopsId);
        if (index <= 0 || index >= points.Count)
        {
            return float.NaN;
        }
        Vector3 from = points[index - 1].AimPosition;
        Vector3 to = next.AimPosition;
        Vector3 here = subRoot.transform.position;
        (float t, _, _, _, float distance) = NavigationRules.NearestOnSegment(here.x, here.y, here.z, from.x, from.y, from.z, to.x, to.y, to.z);
        length = Vector3.Distance(from, to);
        along = t * length;
        return distance;
    }

    private bool ShouldAdvance(NavWaypoint next)
    {
        if (NavigationRules.HasReached(current.HullDistance))
        {
            return true;
        }
        // slid past it: the leg is covered and the hull is not far from the point (the first leg starts where the Cyclops was when it got the route)
        return LegOf(next, out float along, out float length) && NavigationRules.HasPassed(along, length, current.HullDistance);
    }

    /// <summary>How far along the leg to the next waypoint the Cyclops is, and how long the leg is; false when the leg has no length.</summary>
    private bool LegOf(NavWaypoint next, out float along, out float length)
    {
        int index = NavRoute.NextOf(cyclopsId);
        IReadOnlyList<NavWaypoint> points = NavRoute.PointsOf(cyclopsId);
        Vector3 from = index > 0 && index <= points.Count ? points[index - 1].AimPosition : legStart;
        Vector3 to = next.AimPosition;
        Vector3 here = subRoot.transform.position;
        (float t, _, _, _, _) = NavigationRules.NearestOnSegment(here.x, here.y, here.z, from.x, from.y, from.z, to.x, to.y, to.z);
        length = Vector3.Distance(from, to);
        along = t * length;
        return length > 0.01f;
    }

    private bool HoldsLock()
    {
        return cyclopsId != null && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(cyclopsId);
    }

    // ---- what the players aboard see

    private void LateUpdate()
    {
        bool aboard = Aboard;
        bool show = current.Active && aboard && !GunnerConsole.AnyConsoleOpen;
        UpdatePing(show);
        UpdateLine(show);
        if (aboard)
        {
            RouteHud.Feed(current);
        }
    }

    private void UpdatePing(bool show)
    {
        if (!show)
        {
            DestroyPing();
            return;
        }
        (int, int, bool) key = (current.Index, current.Count, current.OffCourse);
        if (pingObject && key == pingKey)
        {
            return;
        }
        DestroyPing();
        try
        {
            GameObject go = new($"Nitrox_CyclopsRoute_{cyclopsId}");
            go.transform.position = current.Target;
            PingInstance ping = go.AddComponent<PingInstance>();
            ping.pingType = PingType.Signal;
            ping.origin = go.transform;
            ping.minDist = 8f;
            ping.range = 5f;
            ping._label = $"Route {current.Index + 1}/{current.Count}";
            ping.displayPingInManager = false;
            ping.visible = true;
            ping.colorIndex = current.OffCourse ? 1 : 0;
            // a new id every time: the old ping is destroyed at the end of the frame, and its removal must not take this one (same id) with it
            ping._id = $"nitrox_cyclopsroute_{cyclopsId}_{++pingGeneration}";
            ping.Initialize();
            pingObject = go;
            pingKey = key;
        }
        catch (System.Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] navigator: could not make the HUD ping of the next waypoint");
        }
    }

    private void DestroyPing()
    {
        if (pingObject)
        {
            Destroy(pingObject);
        }
        pingObject = null;
        pingKey = default;
    }

    private void UpdateLine(bool show)
    {
        if (!show)
        {
            if (lineObject && lineObject.activeSelf)
            {
                lineObject.SetActive(false);
            }
            return;
        }
        Camera camera = MainCamera.camera;
        Material material = RouteLineMaterial.Get();
        if (!camera || !material)
        {
            return;
        }
        EnsureLine(material);
        lineObject.SetActive(true);

        Vector3 forward = Axis.forward;
        Vector3 start = HullBounds.TryGetAnchor(subRoot.transform, forward, true, out Vector3 anchor) ? anchor : subRoot.transform.position;
        IReadOnlyList<NavWaypoint> points = NavRoute.PointsOf(cyclopsId);
        int index = current.Index;
        float budget = MAX_LINE_METRES;
        Vector3 from = start;
        Color core = current.OffCourse ? new Color(1f, 0.55f, 0.1f, 0.95f) : new Color(0.3f, 0.9f, 1f, 0.95f);
        for (int leg = 0; leg < cores.Length; leg++)
        {
            bool has = index + leg < points.Count && budget > 1f;
            cores[leg].enabled = has;
            halos[leg].enabled = has;
            if (!has)
            {
                continue;
            }
            Vector3 to = points[index + leg].AimPosition;
            float length = Vector3.Distance(from, to);
            if (length > budget)
            {
                to = from + (to - from) / length * budget;
                length = budget;
            }
            budget -= length;
            SetLeg(camera, cores[leg], halos[leg], from, to, core);
            from = to;
        }
    }

    private static void SetLeg(Camera camera, LineRenderer coreLine, LineRenderer haloLine, Vector3 from, Vector3 to, Color color)
    {
        float widthStart = NavigationRules.LineWidth(Vector3.Distance(camera.transform.position, from));
        float widthEnd = NavigationRules.LineWidth(Vector3.Distance(camera.transform.position, to));
        coreLine.SetPosition(0, from);
        coreLine.SetPosition(1, to);
        coreLine.startWidth = widthStart;
        coreLine.endWidth = widthEnd;
        coreLine.startColor = color;
        coreLine.endColor = color;
        haloLine.SetPosition(0, from);
        haloLine.SetPosition(1, to);
        haloLine.startWidth = widthStart * 3f;
        haloLine.endWidth = widthEnd * 3f;
        Color halo = color;
        halo.a = 0.25f;
        haloLine.startColor = halo;
        haloLine.endColor = halo;
    }

    private void EnsureLine(Material material)
    {
        if (lineObject)
        {
            return;
        }
        lineObject = new GameObject($"NitroxRouteLine_{cyclopsId}");
        halos = new LineRenderer[2];
        cores = new LineRenderer[2];
        for (int leg = 0; leg < 2; leg++)
        {
            halos[leg] = MakeLine("Halo", material, 0);
            cores[leg] = MakeLine("Core", material, 1);
        }
    }

    private LineRenderer MakeLine(string name, Material material, int order)
    {
        GameObject child = new(name);
        child.transform.SetParent(lineObject.transform, false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        line.sortingOrder = order;
        line.enabled = false;
        return line;
    }
}

/// <summary>
/// The material of the line that guides the pilot along the route: the game's own sprite shader (the tow cable's line has shown that a code-made line with it is drawn on both clients), chosen the same way every
/// time and not taken from a prefab, so that the line does not look different in different worlds. Tried again every half minute until a shader is found.
/// </summary>
public static class RouteLineMaterial
{
    private static readonly string[] SHADERS = ["Sprites/Default", "UI/Default", "Unlit/Color", "Hidden/Internal-Colored"];
    private static Material material;
    private static float nextAttempt;

    public static Material Get()
    {
        if (material)
        {
            return material;
        }
        if (Time.unscaledTime < nextAttempt)
        {
            return null;
        }
        nextAttempt = Time.unscaledTime + 30f;
        foreach (string name in SHADERS)
        {
            Shader shader = Shader.Find(name);
            if (shader)
            {
                material = new Material(shader) { name = "NitroxRouteLine", color = Color.white, hideFlags = HideFlags.HideAndDontSave };
                Log.Info($"[VehicleUpgrades] navigator: the route line is drawn with {name}");
                return material;
            }
        }
        Log.Warn("[VehicleUpgrades] navigator: no shader for the route line was found");
        return null;
    }
}
