#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NitroxClient.GameLogic;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;
using static NitroxClient.GameLogic.HullForceRules;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// The Debug force probe of bug 11 (VEHICLES.md section 7, a harness script that is not part of this repository, section "The force probe"). While it is armed it takes every call that changes the
/// motion of the Rigidbody of ONE hull (the nearest Cyclops): <c>AddForce</c>, <c>AddRelativeForce</c>, <c>AddForceAtPosition</c>, <c>AddTorque</c>, <c>AddRelativeTorque</c>,
/// <c>MovePosition</c>, <c>MoveRotation</c>, the setters of <c>velocity</c>, <c>angularVelocity</c>, <c>position</c>, <c>rotation</c> and <c>centerOfMass</c> (the Harmony prefixes
/// of NitroxPatcher/Patches/Dynamic/HullForceProbe_Patches.cs call <see cref="NoteForce" /> and <see cref="NoteWrite" />), the contacts of the hull (<see cref="NoteContact" />) and the
/// writes of the Nitrox movement replicators to the transform of the hull (<see cref="NoteReplicatorWrite" />), and adds them up per caller (<see cref="HullForceRules.CallerTally" />).
/// <para>
/// Cost. While it is not armed the only cost is the static bool <see cref="Armed" /> that every prefix reads first. While it is armed a call that is not on the hull costs one more
/// compare (the instance id), a call on the hull costs two shallow stack frames (<c>new StackFrame(n, false)</c>, no file information, the names cached per method) and a dictionary
/// lookup: a hull gets about 15 calls per physics step, 750 per second at 50 Hz. A full <c>StackTrace</c> per call would cost ten times as much, and "the first call of each call site
/// per second" needs the stack to tell the call site, so the shallow walk is the cheaper way.
/// </para>
/// <para>
/// Lines (all <c>[SyncHarness] hullforce: ...</c>): <c>begin</c> and <c>patches</c> when armed; one line per caller per second (<c>t=.. caller=.. calls=.. sumForce=(x,y,z) maxMag=..
/// mode=.. kind=..</c>); <c>unexplained</c> for a physics step in which the horizontal speed changed by more than 0.002 m/s without a call or a drag that explains it; at the end
/// <c>coverage</c>, one <c>top</c> line per caller of the five with the largest summed impulse, and <c>summary</c>.
/// </para>
/// </summary>
public sealed class HullForceProbe : MonoBehaviour
{
    /// <summary>
    /// The probe is armed on a hull. The Harmony prefixes read this first and do nothing else while it is false.
    /// </summary>
    public static bool Armed;

    /// <summary>
    /// The longest a probe stays armed whatever the command says (seconds): a forgotten "hullforce on" does not flood the log of a whole session.
    /// </summary>
    public const float MAX_ARMED_SECONDS = 900f;

    private const int MAX_LINES_PER_WINDOW = 40;
    private const int MAX_FRAMES = 12;

    private static HullForceProbe instance;
    private static readonly Dictionary<string, string> patchStatus = new();
    private static readonly Dictionary<MethodBase, string> frameNames = new();

    private readonly CallerTally tally = new();
    private Rigidbody body;
    private Transform hull;
    private HullContactRelay contactRelay;
    private int bodyId;
    private string hullId = "noid";
    private string owner = string.Empty;
    private float armedAt;
    private float windowStart;

    // Per physics step
    private readonly WaitForFixedUpdate waitForFixedUpdate = new();
    private bool hasPrevious;
    private Vec3 previousVelocity;
    private float previousStep;
    private Vec3 explained;
    private float stepForceCalls;
    private float stepContactImpulse;
    private int stepContacts;

    // Totals
    private int steps;
    private int sameSteps;
    private long forceCalls;
    private long writeCalls;
    private int unexplainedSteps;
    private float unexplainedMax;
    private int unexplainedLinesInWindow;
    private int unexplainedLinesSuppressed;
    private long replicatorWrites;
    private float replicatorMeters;

    public static bool IsArmed => Armed && instance;

    /// <summary>
    /// Takes the status of one Harmony patch of the probe (written by NitroxPatcher when it applies them): the line "patches" of <c>begin</c> says which of them are live.
    /// </summary>
    public static void NotePatch(string api, bool applied, string reason)
    {
        patchStatus[api] = applied ? "ok" : $"failed:{reason}";
    }

    /// <summary>
    /// "ok", "failed:Reason" or "unknown" for one patch of the probe.
    /// </summary>
    public static string PatchStatusOf(string api)
    {
        return patchStatus.TryGetValue(api, out string status) ? status : "unknown";
    }

    public static int PatchesApplied()
    {
        int count = 0;
        foreach (string status in patchStatus.Values)
        {
            if (status == "ok")
            {
                count++;
            }
        }

        return count;
    }

    private static string PatchSummary()
    {
        if (patchStatus.Count == 0)
        {
            return "none_registered";
        }

        List<string> failed = new();
        foreach (KeyValuePair<string, string> pair in patchStatus)
        {
            if (pair.Value != "ok")
            {
                failed.Add($"{pair.Key}={pair.Value}".Replace(' ', '_'));
            }
        }

        return $"{PatchesApplied()}/{patchStatus.Count} failed={(failed.Count == 0 ? "none" : string.Join(",", failed))}";
    }

    /// <summary>
    /// Arms the probe on the Rigidbody of a hull. Arming again replaces the earlier arming (its summary is written first).
    /// </summary>
    /// <param name="cyclops">The hull.</param>
    /// <param name="rigidbody">Its Rigidbody.</param>
    /// <param name="who">Who arms it ("hullrest", "manual"), for the lines.</param>
    public static void Arm(NitroxCyclops cyclops, Rigidbody rigidbody, string who)
    {
        if (!cyclops || !rigidbody)
        {
            return;
        }

        if (instance)
        {
            instance.Finish("rearmed");
        }

        GameObject host = new("NitroxHullForceProbe");
        HullForceProbe probe = host.AddComponent<HullForceProbe>();
        probe.Begin(cyclops, rigidbody, who);
    }

    /// <summary>
    /// Disarms the probe and writes its summary. Nothing happens when it is not armed.
    /// </summary>
    public static void Disarm(string reason)
    {
        if (instance)
        {
            instance.Finish(reason);
        }
    }

    /// <summary>
    /// Writes the totals so far without disarming ("hullforce dump").
    /// </summary>
    public static void Dump()
    {
        if (instance)
        {
            instance.WriteSummary("dump");
        }
        else
        {
            Log.Info("[SyncHarness] hullforce: dump not_armed");
        }
    }

    public static string Status()
    {
        return instance ? $"armed hull={instance.hullId} owner={instance.owner} seconds={Time.unscaledTime - instance.armedAt:F1} callers={instance.tally.Callers} patches={PatchSummary()}" : $"not_armed patches={PatchSummary()}";
    }

    private void Begin(NitroxCyclops cyclops, Rigidbody rigidbody, string who)
    {
        instance = this;
        body = rigidbody;
        hull = cyclops.transform;
        bodyId = rigidbody.GetInstanceID();
        owner = who;
        hullId = cyclops.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id) ? id.ToString() : "noid";
        tally.Mass = rigidbody.mass;
        armedAt = Time.unscaledTime;
        windowStart = armedAt;
        Armed = true;
        // The contacts of the hull are the probe's own business: a relay on the hull passes them on, so they are counted when "hullforce on" or the drive of hullrest.ps1 armed the probe too
        // and not only while a "hullrest" run is going
        contactRelay = body.gameObject.AddComponent<HullContactRelay>();
        contactRelay.Probe = this;
        Log.Info($"[SyncHarness] hullforce: begin hull={hullId} owner={who} mass={rigidbody.mass:F0} drag={rigidbody.drag:F3} angularDrag={rigidbody.angularDrag:F3} step={Time.fixedDeltaTime:F3} patches={PatchSummary()} pose={PoseText()}");
        StartCoroutine(StepLoop());
    }

    private void Finish(string reason)
    {
        if (!Armed || instance != this)
        {
            return;
        }

        Armed = false;
        WriteSummary(reason);
        instance = null;
        RemoveContactRelay();
        Destroy(gameObject);
    }

    public void OnDestroy()
    {
        RemoveContactRelay();
        if (instance == this)
        {
            Armed = false;
            instance = null;
        }
    }

    private void RemoveContactRelay()
    {
        if (contactRelay)
        {
            Destroy(contactRelay);
        }

        contactRelay = null;
    }

    public void Update()
    {
        if (!Armed || instance != this)
        {
            return;
        }

        if (!body || !hull)
        {
            Finish("hull_gone");
            return;
        }

        if (Time.unscaledTime - armedAt > MAX_ARMED_SECONDS)
        {
            Finish("time_limit");
            return;
        }

        if (Time.unscaledTime - windowStart >= WINDOW_SECONDS)
        {
            FlushWindow();
        }
    }

    private IEnumerator StepLoop()
    {
        while (Armed && instance == this)
        {
            // Resumes after the physics step: every FixedUpdate of the step has made its calls, the contacts of the step are in
            yield return waitForFixedUpdate;
            if (!Armed || instance != this || !body)
            {
                yield break;
            }

            StepEnd();
        }
    }

    private void StepEnd()
    {
        float step = Time.fixedDeltaTime;
        Vector3 velocity = body.velocity;
        Vec3 current = new(velocity.x, velocity.y, velocity.z);
        steps++;
        if (hasPrevious && HullParkingRules.AreSameStep(previousStep, step))
        {
            sameSteps++;
            float unexplained = UnexplainedVelocityStep(previousVelocity, current, explained, body.drag * step);
            if (unexplained > 0f)
            {
                unexplainedSteps++;
                unexplainedMax = Mathf.Max(unexplainedMax, unexplained);
                if (unexplainedLinesInWindow < MAX_UNEXPLAINED_LINES_PER_WINDOW)
                {
                    unexplainedLinesInWindow++;
                    float dx = current.X - previousVelocity.X;
                    float dz = current.Z - previousVelocity.Z;
                    Log.Info($"[SyncHarness] hullforce: unexplained t={Time.unscaledTime - armedAt:F2} mag={Fixed(unexplained, 5)} dvH=({Fixed(dx, 5)},{Fixed(dz, 5)}) explainedH=({Fixed(explained.X, 5)},{Fixed(explained.Z, 5)}) vel={current.Format()} step={Fixed(step, 3)} forces={(int)stepForceCalls} contacts={stepContacts} contactImpulse={Fixed(stepContactImpulse, 4)}");
                }
                else
                {
                    unexplainedLinesSuppressed++;
                }
            }
        }

        previousVelocity = current;
        previousStep = step;
        hasPrevious = true;
        explained = Vec3.Zero;
        stepForceCalls = 0f;
        stepContacts = 0;
        stepContactImpulse = 0f;
    }

    private void FlushWindow()
    {
        float now = Time.unscaledTime;
        List<string> lines = tally.TakeWindow(now - armedAt);
        int written = 0;
        foreach (string line in lines)
        {
            if (written++ >= MAX_LINES_PER_WINDOW)
            {
                break;
            }

            Log.Info($"[SyncHarness] hullforce: {line}");
        }

        if (unexplainedLinesSuppressed > 0)
        {
            Log.Info($"[SyncHarness] hullforce: unexplained t={now - armedAt:F2} suppressed={unexplainedLinesSuppressed}");
        }

        windowStart = now;
        unexplainedLinesInWindow = 0;
        unexplainedLinesSuppressed = 0;
    }

    private void WriteSummary(string reason)
    {
        FlushWindow();
        float seconds = Time.unscaledTime - armedAt;
        // The hull is simulated here and gets a force from WorldForces in every physics step: no call at all in 50 steps means the patches do not reach the callers
        string coverage = steps < 50 ? "short" : forceCalls > 0 || writeCalls > 0 ? "ok" : "none";
        Log.Info($"[SyncHarness] hullforce: coverage steps={steps} sameStepPairs={sameSteps} forceCalls={forceCalls} writeCalls={writeCalls} verdict={coverage} patches={PatchSummary()}");
        int rank = 0;
        foreach (Entry entry in tally.Top(TOP_COUNT))
        {
            Log.Info($"[SyncHarness] hullforce: {tally.FormatTop(++rank, entry)}");
        }

        Log.Info($"[SyncHarness] hullforce: summary reason={reason} hull={hullId} seconds={seconds:F1} steps={steps} callers={tally.Callers} forceCalls={forceCalls} writeCalls={writeCalls} unexplainedSteps={unexplainedSteps} unexplainedMax={Fixed(unexplainedMax, 5)} replicatorWrites={replicatorWrites} replicatorMeters={Fixed(replicatorMeters, 4)} mass={tally.Mass:F0} top={tally.FormatTopList(TOP_COUNT)} endPose={PoseText()}");
    }

    /// <summary>
    /// Where the hull is and how it lies: "(x,y,z) pitch=.. roll=..", the pitch positive when the +z axis of the hull points up. The begin line and the summary carry it, so that a
    /// session armed by hand around a drive says what the drive did to the hull (its height, its pitch) without a hullrest run.
    /// </summary>
    private string PoseText()
    {
        if (!hull)
        {
            return "(0.000,0.000,0.000) pitch=0.000 roll=0.000";
        }

        Vector3 position = hull.position;
        float pitch = Mathf.Asin(Mathf.Clamp(hull.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float roll = Mathf.Asin(Mathf.Clamp(hull.right.y, -1f, 1f)) * Mathf.Rad2Deg;
        return $"{FormatVector(position.x, position.y, position.z, 3)} pitch={Fixed(pitch, 3)} roll={Fixed(roll, 3)}";
    }

    // ---- the calls of the Harmony prefixes, the contacts and the replicators

    private bool IsHullBody(Rigidbody candidate)
    {
        return (object)candidate != null && candidate.GetInstanceID() == bodyId;
    }

    /// <summary>
    /// A call of <c>AddForce</c>, <c>AddRelativeForce</c>, <c>AddForceAtPosition</c>, <c>AddTorque</c>, <c>AddRelativeTorque</c> or <c>AddExplosionForce</c> (the prefixes check
    /// <see cref="Armed" /> first). Only the hull counts.
    /// </summary>
    public static void NoteForce(Rigidbody target, ForceKind kind, int mode, Vector3 value)
    {
        HullForceProbe probe = instance;
        if (probe == null || !Armed || !probe.IsHullBody(target))
        {
            return;
        }

        probe.RecordForce(kind, mode, value);
    }

    /// <summary>
    /// A write of the velocity, the angular velocity, the position, the rotation or the center of mass, or a call of <c>MovePosition</c> or <c>MoveRotation</c>. For a rotation
    /// pass the angle between the old and the new one in degrees as <paramref name="value" />.x.
    /// </summary>
    public static void NoteWrite(Rigidbody target, ForceKind kind, Vector3 written)
    {
        HullForceProbe probe = instance;
        if (probe == null || !Armed || !probe.IsHullBody(target))
        {
            return;
        }

        probe.RecordWrite(kind, written);
    }

    public static void NoteWriteRotation(Rigidbody target, ForceKind kind, Quaternion written)
    {
        HullForceProbe probe = instance;
        if (probe == null || !Armed || !probe.IsHullBody(target))
        {
            return;
        }

        probe.RecordRotation(kind, written);
    }

    private void RecordForce(ForceKind kind, int mode, Vector3 raw)
    {
        Vector3 world = kind == ForceKind.AddRelativeForce || kind == ForceKind.AddRelativeTorque ? body.rotation * raw : raw;
        Vec3 value = new(world.x, world.y, world.z);
        float step = Time.fixedDeltaTime;
        Vec3 impulse = IsLinearForce(kind) ? LinearImpulse(mode, value, tally.Mass, step) : Vec3.Zero;
        forceCalls++;
        stepForceCalls++;
        if (IsLinearForce(kind))
        {
            explained += DeltaVelocity(impulse, tally.Mass);
        }

        tally.Add(DescribeCaller(), kind, ModeName(mode), value, impulse);
    }

    private void RecordWrite(ForceKind kind, Vector3 written)
    {
        Vector3 current;
        string mode = "Write";
        switch (kind)
        {
            case ForceKind.SetVelocity:
                current = body.velocity;
                break;
            case ForceKind.SetAngularVelocity:
                current = body.angularVelocity;
                break;
            case ForceKind.SetCenterOfMass:
                current = body.centerOfMass;
                break;
            default:
                // MovePosition and SetPosition
                current = body.position;
                mode = "Move";
                break;
        }

        Vector3 delta = written - current;
        Vec3 change = new(delta.x, delta.y, delta.z);
        Vec3 impulse = Vec3.Zero;
        if (kind == ForceKind.SetVelocity)
        {
            // The caller changed the speed by delta: that is an impulse of delta times the mass, and it explains the change of speed of the step
            impulse = change * tally.Mass;
            explained += change;
        }

        writeCalls++;
        tally.Add(DescribeCaller(), kind, mode, change, impulse, delta.magnitude);
    }

    private void RecordRotation(ForceKind kind, Quaternion written)
    {
        float degrees = Quaternion.Angle(body.rotation, written);
        writeCalls++;
        tally.Add(DescribeCaller(), kind, "Degrees", new Vec3(degrees, 0f, 0f), Vec3.Zero, degrees);
    }

    /// <summary>
    /// A contact of the hull with another collider during the step (called from the collision relay that the probe puts on the hull). The impulse is what the solver applied to the
    /// pair, its direction is as Unity reports it.
    /// </summary>
    public static void NoteContact(Collision collision)
    {
        HullForceProbe probe = instance;
        if (probe == null || !Armed || collision == null)
        {
            return;
        }

        Vector3 impulseVector = collision.impulse;
        Vec3 impulse = new(impulseVector.x, impulseVector.y, impulseVector.z);
        probe.stepContacts++;
        probe.stepContactImpulse += impulse.Magnitude;
        string name = collision.collider ? collision.collider.name.Replace(' ', '_') : "unknown";
        string bodyKind = collision.rigidbody ? "dynamic" : "static";
        probe.tally.Add($"Contact.{name}@{bodyKind}", ForceKind.Contact, "Impulse", impulse, impulse);
    }

    /// <summary>
    /// A write that a Nitrox movement replicator made to the transform of a hull (the counters of the replicators, Debug builds). Only the hull counts.
    /// </summary>
    public static void NoteReplicatorWrite(Transform target, string site, Vector3 positionBefore, Vector3 positionAfter, float degrees)
    {
        HullForceProbe probe = instance;
        if (probe == null || !Armed || probe.hull != target)
        {
            return;
        }

        Vector3 delta = positionAfter - positionBefore;
        probe.replicatorWrites++;
        probe.replicatorMeters += delta.magnitude;
        probe.tally.Add(site, ForceKind.TransformWrite, "Move", new Vec3(delta.x, delta.y, delta.z), Vec3.Zero, delta.magnitude);
        if (degrees > 0f)
        {
            probe.tally.Add($"{site}.rotation", ForceKind.TransformWrite, "Degrees", new Vec3(degrees, 0f, 0f), Vec3.Zero, degrees);
        }
    }

    // ---- who called

    private static bool IsInfrastructure(MethodBase method)
    {
        Type type = method.DeclaringType;
        if (type == null)
        {
            return true;
        }

        if (type == typeof(Rigidbody) || type.Name.StartsWith("HullForceProbe", StringComparison.Ordinal))
        {
            return true;
        }

        string space = type.Namespace ?? string.Empty;
        return space.StartsWith("HarmonyLib", StringComparison.Ordinal) || space.StartsWith("MonoMod", StringComparison.Ordinal) || method.Name.StartsWith("DMD<", StringComparison.Ordinal);
    }

    private static string NameOf(MethodBase method)
    {
        if (!frameNames.TryGetValue(method, out string name))
        {
            name = $"{method.DeclaringType?.Name}.{method.Name}".Replace(' ', '_');
            frameNames[method] = name;
        }

        return name;
    }

    /// <summary>
    /// "Type.Method" of the method that called the patched Rigidbody method, with the one above it ("Type.Method&lt;-Type.Method") when that one is managed code too: the first frames that
    /// are not the probe, its patches, Harmony or Unity's own wrappers.
    /// </summary>
    private static string DescribeCaller()
    {
        string first = null;
        string second = null;
        try
        {
            for (int i = 1; i < MAX_FRAMES; i++)
            {
                MethodBase method = new StackFrame(i, false).GetMethod();
                if (method == null)
                {
                    break;
                }

                if (IsInfrastructure(method))
                {
                    continue;
                }

                string name = NameOf(method);
                if (first == null)
                {
                    first = name;
                }
                else
                {
                    second = name;
                    break;
                }
            }
        }
        catch (Exception)
        {
            return "unknown";
        }

        if (first == null)
        {
            return "unknown";
        }

        return second == null ? first : $"{first}<-{second}";
    }

    // ---- the self test: are the Harmony patches live?

    /// <summary>
    /// Calls every patched API once on a Rigidbody of its own and says which calls the probe saw ("hullforce selftest"). That the patches are applied does not mean that the game's
    /// own callers use them: a caller that was compiled before the patches may have the small wrapper inlined, that is what the line "coverage" of a real run says.
    /// </summary>
    public static void RunSelfTest()
    {
        if (instance)
        {
            Log.Info("[SyncHarness] hullforce: selftest refused, the probe is armed on a hull (hullforce off first)");
            return;
        }

        GameObject dummy = new("NitroxHullForceSelfTest");
        Rigidbody rigidbody = dummy.AddComponent<Rigidbody>();
        rigidbody.useGravity = false;
        rigidbody.detectCollisions = false;
        rigidbody.mass = 1f;
        GameObject host = new("NitroxHullForceProbe");
        HullForceProbe probe = host.AddComponent<HullForceProbe>();
        instance = probe;
        probe.body = rigidbody;
        probe.hull = dummy.transform;
        probe.bodyId = rigidbody.GetInstanceID();
        probe.tally.Mass = 1f;
        Armed = true;
        int seen = 0;
        int total = 0;
        try
        {
            seen += Check("AddForce", () => rigidbody.AddForce(Vector3.up, ForceMode.Acceleration), probe, ref total);
            seen += Check("AddForce_xyz", () => rigidbody.AddForce(0f, 1f, 0f), probe, ref total);
            seen += Check("AddRelativeForce", () => rigidbody.AddRelativeForce(Vector3.up), probe, ref total);
            seen += Check("AddForceAtPosition", () => rigidbody.AddForceAtPosition(Vector3.up, Vector3.zero), probe, ref total);
            seen += Check("AddTorque", () => rigidbody.AddTorque(Vector3.up), probe, ref total);
            seen += Check("AddRelativeTorque", () => rigidbody.AddRelativeTorque(Vector3.up), probe, ref total);
            seen += Check("MovePosition", () => rigidbody.MovePosition(Vector3.up), probe, ref total);
            seen += Check("MoveRotation", () => rigidbody.MoveRotation(Quaternion.Euler(0f, 1f, 0f)), probe, ref total);
            seen += Check("set_velocity", () => rigidbody.velocity = Vector3.up, probe, ref total);
            seen += Check("set_angularVelocity", () => rigidbody.angularVelocity = Vector3.up, probe, ref total);
            seen += Check("set_position", () => rigidbody.position = Vector3.up, probe, ref total);
            seen += Check("set_rotation", () => rigidbody.rotation = Quaternion.Euler(0f, 2f, 0f), probe, ref total);
            seen += Check("set_centerOfMass", () => rigidbody.centerOfMass = Vector3.up, probe, ref total);
        }
        finally
        {
            Armed = false;
            instance = null;
            Log.Info($"[SyncHarness] hullforce: selftest result hooked={seen} of={total} patches={PatchSummary()}");
            Destroy(host);
            Destroy(dummy);
        }
    }

    /// <summary>
    /// Passes the collision callbacks of the hull to the probe (Unity sends them to the components of the GameObject that has the Rigidbody). The probe adds it to the hull when it is
    /// armed and removes it when it is disarmed.
    /// </summary>
    private sealed class HullContactRelay : MonoBehaviour
    {
        public HullForceProbe Probe;

        public void OnCollisionEnter(Collision collision)
        {
            Pass(collision);
        }

        public void OnCollisionStay(Collision collision)
        {
            Pass(collision);
        }

        private void Pass(Collision collision)
        {
            // The relay of an earlier arming that is not destroyed yet (Destroy waits for the end of the frame) must not count for the next one
            if (Probe && Probe == instance)
            {
                NoteContact(collision);
            }
        }
    }

    private static int Check(string api, Action call, HullForceProbe probe, ref int total)
    {
        total++;
        long before = probe.forceCalls + probe.writeCalls;
        try
        {
            call();
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] hullforce: selftest api={api} hooked=False threw={exception.GetType().Name}");
            return 0;
        }

        bool hooked = probe.forceCalls + probe.writeCalls > before;
        Log.Info($"[SyncHarness] hullforce: selftest api={api} hooked={hooked}");
        return hooked ? 1 : 0;
    }
}
#endif
