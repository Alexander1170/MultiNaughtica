#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NitroxClient.GameLogic;

/// <summary>
/// The bookkeeping of the Debug force probe (<c>HullForceProbe</c>, <c>hullforce</c>, a harness script that is not part of this repository): who adds a force to a Cyclops hull, who writes its velocity or
/// its pose, how much, and which of that explains a change of its speed. Plain numbers, no game types, so that it can be tested; the probe reads the game and asks. Debug builds only,
/// the Release build does not contain it.
/// </summary>
/// <remarks>
/// Bug 11 (VEHICLES.md section 7): the hull of a parked Cyclops creeps by a few millimeters per second and nothing in the code that exists says what pushes it. The probe takes every call
/// of <c>Rigidbody.AddForce</c> and its relatives, every write of the velocity and of the pose, that is made on the hull while a <c>hullrest</c> run is on, names the caller and adds
/// the calls up per caller: the sum of the forces, the sum of the impulses (force times the physics step, so that a force is comparable with the speed it gives a 12000 kg hull), the
/// largest single value. A physics step in which the speed changed by more than <see cref="UNEXPLAINED_VELOCITY_STEP" /> without a force or a write that accounts for it is reported
/// on its own: that is a collision, a depenetration or a pose write.
/// </remarks>
public static class HullForceRules
{
    /// <summary>
    /// A physics step in which the horizontal speed of the hull changed by more than this (m/s), beyond what the forces and the writes of that step and the drag account for, is
    /// reported as unexplained. A drive changes the speed by 0.02 m/s per step; the creep of bug 11 is 0.0035 m/s in total.
    /// </summary>
    public const float UNEXPLAINED_VELOCITY_STEP = 0.002f;

    /// <summary>
    /// A force below this size (in the unit of its mode) is no news: a caller that only did that in a window gets no line.
    /// </summary>
    public const float TRIVIAL_MAGNITUDE = 1e-6f;

    /// <summary>
    /// One line per caller per this many seconds.
    /// </summary>
    public const float WINDOW_SECONDS = 1f;

    /// <summary>
    /// The summary names this many callers.
    /// </summary>
    public const int TOP_COUNT = 5;

    /// <summary>
    /// At most this many "unexplained" lines per window, the rest is counted in the summary only.
    /// </summary>
    public const int MAX_UNEXPLAINED_LINES_PER_WINDOW = 10;

    /// <summary>
    /// A hull is settled when its speed stays below <see cref="SETTLED_SPEED" /> and its turn rate below <see cref="SETTLED_TURN_RATE" /> for <see cref="SETTLED_SECONDS" />.
    /// </summary>
    public const float SETTLED_SPEED = 0.0003f;

    public const float SETTLED_TURN_RATE = 0.0005f;

    public const float SETTLED_SECONDS = 2f;

    // The values of UnityEngine.ForceMode
    public const int MODE_FORCE = 0;
    public const int MODE_IMPULSE = 1;
    public const int MODE_VELOCITY_CHANGE = 2;
    public const int MODE_ACCELERATION = 5;

    /// <summary>
    /// What was done to the hull.
    /// </summary>
    public enum ForceKind
    {
        AddForce,
        AddRelativeForce,
        AddForceAtPosition,
        AddExplosionForce,
        AddTorque,
        AddRelativeTorque,
        MovePosition,
        MoveRotation,
        SetVelocity,
        SetAngularVelocity,
        SetPosition,
        SetRotation,
        SetCenterOfMass,
        TransformWrite,
        Contact
    }

    /// <summary>
    /// A vector of three floats, the only type of the rules.
    /// </summary>
    public readonly struct Vec3
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new(0f, 0f, 0f);

        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public float HorizontalMagnitude => (float)Math.Sqrt(X * X + Z * Z);

        public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Y) && !float.IsInfinity(Y) && !float.IsNaN(Z) && !float.IsInfinity(Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 a, float factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

        /// <summary>
        /// "(x,y,z)" with five decimals, no spaces, the decimal point of every culture: a value of a log line that the scripts read.
        /// </summary>
        public string Format() => FormatVector(X, Y, Z, 5);
    }

    /// <summary>
    /// A number with the decimal point and the decimals asked for, whatever the culture of the game.
    /// </summary>
    public static string Fixed(float value, int decimals)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    public static string FormatVector(float x, float y, float z, int decimals)
    {
        return $"({Fixed(x, decimals)},{Fixed(y, decimals)},{Fixed(z, decimals)})";
    }

    public static string ModeName(int mode)
    {
        switch (mode)
        {
            case MODE_FORCE: return "Force";
            case MODE_IMPULSE: return "Impulse";
            case MODE_VELOCITY_CHANGE: return "VelocityChange";
            case MODE_ACCELERATION: return "Acceleration";
            default: return $"mode{mode}";
        }
    }

    /// <summary>
    /// Whether the call adds a linear force or acceleration (its sum can explain a change of the speed of the center of mass).
    /// </summary>
    public static bool IsLinearForce(ForceKind kind)
    {
        return kind == ForceKind.AddForce || kind == ForceKind.AddRelativeForce || kind == ForceKind.AddForceAtPosition;
    }

    /// <summary>
    /// Whether the call writes a value (a velocity, a pose) instead of adding a force: such a call is news whatever its size.
    /// </summary>
    public static bool IsWrite(ForceKind kind)
    {
        return kind != ForceKind.AddForce && kind != ForceKind.AddRelativeForce && kind != ForceKind.AddForceAtPosition && kind != ForceKind.AddExplosionForce
               && kind != ForceKind.AddTorque && kind != ForceKind.AddRelativeTorque;
    }

    /// <summary>
    /// The linear impulse (N s) that one call of <c>AddForce</c> and its relatives gives a body of <paramref name="mass" /> kilograms in a physics step of <paramref name="step" />
    /// seconds: a force acts for the step, an acceleration is a force per kilogram, an impulse is the impulse, a velocity change is a speed. A mode that is not known gives nothing.
    /// </summary>
    public static Vec3 LinearImpulse(int mode, Vec3 value, float mass, float step)
    {
        switch (mode)
        {
            case MODE_FORCE: return value * step;
            case MODE_IMPULSE: return value;
            case MODE_VELOCITY_CHANGE: return value * mass;
            case MODE_ACCELERATION: return value * (mass * step);
            default: return Vec3.Zero;
        }
    }

    /// <summary>
    /// The change of speed (m/s) that an impulse gives a body of <paramref name="mass" /> kilograms. Nothing for a mass that is not positive.
    /// </summary>
    public static Vec3 DeltaVelocity(Vec3 impulse, float mass)
    {
        return mass > 0f ? impulse * (1f / mass) : Vec3.Zero;
    }

    /// <summary>
    /// How far a physics step changed the horizontal speed of the hull beyond what accounts for it: the speed after the step is the speed before plus what the forces and the writes of
    /// the step gave, less the drag. The caller passes the speeds, the change of speed that the calls of the step explain (<paramref name="explained" />) and the fraction that the drag
    /// takes in one step (drag times step).
    /// </summary>
    /// <returns>The size of the horizontal difference in m/s when it is at least <see cref="UNEXPLAINED_VELOCITY_STEP" />, else 0 (noise, and a broken measurement).</returns>
    public static float UnexplainedVelocityStep(Vec3 previous, Vec3 current, Vec3 explained, float dragFraction)
    {
        if (!previous.IsFinite || !current.IsFinite || !explained.IsFinite)
        {
            return 0f;
        }

        // NaN counts as no drag
        float keep = 1f - (dragFraction >= 0f && dragFraction <= 1f ? dragFraction : 0f);
        float expectedX = (previous.X + explained.X) * keep;
        float expectedZ = (previous.Z + explained.Z) * keep;
        float differenceX = current.X - expectedX;
        float differenceZ = current.Z - expectedZ;
        float size = (float)Math.Sqrt(differenceX * differenceX + differenceZ * differenceZ);
        return size >= UNEXPLAINED_VELOCITY_STEP ? size : 0f;
    }

    /// <summary>
    /// What one caller did to the hull, added up (<see cref="Total" /> since the probe was armed, the window part since the last line).
    /// </summary>
    public sealed class Entry
    {
        internal Entry(string caller, ForceKind kind)
        {
            Caller = caller;
            Kind = kind;
            Mode = string.Empty;
        }

        public string Caller { get; }

        public ForceKind Kind { get; }

        /// <summary>The mode of the calls, "mixed" when the caller used more than one.</summary>
        public string Mode { get; internal set; }

        public int Calls { get; internal set; }

        /// <summary>The sum of the values of the calls (forces in N, accelerations in m/s^2, writes as the change they made).</summary>
        public Vec3 Sum { get; internal set; }

        /// <summary>The sum of the linear impulses (N s).</summary>
        public Vec3 Impulse { get; internal set; }

        /// <summary>The sum of the sizes of the impulses of the single calls: what a caller whose calls cancel each other (a pair of opposite forces) did at all.</summary>
        public float AbsImpulse { get; internal set; }

        public float MaxMagnitude { get; internal set; }

        /// <summary>The sum of the sizes of the writes: meters, or degrees for a rotation.</summary>
        public float Moved { get; internal set; }

        public int WindowCalls { get; internal set; }

        public Vec3 WindowSum { get; internal set; }

        public Vec3 WindowImpulse { get; internal set; }

        public float WindowMax { get; internal set; }

        /// <summary>"name:kind" without spaces: what the summary lists.</summary>
        public string Label => $"{Caller}:{Kind}";
    }

    /// <summary>
    /// The calls on the hull, added up per caller and kind.
    /// </summary>
    public sealed class CallerTally
    {
        private readonly Dictionary<string, Entry> entries = new();
        private readonly List<Entry> order = new();

        /// <summary>The mass of the hull (kg), to turn the impulses of a line into a change of speed.</summary>
        public float Mass { get; set; } = 12000f;

        public int Callers => order.Count;

        public int TotalCalls { get; private set; }

        public IReadOnlyList<Entry> Entries => order;

        /// <param name="caller">Who did it, "Type.Method" (the probe takes it from the stack).</param>
        /// <param name="kind">What was done.</param>
        /// <param name="mode">The mode of the call ("Force", "Acceleration", "Write", "Move", ...).</param>
        /// <param name="value">The value of the call.</param>
        /// <param name="impulse">The linear impulse of the call in N s (zero for a call that adds none).</param>
        /// <param name="moved">The size of a write in meters or degrees (zero for a force).</param>
        public void Add(string caller, ForceKind kind, string mode, Vec3 value, Vec3 impulse, float moved = 0f)
        {
            string name = string.IsNullOrEmpty(caller) ? "unknown" : caller;
            string key = $"{name}|{kind}";
            if (!entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry(name, kind);
                entries.Add(key, entry);
                order.Add(entry);
            }

            // A broken value (NaN, infinity) is counted as a call and as nothing else
            Vec3 usableValue = value.IsFinite ? value : Vec3.Zero;
            Vec3 usableImpulse = impulse.IsFinite ? impulse : Vec3.Zero;
            float size = usableValue.Magnitude;
            float usableMoved = moved >= 0f && !float.IsInfinity(moved) ? moved : 0f;

            entry.Mode = entry.Mode.Length == 0 || entry.Mode == mode ? mode : "mixed";
            entry.Calls++;
            entry.Sum += usableValue;
            entry.Impulse += usableImpulse;
            entry.AbsImpulse += usableImpulse.Magnitude;
            entry.MaxMagnitude = Math.Max(entry.MaxMagnitude, size);
            entry.Moved += usableMoved;
            entry.WindowCalls++;
            entry.WindowSum += usableValue;
            entry.WindowImpulse += usableImpulse;
            entry.WindowMax = Math.Max(entry.WindowMax, size);
            TotalCalls++;
        }

        /// <summary>
        /// The lines of the window that ends now, one per caller that did something that is news: a write, or a force of more than <see cref="TRIVIAL_MAGNITUDE" />. The window part of
        /// every entry is reset. A line is "t=.. caller=.. calls=.. sumForce=(x,y,z) maxMag=.. mode=.. kind=.. impulse=(x,y,z) dvH=..", written after "[SyncHarness] hullforce: ".
        /// </summary>
        /// <param name="seconds">The time since the probe was armed, for the "t=" of the lines.</param>
        public List<string> TakeWindow(float seconds)
        {
            List<string> lines = new();
            foreach (Entry entry in order)
            {
                if (entry.WindowCalls > 0 && (IsWrite(entry.Kind) || entry.WindowMax >= TRIVIAL_MAGNITUDE))
                {
                    float deltaH = DeltaVelocity(entry.WindowImpulse, Mass).HorizontalMagnitude;
                    lines.Add($"t={Fixed(seconds, 1)} caller={entry.Caller} calls={entry.WindowCalls} sumForce={entry.WindowSum.Format()} maxMag={Fixed(entry.WindowMax, 5)} mode={entry.Mode} kind={entry.Kind} impulse={entry.WindowImpulse.Format()} dvH={Fixed(deltaH, 5)}");
                }

                entry.WindowCalls = 0;
                entry.WindowSum = Vec3.Zero;
                entry.WindowImpulse = Vec3.Zero;
                entry.WindowMax = 0f;
            }

            return lines;
        }

        /// <summary>
        /// The callers with the largest summed impulse (the size of the vector sum: opposite forces cancel, which is what a push that creeps needs), the largest first. Ties go to the
        /// one with more calls, then by name, so that the order is the same in every run.
        /// </summary>
        public List<Entry> Top(int count)
        {
            List<Entry> sorted = new(order);
            sorted.Sort(CompareByImpulse);
            if (count >= 0 && sorted.Count > count)
            {
                sorted.RemoveRange(count, sorted.Count - count);
            }

            return sorted;
        }

        private static int CompareByImpulse(Entry a, Entry b)
        {
            int byImpulse = b.Impulse.Magnitude.CompareTo(a.Impulse.Magnitude);
            if (byImpulse != 0)
            {
                return byImpulse;
            }

            int byCalls = b.Calls.CompareTo(a.Calls);
            return byCalls != 0 ? byCalls : string.CompareOrdinal(a.Label, b.Label);
        }

        /// <summary>
        /// "top rank=1 caller=.. kind=.. calls=.. impulse=(x,y,z) mag=.. absImpulse=.. dvH=.. moved=.. sumForce=(x,y,z) maxMag=.. mode=..": one line of the summary per caller of <see cref="Top" />.
        /// </summary>
        public string FormatTop(int rank, Entry entry)
        {
            float deltaH = DeltaVelocity(entry.Impulse, Mass).HorizontalMagnitude;
            return $"top rank={rank} caller={entry.Caller} kind={entry.Kind} calls={entry.Calls} impulse={entry.Impulse.Format()} mag={Fixed(entry.Impulse.Magnitude, 4)} absImpulse={Fixed(entry.AbsImpulse, 4)} dvH={Fixed(deltaH, 5)} moved={Fixed(entry.Moved, 4)} sumForce={entry.Sum.Format()} maxMag={Fixed(entry.MaxMagnitude, 5)} mode={entry.Mode}";
        }

        /// <summary>
        /// The short list of the summary line: "caller:kind:impulseMagnitude" for each of <see cref="Top" />, joined with ";" (no spaces).
        /// </summary>
        public string FormatTopList(int count)
        {
            List<string> parts = new();
            foreach (Entry entry in Top(count))
            {
                parts.Add($"{entry.Label}:{Fixed(entry.Impulse.Magnitude, 4)}");
            }

            return parts.Count == 0 ? "none" : string.Join(";", parts);
        }
    }

    /// <summary>
    /// Learns the height at which a hull rests: the height of the last two seconds in which it did not move. A hull that is above it is still settling.
    /// </summary>
    public sealed class RestDepthTracker
    {
        private float stillSince = float.NaN;

        public bool HasRest { get; private set; }

        public float RestY { get; private set; }

        /// <param name="y">The height of the hull (m).</param>
        /// <param name="speed">The size of its velocity (m/s).</param>
        /// <param name="turnRate">The size of its angular velocity (rad/s).</param>
        /// <param name="time">The clock of the caller (s).</param>
        public void Note(float y, float speed, float turnRate, float time)
        {
            // Negated so that NaN is never still
            bool still = speed <= SETTLED_SPEED && turnRate <= SETTLED_TURN_RATE && speed >= 0f && turnRate >= 0f && !float.IsNaN(y) && !float.IsInfinity(y);
            if (!still)
            {
                stillSince = float.NaN;
                return;
            }

            if (float.IsNaN(stillSince))
            {
                stillSince = time;
            }

            if (time - stillSince >= SETTLED_SECONDS)
            {
                HasRest = true;
                RestY = y;
            }
        }

        /// <returns>How far (m) the hull is above (positive) or below the resting height, NaN while none is known.</returns>
        public float Offset(float y)
        {
            return HasRest ? y - RestY : float.NaN;
        }
    }
}
#endif
