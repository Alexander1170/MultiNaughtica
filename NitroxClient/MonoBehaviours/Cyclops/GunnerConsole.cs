using System.Collections;
using Nitrox.Model.DataStructures;
using NitroxClient.Extensions;
using NitroxClient.GameLogic.Gunner;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The trigger on the right desk of the cockpit that the player's hand finds (<see cref="GunnerConsole" />): "Use gunner station" when the player can, with the name of the gunner under it when somebody sits there.
/// </summary>
public class GunnerConsoleHandTarget : MonoBehaviour, IHandTarget
{
    public GunnerConsole Console { get; set; }

    public void OnHandHover(GUIHand hand)
    {
        if (!Console)
        {
            return;
        }
        bool occupied = Console.TryGetOtherGunnerName(out string name);
        if (!occupied && !Console.CanEnter(out _))
        {
            return;
        }
        HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_GunnerStationUse", true, GameInput.Button.LeftHand);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, occupied ? string.Format(Language.main.Get("Nitrox_GunnerStationOccupied"), name) : string.Empty, false, GameInput.Button.None);
        HandReticle.main.SetIcon(HandReticle.IconType.Interact, 1f);
    }

    public void OnHandClick(GUIHand hand)
    {
        if (Console)
        {
            Console.Enter();
        }
    }
}

/// <summary>
/// The gunner station of a Cyclops with the Cyclops Gunner Station module (docs/coop/VEHICLE_UPGRADES.md, station 2): the right desk of the cockpit. A hand target on the desk asks the server for the seat (one gunner
/// per Cyclops); when the server grants it the console opens: the player stays standing at the desk in the normal mode (everybody else keeps seeing them there), the cursor is hidden and the walking and looking
/// are locked (the game's own input group, the way the PDA and the chat do it), the player's own camera is moved to the turret (<see cref="TurretCameraRig" />) and a screen overlay
/// (<see cref="GunnerConsoleScreen" />) draws the crosshair, the charge and the readouts. The mouse aims, the button held charges and released fires (<see cref="TryFire" />: the server pays for the shot through
/// the player who simulates the Cyclops, then the sphere is launched). It closes with the exit key, Escape or the button, and by itself when the player leaves, dies, is moved or the module goes, or when the server
/// ends the seat.
/// <para>
/// One component on every real Cyclops (added by <c>SubRoot_Start_Gunner_Patch</c>), written to the same pattern as <see cref="NavigatorConsole" /> but not sharing its code, so that the merged navigator is
/// untouched; the lifecycle can be lifted into a shared base when a third console is written. The hand target exists while the module is in; nothing else runs while the console is closed.
/// </para>
/// </summary>
public class GunnerConsole : MonoBehaviour
{
    private const string HAND_TARGET_NAME = "NitroxGunnerConsoleHandTarget";
    private const float POLL_SECONDS = 0.25f;
    private const float TARGETING_SECONDS = 0.2f;
    private const float MAX_DISTANCE_FROM_DESK = 4.5f;
    private const float SPHERE_SHELL_RADIUS = 0.5f;
    private const int SCAN_SECTORS_PER_FRAME = 6;
    private const float SCAN_FIRST_PITCH = -2f;
    private const float SCAN_LAST_PITCH = -80f;
    private const float SCAN_PITCH_STEP = 2f;
    private const float SCAN_MARGIN_DEGREES = 3f;
    private const float SCAN_REACH_METRES = 40f;
    private const int HULL_LAYERS = 1 << 23;
    private const int FOCUS_FRAMES = 3;

    // the hit layers of the game's stasis sphere (Bullet.hitLayers of its prefab); used until the sphere has been made and can be asked
    private const int FALLBACK_HIT_LAYERS = 2101362377;

    private static readonly RaycastHit[] hits = new RaycastHit[24];

    private enum State
    {
        Closed,
        Requesting,
        Opening,
        Open
    }

    private SubRoot subRoot;
    private NitroxId cyclopsId;
    private GameObject handTarget;
    private GunnerConsoleScreen screen;
    private TurretCameraRig rig;
    private State stateValue;
    private float requestedAt;
    private float nextPoll;
    private float nextTargeting;
    private int savedQuickSlot = -1;
    private float lastReleaseAt = float.NegativeInfinity;
    private bool closing;
    private int unfocusedFrames;
    private float[] horizon;

#if DEBUG
    /// <summary>Debug (the live check): moves the hand target in the hull's frame, to fit it to the desk; zero in play.</summary>
    public static Vector3 DebugHandNudge { get; set; }

    /// <summary>Debug (the live check): forgets the local cooldown, so that the next release is not turned down for it.</summary>
    public void DebugResetCooldown()
    {
        lastReleaseAt = float.NegativeInfinity;
    }

    /// <summary>Debug (the live check): the id of this Cyclops, if it has one.</summary>
    public bool TryGetCyclopsIdForDebug(out NitroxId id) => TryGetCyclopsId(out id);
#endif

    /// <summary>The console that is open on this client, if any.</summary>
    public static GunnerConsole Current { get; private set; }

    /// <summary>A console of the Cyclops (the navigator's, the gunner's, the engineer's or the drone bay's) is open on this client: what draws over the screen of a Cyclops (the route, the clearance text) steps aside.</summary>
    public static bool AnyConsoleOpen => Current || NavigatorConsole.Current || EngineerConsole.Current || DroneBay.Current;

    public SubRoot SubRoot => subRoot;
    public TurretCameraRig Rig => rig;
    public bool IsOpen => screen && state == State.Open;
    public bool IsBusy => state != State.Closed;

    /// <summary>What the range readout says: the distance to what a shot along the aim would hit first, with its name, or that nothing is in range. Empty when nothing was measured yet.</summary>
    public string TargetText { get; private set; } = string.Empty;

    /// <summary>The distance to that target, NaN when there is none.</summary>
    public float TargetDistance { get; private set; } = float.NaN;

    public bool IsWaitingForShot => GunnerFire.IsWaiting;

    /// <summary>Seconds still to wait for the next shot.</summary>
    public float CooldownRemaining => GunnerRules.CooldownRemaining(lastReleaseAt, Time.unscaledTime, VehicleUpgradeSettings.GunnerCooldownSeconds);

    /// <summary>The Cyclops' energy in percent of what its cells hold, -1 when it is not known.</summary>
    public float PowerPercent
    {
        get
        {
            PowerRelay relay = subRoot ? subRoot.powerRelay : null;
            return relay && relay.GetMaxPower() > 0f ? 100f * relay.GetPower() / relay.GetMaxPower() : -1f;
        }
    }

    /// <summary>The state, mirrored to the seat table (a grant for a desk whose console is busy is not given back).</summary>
    private State state
    {
        get => stateValue;
        set
        {
            stateValue = value;
            GunnerSeats.SetBusy(cyclopsId, value != State.Closed);
        }
    }

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        GunnerSeats.Subscribe();
        GunnerTurret.Subscribe();
    }

    private void OnEnable()
    {
        GunnerSeats.Granted += OnGranted;
        GunnerSeats.Denied += OnDenied;
        GunnerSeats.Released += OnReleased;
    }

    private void OnDisable()
    {
        GunnerSeats.Granted -= OnGranted;
        GunnerSeats.Denied -= OnDenied;
        GunnerSeats.Released -= OnReleased;
    }

    private void OnDestroy()
    {
        Close(true, null);
        if (handTarget)
        {
            Destroy(handTarget);
        }
    }

    private void Update()
    {
        GunnerFire.Tick();
        if (state == State.Requesting && Time.time - requestedAt > GunnerRules.REQUEST_TIMEOUT_SECONDS)
        {
            // The server did not answer: no seat is held for this client (a grant that comes later is given back)
            GunnerSeats.CancelPending(cyclopsId);
            state = State.Closed;
            ErrorMessage.AddMessage("The gunner station did not answer");
        }
        if (IsOpen)
        {
            UpdateOpen();
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        RefreshHandTarget();
    }

    private bool TryGetCyclopsId(out NitroxId id)
    {
        if (cyclopsId == null)
        {
            subRoot.TryGetNitroxId(out cyclopsId);
        }
        id = cyclopsId;
        return id != null;
    }

    /// <summary>Somebody else sits at the desk, as far as this client knows; their name.</summary>
    public bool TryGetOtherGunnerName(out string name)
    {
        name = string.Empty;
        if (!TryGetCyclopsId(out NitroxId id) || !GunnerSeats.IsTakenByOther(id))
        {
            return false;
        }
        GunnerSeats.TryGetGunner(id, out GunnerSeats.Seat seat);
        name = seat.Name;
        return true;
    }

    /// <summary>The hand target exists while the module is in, and sits on the desk (a Debug nudge aside).</summary>
    private void RefreshHandTarget()
    {
        bool available = GunnerStation.IsAvailable(subRoot);
        if (available && !handTarget)
        {
            handTarget = new GameObject(HAND_TARGET_NAME) { layer = LayerID.Useable };
            handTarget.transform.SetParent(transform, false);
            BoxCollider box = handTarget.AddComponent<BoxCollider>();
            box.isTrigger = true;
            (float sx, float sy, float sz) = GunnerStationLayout.HandTargetSize;
            box.size = new Vector3(sx, sy, sz);
            handTarget.AddComponent<GunnerConsoleHandTarget>().Console = this;
            Log.Info($"[VehicleUpgrades] gunner console: hand target on {subRoot.name} at the right desk, hull-local {HandTargetLocal():F2} size {box.size:F2}");
        }
        if (handTarget)
        {
            handTarget.SetActive(available);
            handTarget.transform.localPosition = HandTargetLocal();
        }
        if (!available && state != State.Closed)
        {
            Close(true, "the module is out or the upgrades are off");
        }
    }

    private static Vector3 HandTargetLocal()
    {
        (float x, float y, float z) = GunnerStationLayout.HandTargetCenter;
        Vector3 position = new(x, y, z);
#if DEBUG
        position += DebugHandNudge;
#endif
        return position;
    }

    private Vector3 DeskWorld()
    {
        (float x, float y, float z) = GunnerStationLayout.HandTargetCenter;
        return transform.TransformPoint(new Vector3(x, y, z));
    }

    /// <summary>Whether the local player can use the console now; <paramref name="why" /> says why not.</summary>
    public bool CanEnter(out string why)
    {
        why = null;
        Player player = Player.main;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!GunnerStation.IsAvailable(subRoot))
        {
            why = "the Cyclops has no Cyclops Gunner Station, or the vehicle upgrades are off";
        }
        else if (state != State.Closed || AnyConsoleOpen)
        {
            why = "a console is open already";
        }
        else if (!player || player.currentSub != subRoot)
        {
            why = "the player is not aboard this Cyclops";
        }
        else if (!player.IsAlive() || player.GetMode() != Player.Mode.Normal || player.cinematicModeActive)
        {
            why = "the player is busy (dead, at the helm, in a cinematic or a vehicle)";
        }
        else if (!MainCamera.camera || !SNCameraRoot.main)
        {
            why = "there is no camera";
        }
        else if (!TryGetCyclopsId(out _))
        {
            why = "the Cyclops has no id yet";
        }
        return why == null;
    }

    /// <summary>The player used the desk: ask the server for the seat. The console opens when the server grants it.</summary>
    public void Enter()
    {
        if (!CanEnter(out string why))
        {
            Log.Info($"[VehicleUpgrades] gunner console: cannot be used now: {why}");
            return;
        }
        state = State.Requesting;
        requestedAt = Time.time;
        GunnerSeats.Request(cyclopsId);
        Log.Info($"[VehicleUpgrades] gunner console: asked the server for the seat on {subRoot.name}");
    }

    private void OnGranted(GunnerSeatGrant grant)
    {
        if (state != State.Requesting || cyclopsId == null || !cyclopsId.Equals(grant.CyclopsId))
        {
            return;
        }
        if (!CanStayOpen(out string why))
        {
            // The player moved on while the server was answering: the seat is not kept (the table gives it back)
            Log.Info($"[VehicleUpgrades] gunner console: the seat was granted but is not wanted any more: {why}");
            state = State.Closed;
            return;
        }
        grant.Accepted = true;
        StartCoroutine(OpenRoutine());
    }

    private void OnDenied(CyclopsGunnerSeat packet)
    {
        if (state == State.Requesting && cyclopsId != null && cyclopsId.Equals(packet.CyclopsId))
        {
            state = State.Closed;
            ErrorMessage.AddMessage(GunnerSeats.DeniedText(packet));
            Log.Info($"[VehicleUpgrades] gunner console: the seat was refused: {packet.Reason}{(packet.Reason == CyclopsGunnerSeatReason.Occupied ? $" ({packet.PlayerName} sits there)" : string.Empty)}");
        }
    }

    private void OnReleased(CyclopsGunnerSeat packet)
    {
        // (no seat is held while the request is out: a Released of the seat that was just given up comes first, and the Seated that follows decides)
        if (state == State.Closed || state == State.Requesting || cyclopsId == null || !cyclopsId.Equals(packet.CyclopsId))
        {
            return;
        }
        if (packet.SessionId == GunnerSeats.LocalSession)
        {
            string text = GunnerSeats.ReleasedText(packet);
            if (!string.IsNullOrEmpty(text))
            {
                ErrorMessage.AddMessage(text);
            }
            Close(false, $"the server ended the seat ({packet.Reason})");
        }
    }

    private IEnumerator OpenRoutine()
    {
        state = State.Opening;
        GameObject screenObject = new("NitroxGunnerConsole");
        screen = screenObject.AddComponent<GunnerConsoleScreen>();
        screen.Setup(this);
        yield return null; // the input group needs its first frame before it can be selected (the chat does the same)

        string why = null;
        if (!this || !screen || !CanStayOpen(out why))
        {
            Log.Info($"[VehicleUpgrades] gunner console: not opened: {why ?? "closed meanwhile"}");
            Close(true, null);
            yield break;
        }

        // The hands: put away what is held and remember the quick slot, as the helm's chair does (a held Stasis Rifle must not fire with the turret)
        Inventory inventory = Inventory.main;
        savedQuickSlot = inventory.quickSlots.activeSlot;
        inventory.ReturnHeld(true);
        GameInput.AutoMove = false;
        Current = this;
        screen.Open();
        yield return null; // one frame for the group to take the player
        if (!this || !screen || !screen.IsFocused || !FPSInputModule.current || FPSInputModule.current.lastGroup != screen)
        {
            Log.Info("[VehicleUpgrades] gunner console: not opened: the input group did not take the player");
            Close(true, null);
            yield break;
        }
        rig = gameObject.AddComponent<TurretCameraRig>();
        if (!rig.Begin(subRoot))
        {
            Log.Warn("[VehicleUpgrades] gunner console: there is no camera to move to the turret");
            Close(true, null);
            yield break;
        }
        unfocusedFrames = 0;
        state = State.Open;
        StartCoroutine(ScanHorizon());
        Log.Info($"[VehicleUpgrades] gunner console: opened on {subRoot.name}, range {VehicleUpgradeSettings.GunnerRange:F0} m, shot {VehicleUpgradeSettings.GunnerShotEnergy:F1} energy, cooldown {VehicleUpgradeSettings.GunnerCooldownSeconds:F1} s");
    }

    private void UpdateOpen()
    {
        if (screen && !screen.IsFocused)
        {
            // The input group did not take (or was lost): nothing locks the player and no key would close it
            unfocusedFrames++;
            if (unfocusedFrames >= FOCUS_FRAMES)
            {
                Log.Info("[VehicleUpgrades] gunner console: closed because the input group is not focused");
                Close(true, "the input group is not focused");
                return;
            }
        }
        else
        {
            unfocusedFrames = 0;
        }
        if (!CanStayOpen(out string why))
        {
            Log.Info($"[VehicleUpgrades] gunner console: closed because {why}");
            Close(true, why);
            return;
        }
        if (Time.unscaledTime >= nextTargeting && rig && rig.Active)
        {
            nextTargeting = Time.unscaledTime + TARGETING_SECONDS;
            UpdateTargeting();
        }
    }

    /// <summary>Whether the console may stay open: the player is still at the desk, alive and free, the module is still in and the seat is still theirs.</summary>
    private bool CanStayOpen(out string why)
    {
        why = null;
        Player player = Player.main;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!GunnerStation.IsAvailable(subRoot))
        {
            why = "the Gunner Station is out or the upgrades are off";
        }
        else if (!player || !player.IsAlive())
        {
            why = "the player is gone or dead";
        }
        else if (player.currentSub != subRoot)
        {
            why = "the player left the Cyclops";
        }
        else if (player.GetMode() != Player.Mode.Normal || player.cinematicModeActive)
        {
            why = "the player is in another mode";
        }
        else if (NavigatorConsole.Current)
        {
            why = "the navigator console is open";
        }
        else if (Vector3.Distance(player.transform.position, DeskWorld()) > MAX_DISTANCE_FROM_DESK)
        {
            why = "the player was moved away from the desk";
        }
        else if (cyclopsId == null || !GunnerSeats.IsMine(cyclopsId))
        {
            why = "the seat is not the player's any more";
        }
        return why == null;
    }

    // ---- the shot

    /// <summary>
    /// The gunner released a charge. Everything the client can check is checked first (nothing is sent for a shot that cannot be): the aim is kept in the hull's frame, the server pays for the shot through the
    /// player who simulates the Cyclops, and when the answer is "paid" the sphere is launched along the aim of the release.
    /// </summary>
    public bool TryFire(float charge, out string why)
    {
        why = null;
        if (!GunnerRules.IsShot(charge))
        {
            return false;
        }
        if (!IsOpen || !rig || !rig.Active || cyclopsId == null)
        {
            why = "not at the desk";
        }
        else if (GunnerFire.IsWaiting)
        {
            why = "The last shot is not answered yet";
        }
        else if (!GunnerSeats.IsMine(cyclopsId))
        {
            why = "You are not at the gunner station";
        }
        else if (CooldownRemaining > 0f)
        {
            why = "The turret needs a moment";
        }
        else if (!GunnerTurret.CanLaunch(CyclopsGunnerAmmo.StasisSphere, out string cannot))
        {
            why = $"No shot: {cannot}";
        }
        if (why != null)
        {
            return false;
        }

        Vector3 aimLocal = rig.AimLocal;
        float shotCharge = Mathf.Clamp01(charge);
        lastReleaseAt = Time.unscaledTime;
        GunnerFire.TryRequest(cyclopsId, shotCharge, answer => OnAnswer(answer, aimLocal, shotCharge));
        Log.Info($"[VehicleUpgrades] gunner: asked for a shot of {shotCharge:F2}, aim yaw {rig.Yaw:F1} pitch {rig.Pitch:F1}");
        return true;
    }

    private void OnAnswer(GunnerShotAnswer answer, Vector3 aimLocal, float charge)
    {
        if (!answer.Paid)
        {
            string text = GunnerFire.TextOf(answer);
            Log.Info($"[VehicleUpgrades] gunner: no shot: {(answer.Answered ? answer.Failure.ToString() : "no answer")}");
            if (this && screen)
            {
                screen.ShowMessage(text);
            }
            return;
        }
        if (!this || !subRoot)
        {
            Log.Info("[VehicleUpgrades] gunner: the shot was paid for but the Cyclops is gone: no sphere");
            return;
        }
        // Along the aim of the release, from where the turret is now (the Cyclops may have moved), even if the gunner has pressed the exit key meanwhile: the energy is paid and the server saw a seat
        Vector3 direction = transform.TransformDirection(aimLocal).normalized;
        (float mx, float my, float mz) = GunnerStationLayout.MountPosition;
        Vector3 pivot = transform.TransformPoint(new Vector3(mx, my, mz));
        (float x, float y, float z) = GunnerRules.Muzzle((pivot.x, pivot.y, pivot.z), (direction.x, direction.y, direction.z));
        GunnerTurret.Fire(CyclopsGunnerAmmo.StasisSphere, subRoot, new Vector3(x, y, z), Quaternion.LookRotation(direction), charge, VehicleUpgradeSettings.GunnerRange);
    }

    private static int HitLayers => GunnerTurret.Sphere ? (int)GunnerTurret.Sphere.hitLayers : FALLBACK_HIT_LAYERS;

    /// <summary>The range readout and the hull indicator: what a shot along the aim would hit first, within the range of the shot.</summary>
    private void UpdateTargeting()
    {
        Transform pivot = rig.Pivot;
        if (!pivot)
        {
            return;
        }
        Vector3 direction = pivot.forward;
        float range = VehicleUpgradeSettings.GunnerRange;
        Vector3 start = pivot.position + direction * GunnerRules.MUZZLE_OFFSET;
        // As the sphere casts: triggers count, and everything of this Cyclops is skipped
        int count = Physics.SphereCastNonAlloc(start, SPHERE_SHELL_RADIUS, direction, hits, range, HitLayers, QueryTriggerInteraction.Collide);
        float best = float.PositiveInfinity;
        Collider bestCollider = null;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (!collider || collider.GetComponentInParent<SubRoot>() == subRoot || collider.GetComponentInParent<Player>())
            {
                continue;
            }
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                bestCollider = collider;
            }
        }
        if (bestCollider)
        {
            TargetDistance = best + GunnerRules.MUZZLE_OFFSET;
            TargetText = $"{TargetDistance:F0} m  {NameOf(bestCollider)}";
        }
        else
        {
            TargetDistance = float.NaN;
            TargetText = "no target in range";
        }
    }

    private static string NameOf(Collider collider)
    {
        try
        {
            GameObject root = UWE.Utils.GetEntityRoot(collider.gameObject);
            TechType tech = root ? CraftData.GetTechType(root) : TechType.None;
            return tech != TechType.None ? tech.AsString() : "terrain";
        }
        catch (System.Exception)
        {
            return "terrain";
        }
    }

    // ---- the horizon

    /// <summary>
    /// What the hull allows of the turret, scanned from its colliders: for each sector of five degrees round the mount, how far down the sphere shell can look before it meets the Cyclops itself. It refines the
    /// analytic table (<see cref="GunnerRules.MinPitch(float, float[])" />) with the hull as it really is; the table is used until a sector is done. A few sectors a frame, so that opening costs no hitch.
    /// </summary>
    private IEnumerator ScanHorizon()
    {
        Transform pivot = rig ? rig.Pivot : null;
        if (!pivot)
        {
            yield break;
        }
        float[] scan = new float[GunnerRules.SCAN_SECTORS];
        for (int i = 0; i < scan.Length; i++)
        {
            scan[i] = float.NaN;
        }
        horizon = scan;
        rig.Horizon = scan;
        int mask = HitLayers | HULL_LAYERS;
        Vector3 origin = pivot.position;
        for (int sector = 0; sector < GunnerRules.SCAN_SECTORS; sector++)
        {
            if (!this || !rig || !rig.Active)
            {
                yield break;
            }
            float yaw = -180f + sector * GunnerRules.SCAN_STEP_DEGREES;
            float lowest = SCAN_LAST_PITCH;
            for (float pitch = SCAN_FIRST_PITCH; pitch >= SCAN_LAST_PITCH; pitch -= SCAN_PITCH_STEP)
            {
                (float dx, float dy, float dz) = GunnerRules.Direction(yaw, pitch);
                Vector3 direction = transform.TransformDirection(new Vector3(dx, dy, dz));
                if (Physics.SphereCast(origin, SPHERE_SHELL_RADIUS, direction, out RaycastHit hit, SCAN_REACH_METRES, mask, QueryTriggerInteraction.Ignore) && hit.collider && hit.collider.GetComponentInParent<SubRoot>() == subRoot)
                {
                    // the hull is in the way at this pitch: the last clear one, and a margin
                    lowest = Mathf.Min(0f, pitch + SCAN_PITCH_STEP + SCAN_MARGIN_DEGREES);
                    break;
                }
            }
            scan[sector] = lowest;
            if ((sector + 1) % SCAN_SECTORS_PER_FRAME == 0)
            {
                yield return null;
            }
        }
        Log.Info($"[VehicleUpgrades] gunner: the hull's horizon is scanned: abeam to starboard {GunnerRules.MinPitch(90f, scan):F0}, to port {GunnerRules.MinPitch(-90f, scan):F0}, ahead {GunnerRules.MinPitch(0f, scan):F0}, astern {GunnerRules.MinPitch(180f, scan):F0} degrees (the table says {GunnerRules.MinPitch(90f):F0}, {GunnerRules.MinPitch(0f):F0}, {GunnerRules.MinPitch(180f):F0})");
    }

    /// <summary>The scanned horizon (the lowest clear pitch of each sector), for the Debug state line; null until the scan has begun.</summary>
    public float[] Horizon => horizon;

    // ---- closing

    /// <summary>
    /// Closes the console: the camera goes back first, then the input is given back, the overlay goes, the hands are put back and the seat is given up (<paramref name="tellServer" /> false when the server ended
    /// it). Safe to call at any time and more than once.
    /// </summary>
    public void Close(bool tellServer, string reason)
    {
        if (closing || state == State.Closed && !screen && !rig)
        {
            return;
        }
        closing = true;
        try
        {
            bool wasCurrent = Current == this;
            // The camera is put back before anything else can throw
            if (rig)
            {
                rig.End();
                Destroy(rig);
            }
            rig = null;
            if (screen)
            {
                screen.Shut();
                Destroy(screen.gameObject);
            }
            screen = null;
            if (wasCurrent)
            {
                Current = null;
                if (savedQuickSlot >= 0 && Inventory.main != null && Player.main && Player.main.GetMode() == Player.Mode.Normal)
                {
                    Inventory.main.quickSlots.SelectImmediate(savedQuickSlot);
                }
                Log.Info($"[VehicleUpgrades] gunner console: closed on {(subRoot ? subRoot.name : "a destroyed Cyclops")}{(string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})")}");
            }
            savedQuickSlot = -1;
            TargetText = string.Empty;
            TargetDistance = float.NaN;
            if (tellServer && cyclopsId != null && GunnerSeats.IsMine(cyclopsId))
            {
                GunnerSeats.Leave(cyclopsId);
            }
        }
        finally
        {
            state = State.Closed;
            closing = false;
        }
    }

    /// <summary>The input group was taken from the screen by something else (the game menu, another group): the console is over.</summary>
    public void OnScreenDeselected()
    {
        if (!closing && state != State.Closed)
        {
            Close(true, "the input was taken away");
        }
    }
}
