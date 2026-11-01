using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Drone;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.Simulation;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>Marks the dock that the bay builds on the hull (the patches of the game's docking code tell it from the scanner room's docks by this).</summary>
public class DroneDockMarker : MonoBehaviour
{
    public DroneBay Bay { get; set; }
}

/// <summary>Marks the <c>MapRoomScreen</c> that the bay flies the drone from (the game's screen code skips it: it has no scanner room and no list of cameras).</summary>
public class DroneScreenMarker : MonoBehaviour
{
}

/// <summary>
/// The drone bay of a Cyclops with the Cyclops Drone Bay module (docs/coop/VEHICLE_UPGRADES.md, feature group 3, station 4; design note scout_drone_design.md): the left screen of the vehicle bay terminal
/// (<see cref="DroneTerminalScreen" />), whose round bubble loads a Scout Drone from the inventory into the bay and whose second one flies the drone that is there, and a dock on the hull outside (the game's own
/// <c>MapRoomCameraDocking</c>, parented to the hull) that holds the drone and recharges it. (The first builds had a panel on the engine; the play-test of coop.12 moved the drone to the terminal.) The drone is flown with the game's own control (<c>MapRoomCamera.ControlCamera</c>, its screen and its HUD), from a <c>MapRoomScreen</c> that this
/// component adds to the dock because the game's code wants one. One component on every real Cyclops (added by <c>SubRoot_Start_Drone_Patch</c>); the screen on the terminal and the dock exist while the module is in.
/// <para>
/// With other players: the seat (one operator per Cyclops) and the state of the bay (which drone, docked or out) are the server's (<see cref="DroneSeats" />, <see cref="DroneBayState" />); this client reports what
/// its player does (load, unload, dock, undock) and carries out what the server says. The operator's client takes the exclusive lock of the drone, which makes the drone a vehicle of the movement pipeline: its
/// position travels like a Seamoth's (<see cref="Vehicles.DroneMovementReplicator" /> on the other clients).
/// </para>
/// </summary>
public class DroneBay : MonoBehaviour
{
    private const string DOCK_NAME = "NitroxDroneDock";
    private const string SCREEN_NAME = "NitroxDroneScreen";
    private const float POLL_SECONDS = 0.25f;

    /// <summary>How far the body may be from the place where the terminal was used before it is put back (metres).</summary>
    private const float BODY_TOLERANCE = 0.05f;

    private enum State
    {
        Idle,
        Requesting,
        Flying
    }

#if DEBUG
    /// <summary>Debug (the live check): moves the dock in the hull's frame, to fit it to the skin; zero in play.</summary>
    public static Vector3 DebugNudge { get; set; }
#endif

    private SubRoot subRoot;
    private NitroxId cyclopsId;
    private DroneTerminalScreen terminalScreen;
    private bool terminalFailed;
    private int terminalTries;
    private Transform dockRoot;
    private MapRoomCameraDocking dock;
    private MapRoomScreen screen;
    private ScoutDrone drone;
    private State state;
    private float requestedAt;
    private bool loading;
    private float nextPoll;
    private bool wasAvailable;
    private ScoutDrone appliedInstance;
    private Vector3 standLocal;
    private bool standKnown;
    private float standLoggedAt = -100f;

    /// <summary>The bay whose drone this client flies, if any.</summary>
    public static DroneBay Current { get; private set; }

    public SubRoot SubRoot => subRoot;

    /// <summary>The drone that the bay holds or sent out (null when the bay is empty).</summary>
    public ScoutDrone Drone => drone;

    public bool IsOperating => state == State.Flying;

    /// <summary>The Cyclops' entity id (null until it has one).</summary>
    public NitroxId CyclopsId
    {
        get
        {
            TryGetCyclopsId(out NitroxId id);
            return id;
        }
    }

    /// <summary>Where the dock is (its middle), in the world: the distance of the drone's signal is measured from here.</summary>
    public Vector3 DockPosition => dockRoot ? dockRoot.position : transform.position;

    /// <summary>The dock of the game that holds the drone (null while the bay is not built).</summary>
    public MapRoomCameraDocking Dock => dock;

    /// <summary>This client simulates the Cyclops (it holds the Cyclops' lock), so it is the one that recharges the drone in the dock: there is exactly one such client for a Cyclops that anybody is near.</summary>
    public bool IsRechargeAuthority => TryGetCyclopsId(out NitroxId id) && NitroxServiceLocator.LocateService<SimulationOwnership>().HasAnyLockType(id);

    /// <summary>The drone is in the dock now.</summary>
    public bool IsDocked => dock && dock.cameraDocked && dock.camera;

    private bool TryGetCyclopsId(out NitroxId id)
    {
        if (cyclopsId == null)
        {
            subRoot.TryGetNitroxId(out cyclopsId);
        }
        id = cyclopsId;
        return id != null;
    }

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        DroneSeats.Subscribe();
        DroneBayState.Subscribe();
    }

    private void OnEnable()
    {
        DroneSeats.Granted += OnGranted;
        DroneSeats.Denied += OnDenied;
        DroneSeats.Released += OnReleased;
    }

    private void OnDisable()
    {
        DroneSeats.Granted -= OnGranted;
        DroneSeats.Denied -= OnDenied;
        DroneSeats.Released -= OnReleased;
    }

    private void OnDestroy()
    {
        Exit(true);
        terminalScreen?.Destroy();
        terminalScreen = null;
    }

    private void Update()
    {
        if (state == State.Requesting && Time.time - requestedAt > ScoutDroneRules.REQUEST_TIMEOUT_SECONDS)
        {
            // The server did not answer: no seat is held for this client (a grant that comes later is given back)
            DroneSeats.CancelPending(cyclopsId);
            SetState(State.Idle);
            ErrorMessage.AddMessage("The drone console did not answer");
        }
        if (state == State.Flying)
        {
            UpdateOperating();
        }
        if (Time.unscaledTime < nextPoll)
        {
            return;
        }
        nextPoll = Time.unscaledTime + POLL_SECONDS;
        Refresh();
    }

    /// <summary>The state, mirrored to the seat table (a grant for a console that is busy is not given back).</summary>
    private void SetState(State value)
    {
        state = value;
        DroneSeats.SetBusy(cyclopsId, value != State.Idle);
    }

    // ---- the parts

    private static Vector3 Nudged((float X, float Y, float Z) position)
    {
        Vector3 result = new(position.X, position.Y, position.Z);
#if DEBUG
        result += DebugNudge;
#endif
        return result;
    }

    /// <summary>The screen on the terminal and the dock exist while the module is in; the dock sits where the layout puts it (a Debug nudge aside).</summary>
    private void Refresh()
    {
        bool available = DroneStation.IsAvailable(subRoot);
        // The marks of the drone's operator are for the whole crew: the HUD pings of this Cyclops' drone marks are made and shown to every player, aboard or not
        if (TryGetCyclopsId(out NitroxId marksCyclopsId))
        {
            NavMarks.SyncDroneMarks(marksCyclopsId, VehicleUpgradeSettings.Enabled && VehicleUpgradeSettings.Received);
        }
        if (available && terminalScreen == null && !terminalFailed)
        {
            // (the terminal may not be set up in the first moments of a Cyclops: a few tries, a second apart, before it is given up)
            terminalScreen = DroneTerminalScreen.TryBuild(subRoot, subRoot.name, LoadFromTerminal, FlyFromTerminal);
            terminalTries++;
            terminalFailed = terminalScreen == null && terminalTries >= 20;
        }
        if (available && !dock)
        {
            BuildDock();
        }
        if (dockRoot)
        {
            dockRoot.gameObject.SetActive(available);
            dockRoot.localPosition = Nudged(DroneLayout.DockCenter);
        }
        terminalScreen?.SetVisible(available);

        if (!drone)
        {
            drone = null;
        }
        if (dock && dock.camera && ScoutDrone.Of(dock.camera, out ScoutDrone docked))
        {
            drone = docked;
            drone.Bay = this;
        }
        if (available)
        {
            ApplyServerState();
        }
        if (terminalScreen != null && terminalScreen.Exists && available)
        {
            ShowOnTerminal();
        }
        if (!available)
        {
            Exit(true);
            // The drone in the dock is let go (so that it can be picked up) only when the bay really stops: the module went out after it had been in, or the server's switch is known to be off. A client that is
            // still joining has no settings yet, and the Cyclops' modules are not all there for a moment: nothing is let go of then, and the server is not told that the drone left the bay.
            if (wasAvailable || (VehicleUpgradeSettings.Received && !VehicleUpgradeSettings.Enabled))
            {
                ReleaseDrone();
            }
        }
        wasAvailable = available;
    }

    private string OperatorName()
    {
        if (TryGetCyclopsId(out NitroxId id) && DroneSeats.TryGetOperator(id, out DroneSeats.Seat seat))
        {
            return seat.Name;
        }
        return string.Empty;
    }

    private string StatusText()
    {
        if (!drone && !ServerBayHoldsADrone())
        {
            return "bay empty";
        }
        if (state == State.Flying || !drone || !IsDocked)
        {
            return "drone out";
        }
        return $"docked, {Mathf.RoundToInt(BatteryPercent())} percent";
    }

    private float BatteryPercent()
    {
        EnergyMixin energy = drone && drone.Camera ? drone.Camera.energyMixin : null;
        return energy && energy.capacity > 0f ? 100f * energy.charge / energy.capacity : 0f;
    }

    private void BuildDock()
    {
        GameObject root = new(DOCK_NAME);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Nudged(DroneLayout.DockCenter);
        // The dock's forward points out of the hull (port); a drone in the dock noses outward and leaves in that direction
        root.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        (float sx, float sy, float sz) = DroneLayout.DockTriggerSize;
        trigger.size = new Vector3(sx, sy, sz);
        root.AddComponent<DroneDockMarker>().Bay = this;

        GameObject dockPose = new("DockPose");
        dockPose.transform.SetParent(root.transform, false);
        GameObject undockPose = new("UndockPose");
        undockPose.transform.SetParent(root.transform, false);
        undockPose.transform.localPosition = new Vector3(0f, 0f, DroneLayout.UNDOCK_DISTANCE);

        // The game's dock is made for a scanner room: it creates its own camera at Start unless it believes it was loaded from a save, and the drone it holds is looked for through its triggers
        dock = root.AddComponent<MapRoomCameraDocking>();
        dock.deserialized = true;
        dock.version = 1;
        dock.dockingTransform = dockPose.transform;
        dock.undockTransform = undockPose.transform;
        dock.cameraPrefab = null;

        // The screen the drone is flown from (its position is where the signal is measured from)
        GameObject screenObject = new(SCREEN_NAME);
        screenObject.transform.SetParent(root.transform, false);
        screenObject.AddComponent<DroneScreenMarker>();
        screen = screenObject.AddComponent<MapRoomScreen>();
        dockRoot = root.transform;
        Log.Info($"[VehicleUpgrades] drone bay: dock on {subRoot.name}, hull-local {root.transform.localPosition:F2}");
    }

    // ---- what the server says

    /// <summary>
    /// Carries out what the server last said about this bay, once: which drone it holds and whether it is in the dock. The author of a change (the operator who flew the drone out or home, the player who loaded it)
    /// has done it already; the others dock the drone in their copy of the dock or let it go. A drone that has not spawned here yet is waited for.
    /// </summary>
    private void ApplyServerState()
    {
        if (!TryGetCyclopsId(out NitroxId id) || !DroneBayState.TryGetLatest(id, out CyclopsDroneState wanted))
        {
            return;
        }
        // The state is carried out once (a player's own action that is on its way to the server must not be undone by the state before it), and again whenever the drone's object is a new one on this client
        // (it spawns late, or its cell was unloaded and loaded again): the bay always ends up as the server says, however the drone came to exist here
        bool fresh = DroneBayState.TryGetUnapplied(id, out CyclopsDroneState pending) && ReferenceEquals(pending, wanted);
        if (!wanted.DroneId.HasValue)
        {
            if (fresh)
            {
                // an empty bay: the drone that was here is gone (destroyed, or picked up)
                if (drone && state == State.Flying)
                {
                    Exit(false);
                }
                drone = null;
                appliedInstance = null;
                DroneBayState.MarkApplied(wanted);
            }
            return;
        }
        if (!NitroxEntity.TryGetObjectFrom(wanted.DroneId.Value, out GameObject droneObject) || !droneObject.TryGetComponent(out ScoutDrone scout) || !scout.Camera || !dock)
        {
            return;
        }
        // Not a drone that sits in somebody's inventory (an item that is carried is switched off), and not before its own Start has run (the game's camera lets go of any dock in its Start, which would look like a pickup)
        if (!droneObject.activeInHierarchy || !scout.HasStarted)
        {
            return;
        }
        drone = scout;
        scout.Bay = this;
        if ((fresh || appliedInstance != scout) && !scout.LocalOperator)
        {
            DroneBayState.Apply(() =>
            {
                if (wanted.Docked && !(IsDocked && dock.camera == scout.Camera))
                {
                    dock.DockCamera(scout.Camera);
                }
                else if (!wanted.Docked && IsDocked && dock.camera == scout.Camera)
                {
                    dock.UndockCamera();
                }
            });
        }
        appliedInstance = scout;
        if (fresh)
        {
            DroneBayState.MarkApplied(wanted);
        }
    }

    /// <summary>The server's bay holds a drone (in the dock or out), whether or not that drone exists on this client.</summary>
    private bool ServerBayHoldsADrone()
    {
        return TryGetCyclopsId(out NitroxId id) && DroneBayState.TryGetLatest(id, out CyclopsDroneState bayState) && bayState.DroneId.HasValue;
    }

    /// <summary>The drone is the one that this bay holds: it sits in this bay's dock, or the server says the bay holds it (out flying or adrift).</summary>
    private bool BayHolds(ScoutDrone scout)
    {
        if (!scout || !scout.Camera || !scout.gameObject.activeInHierarchy)
        {
            return false;
        }
        if (IsDocked && dock.camera == scout.Camera)
        {
            return true;
        }
        return TryGetCyclopsId(out NitroxId id) && DroneBayState.TryGetLatest(id, out CyclopsDroneState bayState) && bayState.DroneId.HasValue && scout.TryGetNitroxId(out NitroxId droneId) && bayState.DroneId.Value.Equals(droneId);
    }

    /// <summary>The game's dock docked or undocked the drone of this bay because of something this player did (or the drone was picked up): tell the server.</summary>
    public void ReportDock(ScoutDrone scout, bool docked)
    {
        if (DroneBayState.Applying || !TryGetCyclopsId(out NitroxId id) || !scout.TryGetNitroxId(out NitroxId droneId))
        {
            return;
        }
        if (scout.LocalOperator)
        {
            DroneBayState.Report(docked ? CyclopsDroneBayKind.Dock : CyclopsDroneBayKind.Undock, id, droneId);
        }
        else if (!docked)
        {
            // Out of the dock without the operator's control: somebody picked it up (or the module went out)
            DroneBayState.Report(CyclopsDroneBayKind.Unload, id, droneId);
        }
    }

    /// <summary>A drone that was out was picked up by a player: it is out of the bay for good, the server is told (not while this client flies it, and not when the bay carries out what the server said).</summary>
    public void ReportPickedUp(ScoutDrone scout)
    {
        if (DroneBayState.Applying || scout.LocalOperator || !TryGetCyclopsId(out NitroxId id) || !scout.TryGetNitroxId(out NitroxId droneId))
        {
            return;
        }
        DroneBayState.Report(CyclopsDroneBayKind.Unload, id, droneId);
    }

    // ---- the screen on the terminal

    /// <summary>The load bubble on the left screen of the terminal was clicked: a Scout Drone from the inventory goes into the bay (when the bay holds none).</summary>
    public void LoadFromTerminal()
    {
        if (state != State.Idle || loading)
        {
            return;
        }
        if (BayHolds(drone))
        {
            ErrorMessage.AddMessage("The Scout Drone is in the bay");
            return;
        }
        if (ServerBayHoldsADrone())
        {
            ErrorMessage.AddMessage("The Scout Drone is out of reach");
            return;
        }
        Load();
    }

    /// <summary>The fly bubble was clicked: the player flies the bay's drone, wherever it is (in the dock, out flying or adrift; the leash only lets it come back).</summary>
    public void FlyFromTerminal()
    {
        if (state != State.Idle || loading)
        {
            return;
        }
        if (BayHolds(drone))
        {
            Enter();
            return;
        }
        ErrorMessage.AddMessage(ServerBayHoldsADrone() ? "The Scout Drone is out of reach" : "The bay is empty: load a Scout Drone first");
    }

    /// <summary>What the engine's console did with one click (kept for the live check, which cannot aim at a bubble): fly the bay's drone, or load one when the bay is empty.</summary>
    public void UseConsole()
    {
        if (BayHolds(drone))
        {
            FlyFromTerminal();
        }
        else
        {
            LoadFromTerminal();
        }
    }

    /// <summary>What the left screen shows (called with every refresh of the bay).</summary>
    private void ShowOnTerminal()
    {
        bool holds = BayHolds(drone);
        bool serverHolds = ServerBayHoldsADrone();
        bool loaded = holds || serverHolds;
        bool busy = state != State.Idle || loading;
        bool takenByOther = TryGetCyclopsId(out NitroxId id) && DroneSeats.IsTakenByOther(id);
        bool canFly = holds && !busy && !takenByOther;
        string loadHover = loaded ? Language.main.Get("Nitrox_DroneBubbleLoaded") : FindDroneItem() != null ? Language.main.Get("Nitrox_DroneConsoleLoad") : Language.main.Get("Nitrox_DroneConsoleEmpty");
        string flyHover = holds ? Language.main.Get("Nitrox_DroneConsoleFly") : serverHolds ? Language.main.Get("Nitrox_DroneConsoleAway") : Language.main.Get("Nitrox_DroneBubbleNothingToFly");
        string flyHint = string.Empty;
        if (takenByOther && DroneSeats.TryGetOperator(id, out DroneSeats.Seat seat))
        {
            flyHint = string.Format(Language.main.Get("Nitrox_DroneConsoleOccupied"), seat.Name);
        }
        terminalScreen.Show(StatusText(), OperatorName(), loaded, canFly, loadHover, string.Empty, flyHover, flyHint);
    }

    private static Pickupable FindDroneItem()
    {
        Inventory inventory = Inventory.main;
        IList<InventoryItem> items = inventory != null && inventory.container != null ? inventory.container.GetItems(DroneStation.DroneTech) : null;
        return items != null && items.Count > 0 ? items[0].item : null;
    }

    /// <summary>Puts a Scout Drone from the inventory into the dock (the item is dropped at the dock and the dock takes it).</summary>
    private void Load()
    {
        if (!dock)
        {
            return;
        }
        Pickupable item = FindDroneItem();
        if (item == null)
        {
            ErrorMessage.AddMessage("The bay is empty: craft a Scout Drone at the Cyclops Fabricator");
            return;
        }
        if (!Inventory.main.TryRemoveItem(item))
        {
            return;
        }
        item.Drop(dock.dockingTransform.position, Vector3.zero, false);
        StartCoroutine(DockWhenStarted(item.GetComponent<MapRoomCamera>()));
    }

    private IEnumerator DockWhenStarted(MapRoomCamera camera)
    {
        loading = true;
        // The drone's own Start (it registers the camera and lets go of any dock) runs in the frame after it is dropped
        yield return null;
        yield return null;
        loading = false;
        if (camera && dock)
        {
            DroneBayState.Apply(() => dock.DockCamera(camera));
            if (ScoutDrone.Of(camera, out ScoutDrone loaded))
            {
                drone = loaded;
                drone.Bay = this;
                Log.Info($"[VehicleUpgrades] drone bay: a Scout Drone is in the bay of {subRoot.name}");
                if (TryGetCyclopsId(out NitroxId id) && loaded.TryGetNitroxId(out NitroxId droneId))
                {
                    DroneBayState.Report(CyclopsDroneBayKind.Load, id, droneId);
                }
            }
        }
    }

    public bool CanEnter(out string why)
    {
        why = null;
        Player player = Player.main;
        if (!subRoot || subRoot.subDestroyed)
        {
            why = "the Cyclops is destroyed";
        }
        else if (!DroneStation.IsAvailable(subRoot))
        {
            why = "the Cyclops has no Cyclops Drone Bay, or the vehicle upgrades are off";
        }
        else if (state != State.Idle || GunnerConsole.AnyConsoleOpen)
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
        else if (!BayHolds(drone))
        {
            why = "the bay's drone is not here";
        }
        else if (!drone.HasCharge)
        {
            why = "the drone's battery is flat";
        }
        else if (!TryGetCyclopsId(out _) || !drone.TryGetNitroxId(out _))
        {
            why = "the Cyclops or the drone has no id yet";
        }
        return why == null;
    }

    /// <summary>The player used the console with a drone in the dock: ask the server for the seat. The drone is flown when the server grants it.</summary>
    public void Enter()
    {
        if (!CanEnter(out string why))
        {
            Log.Info($"[VehicleUpgrades] drone bay: cannot be used now: {why}");
            ErrorMessage.AddMessage(why.StartsWith("the drone's battery") ? "The drone's battery is flat: it recharges in the bay" : "The drone console cannot be used now");
            return;
        }
        SetState(State.Requesting);
        requestedAt = Time.time;
        DroneSeats.Request(cyclopsId);
        Log.Info($"[VehicleUpgrades] drone bay: asked the server for the seat on {subRoot.name}");
    }

    private void OnGranted(DroneSeatGrant grant)
    {
        if (state != State.Requesting || cyclopsId == null || !cyclopsId.Equals(grant.CyclopsId))
        {
            return;
        }
        if (!CanBeginFlight(out string why))
        {
            // The player moved on while the server was answering: the seat is not kept (the table gives it back)
            Log.Info($"[VehicleUpgrades] drone bay: the seat was granted but is not wanted any more: {why}");
            SetState(State.Idle);
            return;
        }
        grant.Accepted = true;
        BeginFlight();
    }

    private bool CanBeginFlight(out string why)
    {
        why = null;
        Player player = Player.main;
        if (!subRoot || subRoot.subDestroyed || !DroneStation.IsAvailable(subRoot))
        {
            why = "the bay is out";
        }
        else if (!player || !player.IsAlive() || player.currentSub != subRoot || player.GetMode() != Player.Mode.Normal)
        {
            why = "the player is gone, dead or busy";
        }
        else if (!BayHolds(drone))
        {
            why = "the drone is not the bay's";
        }
        return why == null;
    }

    private void OnDenied(CyclopsDroneSeat packet)
    {
        if (state == State.Requesting && cyclopsId != null && cyclopsId.Equals(packet.CyclopsId))
        {
            SetState(State.Idle);
            ErrorMessage.AddMessage(DroneSeats.DeniedText(packet));
            Log.Info($"[VehicleUpgrades] drone bay: the seat was refused: {packet.Reason}{(packet.Reason == CyclopsDroneSeatReason.Occupied ? $" ({packet.PlayerName} sits there)" : string.Empty)}");
        }
    }

    private void OnReleased(CyclopsDroneSeat packet)
    {
        if (state != State.Flying || cyclopsId == null || !cyclopsId.Equals(packet.CyclopsId) || packet.SessionId != DroneSeats.LocalSession)
        {
            return;
        }
        string text = DroneSeats.ReleasedText(packet);
        if (!string.IsNullOrEmpty(text))
        {
            ErrorMessage.AddMessage(text);
        }
        Log.Info($"[VehicleUpgrades] drone bay: the server ended the seat ({packet.Reason})");
        Exit(false);
    }

    /// <summary>The seat is this player's: take the drone's lock (which makes this client the one that moves it) and fly it with the game's own control, from the screen of the dock.</summary>
    private void BeginFlight()
    {
        MapRoomCamera camera = drone.Camera;
        drone.TryGetNitroxId(out NitroxId droneId);
        drone.LocalOperator = true;
        // the body stays where the player used the terminal (the game's control takes the camera away from the body, and the body must not go with anything): its place in the hull's frame is kept
        standLocal = transform.InverseTransformPoint(Player.main.transform.position);
        standKnown = true;
        Log.Info($"[VehicleUpgrades] drone bay: the body of {LocalName()} stays at hull-local {standLocal:F2} while the drone flies");
        screen.currentCamera = camera;
        camera.ControlCamera(screen);
        drone.HideOwnModel();
        InputHandlerStack.main.Push(screen);
        SetState(State.Flying);
        Current = this;
        NitroxServiceLocator.LocateService<SimulationOwnership>().RequestSimulationLock(new LockRequest<DroneLock>(droneId, SimulationLockType.EXCLUSIVE, OnLockResponse, new DroneLock(this)));
        Log.Info($"[VehicleUpgrades] drone bay: {LocalName()} flies the Scout Drone of {subRoot.name}");
    }

    private static void OnLockResponse(NitroxId id, bool acquired, DroneLock context)
    {
        if (!acquired && context.Bay && context.Bay.state == State.Flying)
        {
            ErrorMessage.AddMessage("Somebody else holds the Scout Drone");
            Log.Info($"[VehicleUpgrades] drone bay: the exclusive lock of the drone {id} was refused");
            context.Bay.Exit(true);
        }
    }

    private static string LocalName() => NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName ?? string.Empty;

    private void LateUpdate()
    {
        if (state == State.Flying)
        {
            KeepBodyAtTheTerminal();
        }
    }

    /// <summary>
    /// The player's body is at the place where the terminal was used, in the hull's frame, whatever else moves it while the drone flies (it was found in the middle of the ship, in the decoy launcher room, instead): every
    /// frame it is put back when it has moved, and the pawn that walks the player through the virtual Cyclops is told where the body is, so that nothing writes the old place back. The others see the body where it
    /// stands, because the movement broadcast reads its transform.
    /// </summary>
    private void KeepBodyAtTheTerminal()
    {
        Player player = Player.main;
        if (!standKnown || !player || player.currentSub != subRoot)
        {
            return;
        }
        Vector3 wanted = transform.TransformPoint(standLocal);
        float drift = Vector3.Distance(player.transform.position, wanted);
        if (drift <= BODY_TOLERANCE)
        {
            return;
        }
        if (Time.unscaledTime - standLoggedAt > 5f)
        {
            standLoggedAt = Time.unscaledTime;
            Log.Info($"[VehicleUpgrades] drone bay: the body had moved {drift:F2} m from the terminal while the drone flies: put back at hull-local {standLocal:F2}");
        }
        player.transform.position = wanted;
        if (player.TryGetComponent(out CyclopsMotor motor) && motor.Pawn != null)
        {
            motor.Pawn.SetReference();
        }
        // The camera rig hangs on the body: it went along with it, and the game's own placing of it at the drone (the end of MapRoomCamera.HandleInput, earlier in this frame) is undone. Done again, so
        // that the view is exactly the drone's (an earlier build showed the drone's own top and rear in its view).
        if (drone && drone.Camera && drone.Camera.active && SNCameraRoot.main)
        {
            SNCameraRoot.main.transform.SetPositionAndRotation(drone.transform.position, drone.transform.rotation);
        }
    }

    private void UpdateOperating()
    {
        // The game's own exit (the exit key, the death of the player, the death of the drone) ends the control; this follows
        if (!drone || !drone.Camera || !drone.Camera.active)
        {
            FinishOperating(true);
            return;
        }
        // The keys the game's drone does not use: the left hand button marks what the view is aimed at, reload marks it as the other kind of mark, the alternate tool zaps
        bool mark = GameInput.GetButtonDown(GameInput.Button.LeftHand);
        bool markOther = GameInput.GetButtonDown(GameInput.Button.Reload);
        if (mark || markOther)
        {
            DroneMarker.TryMark(this, markOther, out string markMessage);
            if (markMessage != null)
            {
                ErrorMessage.AddMessage(markMessage);
            }
        }
        if (GameInput.GetButtonDown(GameInput.Button.AltTool))
        {
            DroneZap.TryRequest(this, out string zapMessage);
            if (zapMessage != null)
            {
                ErrorMessage.AddMessage(zapMessage);
            }
        }
    }

    /// <summary>Ends the control of the drone, whatever the reason (module out, Cyclops gone, the server ended the seat). Safe to call at any time and more than once.</summary>
    public void Exit(bool tellServer)
    {
        if (state == State.Idle)
        {
            return;
        }
        if (state == State.Requesting)
        {
            DroneSeats.CancelPending(cyclopsId);
            SetState(State.Idle);
            return;
        }
        MapRoomCamera camera = drone ? drone.Camera : null;
        if (camera && camera.active)
        {
            // the control hands the body back where it stands now: that is the terminal
            KeepBodyAtTheTerminal();
            camera.FreeCamera(true);
            camera.ExitLockedMode(true);
        }
        FinishOperating(tellServer);
    }

    private void FinishOperating(bool tellServer)
    {
        if (state != State.Flying)
        {
            return;
        }
        standKnown = false;
        NitroxId droneId = null;
        if (drone)
        {
            drone.ShowOwnModel();
            drone.LocalOperator = false;
            drone.TryGetNitroxId(out droneId);
        }
        SetState(State.Idle);
        if (Current == this)
        {
            Current = null;
            Log.Info($"[VehicleUpgrades] drone bay: the control of the Scout Drone of {(subRoot ? subRoot.name : "a destroyed Cyclops")} ended");
        }
        if (tellServer && cyclopsId != null)
        {
            DroneSeats.Leave(cyclopsId);
        }
        if (droneId != null)
        {
            // The exclusive lock is let go of (a transient one stays with this client until the server gives it to a player who is closer)
            NitroxServiceLocator.LocateService<SimulationOwnership>().RequestSimulationLock(droneId, SimulationLockType.TRANSIENT);
        }
    }

    /// <summary>The module went out (or the upgrades were switched off): a drone in the dock is let go, so that it can be picked up.</summary>
    private void ReleaseDrone()
    {
        if (drone && IsDocked)
        {
            dock.UndockCamera();
        }
    }
}
