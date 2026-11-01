#if DEBUG
using System;
using System.Collections;
using System.Globalization;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "wiggle" (a harness script that is not part of this repository, bug 7): the passenger of a Cyclops that somebody else drives sees a slight wiggle or fuzziness while the hull moves. Debug builds only.
// Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> wiggle [seconds]" on the PASSENGER's client.
//
//   wiggle [seconds] [window]   for that many seconds (10, 2 to 120) samples, at the end of every rendered frame, the hull the local player is aboard of and the camera, and logs ONE line "[SyncHarness] wiggle: ...":
//                      hullDev*  = how far the per-frame velocity of the hull is from its own 0.25 s smoothed velocity (m/s, the measure of the HullJudderMeter of VEHICLES.md V3d, rms and max): the noise of the
//                                  hull's pose, which the passenger's camera, a child of the hull, carries into the view of the world outside
//                      hullTurn* = the same for the turn of the hull (degrees per second): what a passenger sees as the world swaying
//                      cam*      = how far the camera moves against the hull from one frame to the next (mm, and degrees): near zero when the passenger stands still with the mouse untouched, so the camera is rigid in the
//                                  hull and the wiggle, if there is one, is the hull's; above zero with nobody moving is the walking pawn or the camera, not the hull
//                      the rest: whether the hull is a copy (a MovementReplicator draws it), what that replicator did (extrapolating speed, holding, at rest), the physics step, the interpolation of the hull's
//                      rigidbody, autoSyncTransforms, the mode of the local player and the frame rate with its spread
// With a window (2 to 60 s) one line is logged per window instead of one for the whole run ("window=<n>" in the line): a script starts the probe BEFORE the drive and lines up the windows with its own events by
// the log time, because nothing can be typed into the driver's window while a key is held.
// The passenger stands still and does not touch the mouse while it runs (the camera numbers say it when he did not). Nothing throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private bool wiggleRunning;

    private static string WiggleNumber(float value, string format = "F2")
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The deviation of a quantity from its own 0.25 s smoothed value, the way <c>HullJudderMeter</c> measures the hull.
    /// </summary>
    private sealed class WiggleStat
    {
        private Vector3 smooth;
        private bool hasSmooth;
        private double sumSquares;
        private double sumMagnitude;
        private float max;
        private int count;

        public void Add(Vector3 value, float deltaTime)
        {
            if (!hasSmooth)
            {
                smooth = value;
                hasSmooth = true;
                return;
            }

            smooth += (value - smooth) * (1f - Mathf.Exp(-deltaTime / 0.25f));
            float deviation = (value - smooth).magnitude;
            sumSquares += deviation * deviation;
            sumMagnitude += smooth.magnitude;
            max = Mathf.Max(max, deviation);
            count++;
        }

        public float Mean => count > 0 ? (float)(sumMagnitude / count) : 0f;
        public float Rms => count > 0 ? Mathf.Sqrt((float)(sumSquares / count)) : 0f;
        public float Max => max;
    }

    /// <summary>
    /// What the sampling loop of <c>wiggle</c> adds up.
    /// </summary>
    private sealed class WiggleTotals
    {
        public readonly WiggleStat Velocity = new();
        public readonly WiggleStat Turn = new();
        public double CameraStepSquares;
        public float CameraStepMax;
        public double CameraRotationSquares;
        public float CameraRotationMax;
        public int CameraSteps;
        public double FrameSum;
        public double FrameSquares;
        public int Frames;
    }

    private void LiveCommand_wiggle(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 10f), 2f, 120f);
        float window = LiveArgFloat(n, 1, 0f);
        window = window <= 0f ? 0f : Mathf.Clamp(window, 2f, 60f);
        if (wiggleRunning)
        {
            Log.Info("[SyncHarness] wiggle: failed reason=the previous run is still going");
            return;
        }

        StartCoroutine(WiggleRoutine(seconds, window));
    }

    private IEnumerator WiggleRoutine(float seconds, float window)
    {
        Player player = Player.main;
        SubRoot sub = player ? player.currentSub : null;
        if (!sub || !sub.isCyclops)
        {
            Log.Info("[SyncHarness] wiggle: failed reason=the local player is not aboard a Cyclops");
            yield break;
        }

        wiggleRunning = true;
        Transform hull = sub.transform;
        Rigidbody body = hull.GetComponent<Rigidbody>();
        MovementReplicator replicator = hull.GetComponent<MovementReplicator>();
        WiggleTotals totals = new();
        bool hasLast = false;
        Vector3 lastPosition = Vector3.zero;
        Quaternion lastRotation = Quaternion.identity;
        Vector3 lastCameraPosition = Vector3.zero;
        Quaternion lastCameraRotation = Quaternion.identity;
        bool hasLastCamera = false;
        float start = Time.unscaledTime;
        float end = start + seconds;
        float windowStart = start;
        float windowEnd = window > 0f ? start + window : float.PositiveInfinity;
        int windowNumber = 0;
        try
        {
            while (Time.unscaledTime < end && hull)
            {
                yield return new WaitForEndOfFrame();
                if (!hull)
                {
                    break;
                }

                float deltaTime = Time.unscaledDeltaTime;
                Vector3 position = hull.position;
                Quaternion rotation = hull.rotation;
                Camera camera = Camera.main;
                Vector3 cameraPosition = camera ? hull.InverseTransformPoint(camera.transform.position) : Vector3.zero;
                Quaternion cameraRotation = camera ? Quaternion.Inverse(rotation) * camera.transform.rotation : Quaternion.identity;
                if (hasLast && deltaTime > 0f)
                {
                    totals.Velocity.Add((position - lastPosition) / deltaTime, deltaTime);
                    Quaternion turnStep = rotation * Quaternion.Inverse(lastRotation);
                    turnStep.ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f)
                    {
                        angle -= 360f;
                    }
                    totals.Turn.Add(axis * (angle / deltaTime), deltaTime);

                    if (camera && hasLastCamera)
                    {
                        float step = (cameraPosition - lastCameraPosition).magnitude;
                        float rotationStep = Quaternion.Angle(cameraRotation, lastCameraRotation);
                        totals.CameraStepSquares += step * step;
                        totals.CameraStepMax = Mathf.Max(totals.CameraStepMax, step);
                        totals.CameraRotationSquares += rotationStep * rotationStep;
                        totals.CameraRotationMax = Mathf.Max(totals.CameraRotationMax, rotationStep);
                        totals.CameraSteps++;
                    }

                    totals.FrameSum += deltaTime;
                    totals.FrameSquares += deltaTime * deltaTime;
                    totals.Frames++;
                }

                hasLast = true;
                lastPosition = position;
                lastRotation = rotation;
                hasLastCamera = camera;
                lastCameraPosition = cameraPosition;
                lastCameraRotation = cameraRotation;

                if (Time.unscaledTime >= windowEnd && Time.unscaledTime < end)
                {
                    windowNumber++;
                    WiggleLog(totals, Time.unscaledTime - windowStart, replicator, body, windowNumber, hull);
                    totals = new WiggleTotals();
                    windowStart = Time.unscaledTime;
                    windowEnd = windowStart + window;
                }
            }
        }
        finally
        {
            wiggleRunning = false;
        }

        // the last (or the only) window: nothing is logged for a rest of less than a second after the last full window
        float lastSeconds = Time.unscaledTime - windowStart;
        if (window <= 0f || lastSeconds >= 1f)
        {
            WiggleLog(totals, window <= 0f ? seconds : lastSeconds, replicator, body, window > 0f ? windowNumber + 1 : 0, hull);
        }
    }

    private void WiggleLog(WiggleTotals totals, float seconds, MovementReplicator replicator, Rigidbody body, int windowNumber, Transform sampledHull)
    {
        try
        {
            int frames = totals.Frames;
            float meanFrame = frames > 0 ? (float)(totals.FrameSum / frames) : 0f;
            float frameSpread = frames > 0 ? Mathf.Sqrt(Mathf.Max(0f, (float)(totals.FrameSquares / frames) - meanFrame * meanFrame)) : 0f;
            float camStepRmsMm = totals.CameraSteps > 0 ? Mathf.Sqrt((float)(totals.CameraStepSquares / totals.CameraSteps)) * 1000f : 0f;
            float camRotRms = totals.CameraSteps > 0 ? Mathf.Sqrt((float)(totals.CameraRotationSquares / totals.CameraSteps)) : 0f;
            string replicaText = replicator
                ? $"replica=True extrapolationSpeed={WiggleNumber(replicator.DebugExtrapolationSpeed)} holding={replicator.DebugHolding} atRest={replicator.DebugAtRest}"
                : "replica=False";
            string bodyText = body ? $"interpolation={body.interpolation} kinematic={body.isKinematic}" : "interpolation=none kinematic=none";
            string timing = $"seconds={WiggleNumber(seconds, "F1")} frames={frames} fps={WiggleNumber(meanFrame > 0f ? 1f / meanFrame : 0f, "F1")} frameMsSpread={WiggleNumber(frameSpread * 1000f)}";
            string hullText = $"hullSpeed={WiggleNumber(totals.Velocity.Mean)} hullDevRms={WiggleNumber(totals.Velocity.Rms)} hullDevMax={WiggleNumber(totals.Velocity.Max)} hullTurnDegPerSec={WiggleNumber(totals.Turn.Mean)} hullTurnDevRms={WiggleNumber(totals.Turn.Rms)} hullTurnDevMax={WiggleNumber(totals.Turn.Max)}";
            string cameraText = $"camStepMmRms={WiggleNumber(camStepRmsMm, "F3")} camStepMmMax={WiggleNumber(totals.CameraStepMax * 1000f, "F3")} camRotStepDegRms={WiggleNumber(camRotRms, "F4")} camRotStepDegMax={WiggleNumber(totals.CameraRotationMax, "F4")}";
            string playerText = $"clock={NitroxClient.GameLogic.MovementClock.Describe()} fixedDt={WiggleNumber(Time.fixedDeltaTime, "F3")} autoSync={Physics.autoSyncTransforms} mode={(Player.main ? Player.main.mode.ToString() : "none")} aboard={(Player.main && sampledHull && Player.main.currentSub && Player.main.currentSub.transform == sampledHull)}";
            string windowText = windowNumber > 0 ? $"window={windowNumber} " : "";
            Log.Info($"[SyncHarness] wiggle: {windowText}{timing} {hullText} {cameraText} {replicaText} {bodyText} {playerText}");
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] wiggle: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
