using System.Collections;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.Extensions;
using NitroxClient.GameLogic.Engineer;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The trigger on the engine's face in the engine room that the player's hand finds (<see cref="EngineerConsole" />): "Use engineer console" when the player can, with the name of the engineer under it when
/// somebody sits there.
/// </summary>
public class EngineerConsoleHandTarget : MonoBehaviour, IHandTarget
{
    public EngineerConsole Console { get; set; }

    public void OnHandHover(GUIHand hand)
    {
        if (!Console)
        {
            return;
        }
        bool occupied = Console.TryGetOtherEngineerName(out string name);
        if (!occupied && !Console.CanEnter(out _))
        {
            return;
        }
        HandReticle.main.SetText(HandReticle.TextType.Hand, "Nitrox_EngineerConsoleUse", true, GameInput.Button.LeftHand);
        HandReticle.main.SetText(HandReticle.TextType.HandSubscript, occupied ? string.Format(Language.main.Get("Nitrox_EngineerConsoleOccupied"), name) : string.Empty, false, GameInput.Button.None);
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

/// <summary>What the engineer console knows of the engine now: the heat (the game's own 0 to 10), whether it is rising, whether the engine is on and the Cyclops' power in percent.</summary>
public readonly struct EngineReading
{
    public EngineReading(bool known, int heat, bool heating, bool engineOn, float powerPercent)
    {
        Known = known;
        Heat = heat;
        Heating = heating;
        EngineOn = engineOn;
        PowerPercent = powerPercent;
    }

    /// <summary>False when no reading is at hand (the simulator has not reported, or not for a while).</summary>
    public bool Known { get; }

    public int Heat { get; }
    public bool Heating { get; }
    public bool EngineOn { get; }
    public float PowerPercent { get; }
}

/// <summary>
/// The engineer console of a Cyclops with the Cyclops Engineer Console module (docs/coop/VEHICLE_UPGRADES.md, station 3): a small panel on the engine in the engine room. A hand target on it asks the server for the
/// seat (one engineer per Cyclops); when the server grants it the console opens: the player stays standing in the engine room in the normal mode (everybody else keeps seeing them there) and the game's PDA opens on
/// a page of its own (<see cref="EngineerPdaTab" />, the picture of the Cyclops with the bubbles of the routes, the overdrive, the vent and the fire suppression, the engine heat, the fires and the
/// breaches). The PDA does the rest the way it always does: the cursor is free and Escape or the PDA key closes it; the console closes it when the player walks more than five metres from the engine. The
/// console closes with the PDA, and by itself when the player leaves, dies, is moved or the module goes, or when the server ends the seat.
/// <para>
/// One component on every real Cyclops (added by <c>SubRoot_Start_Engineer_Patch</c>), written to the same pattern as <see cref="GunnerConsole" /> and <see cref="NavigatorConsole" /> but not sharing their code, so
/// that the merged stations are untouched; the lifecycle can be lifted into a shared base later. The hand target and the panel exist while the module is in; the client that simulates the Cyclops also tells the
/// engineer the engine heat (<see cref="CyclopsEngineerStatus" />), which only exists where the engine runs.
/// </para>
/// </summary>
public class EngineerConsole : MonoBehaviour
{
    private const string HAND_TARGET_NAME = "NitroxEngineerConsoleHandTarget";
    private const float POLL_SECONDS = 0.25f;
    private const float MAX_DISTANCE_FROM_CONSOLE = 5f;
    private const float STATUS_HEARTBEAT_SECONDS = 5f;

    /// <summary>The least time between two reports: above the server's floor (<see cref="CyclopsEngineerRules.MIN_STATUS_INTERVAL_MILLISECONDS" />), so that what is sent is taken and not dropped.</summary>
    private const float STATUS_MIN_SECONDS = 0.5f;
    private const float STATUS_STALE_SECONDS = 12f;

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
    private EngineerPanelProp panel;
    private State stateValue;
    private float requestedAt;
    private float nextPoll;
    private bool closing;

    /// <summary>The PDA is open on the engineer's page for this console (it was opened by it and has not closed yet).</summary>
    private bool pdaOpen;
    private int lastSentHeat = -1;
    private bool lastSentHeating;
    private bool lastSentEngineOn;
    private float lastSentPower = -1f;
    private float lastSentAt = float.NegativeInfinity;
    private bool panelFailed;
    private SubFire fire;

#if DEBUG
    /// <summary>Debug (the live check): moves the hand target and the panel in the hull's frame, to fit them to the engine; zero in play.</summary>
    public static Vector3 DebugHandNudge { get; set; }
#endif

#if DEBUG
    /// <summary>Debug (the live check): the state of the console as a word.</summary>
    public string DebugStateName => state.ToString().ToLowerInvariant();

    /// <summary>Debug (the live check): the panel is built and shown.</summary>
    public bool DebugHasPanel => panel && panel.gameObject.activeSelf;

    /// <summary>Debug (the live check): the hand target is built and active.</summary>
    public bool DebugHasHandTarget => handTarget && handTarget.activeSelf;
#endif

    /// <summary>The console that is open on this client, if any.</summary>
    public static EngineerConsole Current { get; private set; }

    public SubRoot SubRoot => subRoot;
    public NitroxId CyclopsId
    {
        get
        {
            TryGetCyclopsId(out NitroxId id);
            return id;
        }
    }
    public bool IsOpen => pdaOpen && state == State.Open;
    public bool IsBusy => state != State.Closed;

    /// <summary>The state, mirrored to the seat table (a grant for a console that is busy is not given back).</summary>
    private State state
    {
        get => stateValue;
        set
        {
            stateValue = value;
            EngineerSeats.SetBusy(cyclopsId, value != State.Closed);
        }
    }

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        EngineerSeats.Subscribe();
        EngineerState.Subscribe();
    }

    private void OnEnable()
    {
        EngineerSeats.Granted += OnGranted;
        EngineerSeats.Denied += OnDenied;
        EngineerSeats.Released += OnReleased;
    }

    private void OnDisable()
    {
        EngineerSeats.Granted -= OnGranted;
        EngineerSeats.Denied -= OnDenied;
        EngineerSeats.Released -= OnReleased;
    }

    private void OnDestroy()
    {
        Close(true, null);
        if (handTarget)
        {
            Destroy(handTarget);
        }
        if (panel)
        {
            Destroy(panel.gameObject);
        }
        if (cyclopsId != null)
        {
            EngineerState.Forget(cyclopsId);
            EngineerHeat.Forget(cyclopsId);
        }
    }

    private void Update()
    {
        EngineerActions.Tick();
        if (state == State.Requesting && Time.time - requestedAt > CyclopsEngineerRules.REQUEST_TIMEOUT_SECONDS)
        {
            // The server did not answer: no seat is held for this client (a grant that comes later is given back)
            EngineerSeats.CancelPending(cyclopsId);
            state = State.Closed;
            ErrorMessage.AddMessage("The engineer console did not answer");
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
        FeedStatus();
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

    /// <summary>Somebody else sits at the console, as far as this client knows; their name.</summary>
    public bool TryGetOtherEngineerName(out string name)
    {
        name = string.Empty;
        if (!TryGetCyclopsId(out NitroxId id) || !EngineerSeats.IsTakenByOther(id))
        {
            return false;
        }
        EngineerSeats.TryGetEngineer(id, out EngineerSeats.Seat seat);
        name = seat.Name;
        return true;
    }

    /// <summary>The hand target and the panel exist while the module is in, and sit on the engine (a Debug nudge aside).</summary>
    private void RefreshHandTarget()
    {
        bool available = EngineerStation.IsAvailable(subRoot);
        if (available && !handTarget)
        {
            handTarget = new GameObject(HAND_TARGET_NAME) { layer = LayerID.Useable };
            handTarget.transform.SetParent(transform, false);
            BoxCollider box = handTarget.AddComponent<BoxCollider>();
            box.isTrigger = true;
            (float sx, float sy, float sz) = EngineerConsoleLayout.HandTargetSize;
            box.size = new Vector3(sx, sy, sz);
            handTarget.AddComponent<EngineerConsoleHandTarget>().Console = this;
            Log.Info($"[VehicleUpgrades] engineer console: hand target on {subRoot.name} on the engine, hull-local {HandTargetLocal():F2} size {box.size:F2}");
        }
        if (available && !panel && !panelFailed)
        {
            panel = EngineerPanelProp.Build(transform);
            // a panel that cannot be built is not tried again every poll
            panelFailed = !panel;
        }
        if (handTarget)
        {
            handTarget.SetActive(available);
            handTarget.transform.localPosition = HandTargetLocal();
        }
        if (panel)
        {
            panel.gameObject.SetActive(available);
            panel.transform.localPosition = PanelLocal();
            if (available && TryGetCyclopsId(out NitroxId id))
            {
                bool taken = EngineerSeats.TryGetEngineer(id, out EngineerSeats.Seat seat);
                panel.Show(EngineerState.RouteOf(id), taken ? seat.Name : string.Empty);
            }
        }
        if (!available && state != State.Closed)
        {
            Close(true, "the module is out or the upgrades are off");
        }
    }

    private static Vector3 HandTargetLocal()
    {
        (float x, float y, float z) = EngineerConsoleLayout.HandTargetCenter;
        Vector3 position = new(x, y, z);
#if DEBUG
        position += DebugHandNudge;
#endif
        return position;
    }

    private static Vector3 PanelLocal()
    {
        (float x, float y, float z) = EngineerConsoleLayout.PanelCenter;
        Vector3 position = new(x, y, z);
#if DEBUG
        position += DebugHandNudge;
#endif
        return position;
    }

    private Vector3 ConsoleWorld()
    {
        (float x, float y, float z) = EngineerConsoleLayout.HandTargetCenter;
        return transform.TransformPoint(new Vector3(x, y, z));
    }

    // ---- the engine

    /// <summary>The engine now: from the engine itself where it runs (this client holds the Cyclops' lock), else from what the simulator last reported.</summary>
    public EngineReading ReadEngine()
    {
        if (!TryGetCyclopsId(out NitroxId id))
        {
            return default;
        }
        PowerRelay relay = subRoot.powerRelay;
        float power = relay && relay.GetMaxPower() > 0f ? 100f * relay.GetPower() / relay.GetMaxPower() : -1f;
        if (HoldsLock(id))
        {
            SubFire engine = Fire();
            SubControl control = GetComponent<SubControl>();
            CyclopsMotorMode motor = control ? control.cyclopsMotorMode : null;
            return new EngineReading(true, EngineerHeat.Get(engine), EngineerHeat.IsHeating(engine), motor && motor.engineOn, power);
        }
        if (EngineerHeat.TryGetLatest(id, out EngineerHeat.Reading reading) && Time.unscaledTime - reading.At <= STATUS_STALE_SECONDS)
        {
            return new EngineReading(true, reading.Heat, reading.Heating, reading.EngineOn, power >= 0f ? power : reading.PowerPercent);
        }
        return new EngineReading(false, 0, false, false, power);
    }

    private SubFire Fire()
    {
        if (!fire)
        {
            fire = subRoot.GetComponentInChildren<SubFire>(true);
        }
        return fire;
    }

    private static bool HoldsLock(NitroxId id)
    {
        return NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);
    }

    /// <summary>
    /// The client that simulates the Cyclops tells the engineer the engine heat: when it changes (the game steps it every five seconds) and as a heartbeat every five seconds, only while somebody sits at the
    /// console and only to the server (it hands it to the engineer alone).
    /// </summary>
    private void FeedStatus()
    {
        if (!TryGetCyclopsId(out NitroxId id) || !EngineerSeats.TryGetEngineer(id, out EngineerSeats.Seat seat) || seat.Session == EngineerSeats.LocalSession || !HoldsLock(id) || !EngineerStation.IsAvailable(subRoot))
        {
            lastSentHeat = -1;
            return;
        }
        EngineReading reading = ReadEngine();
        float now = Time.unscaledTime;
        bool changed = reading.Heat != lastSentHeat || reading.Heating != lastSentHeating || reading.EngineOn != lastSentEngineOn || Mathf.Abs(reading.PowerPercent - lastSentPower) >= 1f;
        if (!changed && now - lastSentAt < STATUS_HEARTBEAT_SECONDS)
        {
            return;
        }
        if (now - lastSentAt < STATUS_MIN_SECONDS)
        {
            // The server would drop this one (its floor): keep it for the next poll instead of counting it as sent
            return;
        }
        lastSentHeat = reading.Heat;
        lastSentHeating = reading.Heating;
        lastSentEngineOn = reading.EngineOn;
        lastSentPower = reading.PowerPercent;
        lastSentAt = now;
        NitroxServiceLocator.LocateService<IPacketSender>().Send(new CyclopsEngineerStatus(id, reading.Heat, reading.Heating, reading.EngineOn, reading.PowerPercent));
    }

    // ---- the buttons

    /// <summary>The engineer clicked a route: the server keeps it and tells everybody.</summary>
    public void RequestRoute(EngineerRoute route)
    {
        if (!IsOpen || cyclopsId == null)
        {
            return;
        }
        if (EngineerState.RouteOf(cyclopsId) == route)
        {
            return;
        }
        if (!EngineerActions.TryRequest(cyclopsId, CyclopsEngineerActionKind.Route, (int)route, answer => OnAnswer(CyclopsEngineerActionKind.Route, answer, $"Power sent to {EngineerText.RouteName(route)}")))
        {
            Say("A moment, please");
        }
    }

    /// <summary>The engineer pressed the vent: the simulator of the Cyclops lets the heat off and pays for it.</summary>
    public void PressVent()
    {
        if (!IsOpen || cyclopsId == null)
        {
            return;
        }
        float cooling = EngineerState.VentReadyRemaining(cyclopsId);
        if (cooling > 0f)
        {
            Say($"The vents are still cooling: {EngineerText.Seconds(cooling)}");
            return;
        }
        if (!EngineerActions.TryRequest(cyclopsId, CyclopsEngineerActionKind.Vent, 0, answer => OnAnswer(CyclopsEngineerActionKind.Vent, answer, answer.Done ? $"Vented: the heat is {answer.Value}" : null)))
        {
            Say("A moment, please");
        }
    }

    /// <summary>The engineer pressed the overdrive: the simulator of the Cyclops checks the engine and starts the heating.</summary>
    public void PressOverdrive()
    {
        if (!IsOpen || cyclopsId == null)
        {
            return;
        }
        if (EngineerState.RouteOf(cyclopsId) != EngineerRoute.Engines)
        {
            Say("Send the power to the engines first");
            return;
        }
        float cooling = EngineerState.OverdriveReadyRemaining(cyclopsId);
        if (cooling > 0f)
        {
            Say(EngineerState.IsOverdriveActive(cyclopsId) ? "The overdrive is running" : $"The overdrive is still cooling: {EngineerText.Seconds(cooling)}");
            return;
        }
        if (!EngineerActions.TryRequest(cyclopsId, CyclopsEngineerActionKind.Overdrive, 0, answer => OnAnswer(CyclopsEngineerActionKind.Overdrive, answer, "Overdrive!")))
        {
            Say("A moment, please");
        }
    }

    /// <summary>The engineer pressed the fire suppression: the game's own button does it (and Nitrox tells the others).</summary>
    public void PressSuppress()
    {
        if (!IsOpen)
        {
            return;
        }
        EngineerDamage.Suppression suppression = EngineerDamage.SuppressionOf(subRoot);
        if (!suppression.Installed)
        {
            Say("The Cyclops has no Fire Suppression System");
        }
        else if (suppression.Running)
        {
            Say("The fire suppression is running");
        }
        else if (!suppression.Ready)
        {
            Say(suppression.CooldownRemaining > 0f ? $"The fire suppression is recharging: {EngineerText.Seconds(suppression.CooldownRemaining)}" : "The Cyclops has no power");
        }
        else if (EngineerDamage.TrySuppress(subRoot))
        {
            Say("Fire suppression started");
        }
    }

    private void OnAnswer(CyclopsEngineerActionKind action, EngineerAnswer answer, string doneText)
    {
        if (!answer.Done)
        {
            Log.Info($"[VehicleUpgrades] engineer console: {action} was not done: {(answer.Answered ? answer.Failure.ToString() : "no answer")}");
            Say(EngineerActions.TextOf(action, answer));
            return;
        }
        if (!string.IsNullOrEmpty(doneText))
        {
            Say(doneText);
        }
    }

    private void Say(string text)
    {
        EngineerPdaTab tab = EngineerPdaTab.Instance;
        if (this && pdaOpen && tab && !string.IsNullOrEmpty(text))
        {
            tab.ShowMessage(text);
        }
    }

    // ---- the seat

    /// <summary>Whether the local player can use the console now; <paramref name="why" /> says why not.</summary>
    public bool CanEnter(out string why)
    {
        why = null;
        Player player = Player.main;
        PDA pda = player ? player.GetPDA() : null;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!EngineerStation.IsAvailable(subRoot))
        {
            why = "the Cyclops has no Cyclops Engineer Console, or the vehicle upgrades are off";
        }
        else if (state != State.Closed || GunnerConsole.AnyConsoleOpen)
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
        else if (pda && pda.isInUse)
        {
            // (the PDA stays in use for a moment after it closes)
            why = "the PDA is in use";
        }
        else if (!TryGetCyclopsId(out _))
        {
            why = "the Cyclops has no id yet";
        }
        return why == null;
    }

    /// <summary>The player used the console: ask the server for the seat. The console opens when the server grants it.</summary>
    public void Enter()
    {
        if (!CanEnter(out string why))
        {
            Log.Info($"[VehicleUpgrades] engineer console: cannot be used now: {why}");
            return;
        }
        state = State.Requesting;
        requestedAt = Time.time;
        EngineerSeats.Request(cyclopsId);
        Log.Info($"[VehicleUpgrades] engineer console: asked the server for the seat on {subRoot.name}");
    }

    private void OnGranted(EngineerSeatGrant grant)
    {
        if (state != State.Requesting || cyclopsId == null || !cyclopsId.Equals(grant.CyclopsId))
        {
            return;
        }
        if (!CanStayOpen(out string why))
        {
            // The player moved on while the server was answering: the seat is not kept (the table gives it back)
            Log.Info($"[VehicleUpgrades] engineer console: the seat was granted but is not wanted any more: {why}");
            state = State.Closed;
            return;
        }
        grant.Accepted = true;
        StartCoroutine(OpenRoutine());
    }

    private void OnDenied(CyclopsEngineerSeat packet)
    {
        if (state == State.Requesting && cyclopsId != null && cyclopsId.Equals(packet.CyclopsId))
        {
            state = State.Closed;
            ErrorMessage.AddMessage(EngineerSeats.DeniedText(packet));
            Log.Info($"[VehicleUpgrades] engineer console: the seat was refused: {packet.Reason}{(packet.Reason == CyclopsEngineerSeatReason.Occupied ? $" ({packet.PlayerName} sits there)" : string.Empty)}");
        }
    }

    private void OnReleased(CyclopsEngineerSeat packet)
    {
        // (no seat is held while the request is out: a Released of the seat that was just given up comes first, and the Seated that follows decides)
        if (state == State.Closed || state == State.Requesting || cyclopsId == null || !cyclopsId.Equals(packet.CyclopsId))
        {
            return;
        }
        if (packet.SessionId == EngineerSeats.LocalSession)
        {
            string text = EngineerSeats.ReleasedText(packet);
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
        yield return null; // the next frame, out of the packet that carried the grant

        string why = null;
        if (!this || !CanStayOpen(out why))
        {
            Log.Info($"[VehicleUpgrades] engineer console: not opened: {why ?? "closed meanwhile"}");
            Close(true, null);
            yield break;
        }

        // The PDA opens on the engineer's page (it puts away what is held and gives it back, and takes the cursor); it closes by itself when the player walks away from the engine
        EngineerPdaTab tab = EngineerPdaTab.Instance;
        PDA pda = Player.main ? Player.main.GetPDA() : null;
        if (!tab || !pda)
        {
            why = "the PDA is not ready";
        }
        else if (!tab.TryAttach(this, out why))
        {
            // (the reason is in why)
        }
        else
        {
            GameInput.AutoMove = false;
            // No target for the PDA's own distance check: it closes the PDA a metre past where the player stood, by world positions, and a Cyclops that moves (the engineer is meant to work while somebody flies it)
            // would move the player and the target apart for a frame; the console's own check (CanStayOpen: five metres from the engine) closes it when the engineer walks away
            if (pda.Open(EngineerPdaTab.TAB_ID, null, OnPdaClosed))
            {
                pdaOpen = true;
                uGUI_PDA ui = uGUI_PDA.main;
                if (ui && ui.currentTabType != EngineerPdaTab.TAB_ID)
                {
                    // (the PDA should have opened on the page by itself)
                    Log.Warn($"[VehicleUpgrades] engineer console: the PDA opened on the page {ui.currentTabType}; asking for the engineer's page");
                    ui.OpenTab(EngineerPdaTab.TAB_ID);
                }
                Log.Info($"[VehicleUpgrades] engineer console: the PDA is open on the page {(ui ? ui.currentTabType.ToString() : "?")}");
            }
            else
            {
                tab.Detach(this);
                why = "the PDA is in use";
            }
        }
        if (!pdaOpen)
        {
            Log.Info($"[VehicleUpgrades] engineer console: not opened: {why}");
            ErrorMessage.AddMessage("The engineer console cannot open now");
            Close(true, null);
            yield break;
        }
        Current = this;
        state = State.Open;
        Log.Info($"[VehicleUpgrades] engineer console: opened on {subRoot.name}, route {EngineerState.RouteOf(cyclopsId)}");
    }

    private void UpdateOpen()
    {
        PDA pda = Player.main ? Player.main.GetPDA() : null;
        if (!pda || !pda.isInUse)
        {
            // The PDA ended without telling (nothing would show the page or hold the player at the console)
            Log.Info("[VehicleUpgrades] engineer console: closed because the PDA is not in use");
            Close(true, "the PDA is not in use");
            return;
        }
        if (pda.isFocused && GameInput.GetButtonDown(GameInput.Button.Exit))
        {
            Close(true, "the exit key");
            return;
        }
        if (!CanStayOpen(out string why))
        {
            Log.Info($"[VehicleUpgrades] engineer console: closed because {why}");
            Close(true, why);
        }
    }

    /// <summary>Whether the console may stay open: the player is still at the console, alive and free, the module is still in and the seat is still theirs.</summary>
    private bool CanStayOpen(out string why)
    {
        why = null;
        Player player = Player.main;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!EngineerStation.IsAvailable(subRoot))
        {
            why = "the Engineer Console is out or the upgrades are off";
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
        else if (NavigatorConsole.Current || GunnerConsole.Current)
        {
            why = "another console is open";
        }
        else if (Vector3.Distance(player.transform.position, ConsoleWorld()) > MAX_DISTANCE_FROM_CONSOLE)
        {
            why = "the player was moved away from the console";
        }
        else if (cyclopsId == null || !EngineerSeats.IsMine(cyclopsId))
        {
            why = "the seat is not the player's any more";
        }
        return why == null;
    }

    /// <summary>
    /// Closes the console: the PDA is closed (it gives the hands and the cursor back), the page lets go of the console and the seat is given up (<paramref name="tellServer" /> false when the server ended the seat,
    /// which needs no telling). Safe to call twice.
    /// </summary>
    public void Close(bool tellServer, string reason)
    {
        if (closing || state == State.Closed && !pdaOpen)
        {
            return;
        }
        closing = true;
        try
        {
            bool wasCurrent = Current == this;
            EngineerPdaTab tab = EngineerPdaTab.Instance;
            if (tab)
            {
                tab.Detach(this);
            }
            if (pdaOpen)
            {
                // (the PDA tells us back when it closes, which finds the console closing and does nothing)
                pdaOpen = false;
                PDA pda = Player.main ? Player.main.GetPDA() : null;
                if (pda && pda.isInUse)
                {
                    pda.Close();
                }
            }
            EngineerActions.Reset();
            if (wasCurrent)
            {
                Current = null;
                Log.Info($"[VehicleUpgrades] engineer console: closed on {(subRoot ? subRoot.name : "a destroyed Cyclops")}{(string.IsNullOrEmpty(reason) ? string.Empty : $" ({reason})")}");
            }
            if (tellServer && cyclopsId != null && EngineerSeats.IsMine(cyclopsId))
            {
                EngineerSeats.Leave(cyclopsId);
            }
        }
        finally
        {
            state = State.Closed;
            closing = false;
        }
    }

    /// <summary>The PDA closed (the exit key, the PDA key, or the player walked away from the engine): the console is over. The callback of <c>PDA.Open</c>.</summary>
    public void OnPdaClosed(PDA pda)
    {
        // the PDA is closing itself: nothing is left to close
        pdaOpen = false;
        if (!closing && state != State.Closed)
        {
            Close(true, "the PDA was closed");
        }
    }
}
