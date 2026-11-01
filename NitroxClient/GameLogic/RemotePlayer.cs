using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using NitroxClient.GameLogic.HUD;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Equipment.Abstract;
using NitroxClient.GameLogic.PlayerModels;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using NitroxClient.MonoBehaviours.Gui.HUD;
using NitroxClient.MonoBehaviours.Vehicles;
using Nitrox.Model.GameLogic.FMOD;
using Nitrox.Model.GameLogic.PlayerAnimation;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.MultiplayerSession;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

public class RemotePlayer : INitroxPlayer
{
    /// <summary>
    /// Marks <see cref="Player.mainObject"/> and every <see cref="Body"/> so they can be precisely queried (e.g. by sea dragons).
    /// The value (5050) is determined arbitrarily and should not be used already.
    /// </summary>
    public const EcoTargetType PLAYER_ECO_TARGET_TYPE = (EcoTargetType)5050;

    private static readonly int animatorPlayerIn = Animator.StringToHash("player_in");

    private readonly PlayerModelManager playerModelManager;
    private readonly PlayerVitalsManager playerVitalsManager;
    private readonly FMODWhitelist fmodWhitelist;
    private List<IEquipmentVisibilityHandler> equipmentVisibilityHandlers = [];

    public PlayerContext PlayerContext { get; }
    public GameObject? Body { get; private set; }
    public GameObject PlayerModel { get; private set; }
    public Rigidbody RigidBody { get; private set; }
    public RemotePlayerMovementReplicator MovementReplicator { get; private set; }
    public CapsuleCollider Collider { get; private set; }
    public ArmsController ArmsController { get; private set; }
    public AnimationController AnimationController { get; private set; }
    public ItemsContainer Inventory { get; private set; }
    public Transform ItemAttachPoint { get; private set; }
    public RemotePlayerVitals vitals { get; private set; }

    /// <summary>
    /// The last health, food and water that this player sent (the <c>PlayerStats</c> packet, every 3 s) and when it came (this client's unscaled time). The bars of <see cref="vitals" /> only
    /// take the values in, nothing can read them back, and they do not exist before the body does: the player list shows these.
    /// </summary>
    public PlayerListStatsRules.VitalsSample LastVitals { get; private set; }

    public SessionId SessionId => PlayerContext.SessionId;
    public string PlayerName => PlayerContext.PlayerName;
    public PlayerSettings PlayerSettings => PlayerContext.PlayerSettings;

    public Vehicle Vehicle { get; private set; }
    public SubRoot SubRoot { get; private set; }
    public EscapePod? EscapePod { get; private set; }
    public PilotingChair PilotingChair { get; private set; }

    /// <summary>The rider bar of the Seamoth this player holds on to (the body is a child of the bar's anchor), or null.</summary>
    public SeamothRiderBar RiderBar { get; private set; }
    public InfectedMixin InfectedMixin { get; private set; }
    public LiveMixin LiveMixin { get; private set; }

    public readonly Event<RemotePlayer> PlayerDeathEvent = new();

    public readonly Event<RemotePlayer> PlayerDisconnectEvent = new();

    public CyclopsPawn Pawn { get; set; }

    /// <summary>
    /// Whether the copy is drawn in the swimming or in the walking pose (bug 6): the last state the player reported, kept whether the body exists or not, and the pawn of a Cyclops.
    /// </summary>
    public RemotePoseTracker PoseTracker { get; } = new();

    /// <summary>
    /// Where a player who pilots a <see cref="PilotingChair" /> has his root, relative to <c>PilotingChair.sittingPosition</c> (vanilla: local position (0, 1, 0)).
    /// </summary>
    private static readonly Vector3 PILOT_ROOT_OFFSET_IN_CHAIR = new(0f, 1f, 0f);

    public RemotePlayer(PlayerContext playerContext, PlayerModelManager playerModelManager, PlayerVitalsManager playerVitalsManager, FMODWhitelist fmodWhitelist)
    {
        PlayerContext = playerContext;
        this.playerModelManager = playerModelManager;
        this.playerVitalsManager = playerVitalsManager;
        this.fmodWhitelist = fmodWhitelist;
    }

    public void InitializeGameObject(GameObject playerBody)
    {
        Body = playerBody;
        Body.name = PlayerName;

        RigidBody = Body.AddComponent<Rigidbody>();
        RigidBody.useGravity = false;
        RigidBody.interpolation = RigidbodyInterpolation.Interpolate;
        MovementReplicator = Body.AddComponent<RemotePlayerMovementReplicator>();

        NitroxEntity.SetNewId(Body, PlayerContext.PlayerNitroxId);

        // Get player
        PlayerModel = Body.RequireGameObject("player_view");
        // Move variables to keep player animations from mirroring and for identification
        ArmsController = PlayerModel.GetComponent<ArmsController>();
        ArmsController.smoothSpeedUnderWater = 0;
        ArmsController.smoothSpeedAboveWater = 0;

        // ConditionRules has Player.Main based conditions from ArmsController
        PlayerModel.GetComponent<ConditionRules>().enabled = false;

        AnimationController = PlayerModel.AddComponent<AnimationController>();
        MovementReplicator.Initialize(AnimationController);
        MovementReplicator.Owner = this;

        Transform inventoryTransform = new GameObject("Inventory").transform;
        inventoryTransform.SetParent(Body.transform);
        Inventory = new ItemsContainer(6, 8, inventoryTransform, $"NitroxInventoryStorage_{PlayerName}", null);

        ItemAttachPoint = PlayerModel.transform.Find(PlayerEquipmentConstants.ITEM_ATTACH_POINT_GAME_OBJECT_NAME);

        CoroutineUtils.StartCoroutineSmart(playerModelManager.AttachPing(this));
        playerModelManager.BeginApplyPlayerColor(this);
        equipmentVisibilityHandlers = playerModelManager.RegisterEquipmentVisibilityHandler(PlayerModel);
        SetupBody();
        SetupSkyAppliers();
        SetupPlayerSounds();
        SetupMixins();

        PlayerAnimation animation = PlayerContext.Animation;
        // The animation the server sent when he joined is only a start value: an event of the player that came before this body existed is in PoseTracker already and is newer
        PoseTracker.SeedFromJoin((AnimChangeType)animation.Type, (AnimChangeState)animation.State);
        // A new body has no pawn yet, the first pawn packet makes it a walker
        PoseTracker.SetWalkingInHull(false, Time.unscaledTime);
        ApplyInfection((AnimChangeType)animation.Type, (AnimChangeState)animation.State);
        ApplyPose();

        vitals = playerVitalsManager.CreateOrFindForPlayer(this);
        RefreshVitalsVisibility();

        PlayerDisconnectEvent.AddHandler(Body, _ =>
        {
            Pawn?.Unregister();
            Pawn = null;
        });

        PlayerDeathEvent.AddHandler(Body, _ =>
        {
            ResetStates();
        });

        // The custom player model that this player chose (docs/coop/PLAYER_MODELS.md) goes on the new body
        PlayerModelChoices.OnBodyReady(this);
    }

    public void Attach(Transform transform, bool keepWorldTransform = false)
    {
        // The movement snapshots are in world space: the old ones must not move the body any more now that something else positions it
        MovementReplicator.Reset();
        Body.transform.SetParent(transform);

        if (!keepWorldTransform)
        {
            UWE.Utils.ZeroTransform(Body);
        }

        SkyEnvironmentChanged.Broadcast(Body, transform);
    }

    public void Detach()
    {
        MovementReplicator.Reset();
        Body.transform.SetParent(null);
        SkyEnvironmentChanged.Broadcast(Body, (GameObject)null);
    }

    public void UpdatePosition(Vector3 position, Vector3 velocity, Quaternion bodyRotation, Quaternion aimingRotation, double time)
    {
        // It might happen that we get movement packets before the body is actually initialized which is not too bad
        if (!Body)
        {
            return;
        }

        // A rider holds on to the bar of a Seamoth: his body is a child of the bar and moves with it, a world position of his (one that was on its way when he climbed on) would pull it off
        if (RiderBar)
        {
            return;
        }

        Body.SetActive(true);

        // When receiving movement packets, a player can not be controlling a vehicle (they can walk through subroots though).
        SetVehicle(null);
        SetPilotingChair(null);

        // A world movement of a player who has no pawn: the pose he last reported decides again
        if (Pawn == null)
        {
            SetWalkingInHull(false);
        }

        AnimationController.AimingRotation = aimingRotation;
        AnimationController.UpdatePlayerAnimations = true;

        if (PlayerMovementTuning.Extrapolate)
        {
            // The body is moved every frame from the snapshots (RemotePlayerMovementReplicator), the animation follows the velocity the player really has
            if (!RigidBody.isKinematic)
            {
                UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, true, true);
            }
            AnimationController.Velocity = velocity;
            MovementReplicator.AddSnapshot(time, position, velocity, bodyRotation, aimingRotation);
            return;
        }

        AnimationController.Velocity = MovementHelper.GetCorrectedVelocity(position, velocity, Body, Time.fixedDeltaTime);

        RigidBody.velocity = AnimationController.Velocity;
        RigidBody.angularVelocity = MovementHelper.GetCorrectedAngularVelocity(bodyRotation, Vector3.zero, Body, Time.fixedDeltaTime);
    }

    public void UpdatePositionInCyclops(Vector3 localPosition, Vector3 localVelocity, Quaternion localRotation, double time)
    {
        if (Pawn == null || PilotingChair || RiderBar)
        {
            return;
        }

        SetVehicle(null);

        // A pawn walks, whatever the last animation event said (bug 6: a copy in the swimming pose has its legs about a meter above the floor), and its body is moved by the replicator
        // (a child of the hull), never by physics: SetPilotingChair and SetVehicle make it dynamic and nothing makes it kinematic again inside a Cyclops
        SetWalkingInHull(true);
        if (RigidBody && !RigidBody.isKinematic)
        {
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, true, true);
        }

        AnimationController.AimingRotation = localRotation;
        AnimationController.UpdatePlayerAnimations = true;

        if (PlayerMovementTuning.Extrapolate)
        {
            // The pawn is moved every frame from the snapshots (RemotePlayerMovementReplicator, in the space of the Cyclops). The animation wants the velocity in world space.
            Transform cyclopsTransform = Body.transform.parent;
            AnimationController.Velocity = cyclopsTransform ? cyclopsTransform.rotation * localVelocity : localVelocity;
            MovementReplicator.AddLocalSnapshot(time, localPosition, localVelocity, localRotation);
            return;
        }

        AnimationController.Velocity = (localPosition - Pawn.Handle.transform.localPosition) / Time.fixedDeltaTime;

        Pawn.Handle.transform.localPosition = localPosition;
        Pawn.Handle.transform.localRotation = localRotation;
    }

    public void SetPilotingChair(PilotingChair newPilotingChair)
    {
        if (PilotingChair != newPilotingChair)
        {
            PilotingChair = newPilotingChair;
            MovementReplicator.Reset();

            CyclopsMovementReplicator cyclopsMovementReplicator = null;

            // For unexpected and expected cases, for example when a player is driving a cyclops but the cyclops is destroyed
            if (!SubRoot)
            {
                Log.Error("Player changed PilotingChair but is not in SubRoot!");
            }
            else
            {
                cyclopsMovementReplicator = SubRoot.gameObject.EnsureComponent<CyclopsMovementReplicator>();
            }

            if (PilotingChair)
            {
                Attach(PilotingChair.sittingPosition.transform);
                // The local player sits with his root 1 m above this locator (vanilla parents Player.main to it at local position (0, 1, 0), measured with the harness command pilotdump):
                // a body at the locator itself was rendered 1 m too low, its head at seat height and the hips under the seat
                Body.transform.localPosition = PILOT_ROOT_OFFSET_IN_CHAIR;
                ArmsController.SetWorldIKTarget(PilotingChair.leftHandPlug, PilotingChair.rightHandPlug);

                if (cyclopsMovementReplicator)
                {
                    cyclopsMovementReplicator.Enter(this);
                }

                if (SubRoot)
                {
                    SkyEnvironmentChanged.Broadcast(Body, SubRoot);
                }

                AnimationController.UpdatePlayerAnimations = false;
            }
            else
            {
                SetSubRoot(SubRoot, true);
                ArmsController.SetWorldIKTarget(null, null);

                if (cyclopsMovementReplicator)
                {
                    cyclopsMovementReplicator.Exit();
                }
            }

            bool isKinematic = newPilotingChair;
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, isKinematic, true);
            AnimationController["cyclops_steering"] = newPilotingChair;
        }
    }

    public void SetSubRoot(SubRoot newSubRoot, bool force = false)
    {
        if (SubRoot != newSubRoot || force)
        {
            // Unregister from previous cyclops
            Pawn?.Unregister();
            Pawn = null;
            // No pawn, no walker: the pose he reported is his again (a new pawn makes him a walker with its first movement)
            SetWalkingInHull(false);

            if (newSubRoot)
            {
                Attach(newSubRoot.transform, true);

                // Register in new cyclops
                if (newSubRoot.TryGetComponent(out NitroxCyclops nitroxCyclops))
                {
                    nitroxCyclops.OnPlayerEnter(this);
                }
            }
            else
            {
                Detach();
            }

            SubRoot = newSubRoot;
        }
    }

    /// <summary>
    /// Puts the body on the bar of a Seamoth (the rider bar) or takes it off. The body becomes a child of the bar's anchor, kinematic, with its hands on the bar, so it moves with the Seamoth exactly
    /// as the Seamoth is drawn on this client; it keeps swimming animations (a rider is not seated) and looks where the rider's look packets say. Taking it off leaves it in the water where it is:
    /// the next movement packet of the player places it.
    /// </summary>
    public void SetBarRider(SeamothRiderBar newBar)
    {
        if (RiderBar == newBar)
        {
            return;
        }

        if (RiderBar)
        {
            ArmsController.SetWorldIKTarget(null, null);
            Detach();
        }
        RiderBar = newBar;

        if (newBar)
        {
            // Not in a vehicle, a chair or a hull any more: he holds on to the outside of a Seamoth
            SetVehicle(null);
            SetPilotingChair(null);
            Body.SetActive(true);
            Attach(newBar.Anchor);
            ArmsController.SetWorldIKTarget(newBar.LeftHand, newBar.RightHand);
            if (RigidBody && !RigidBody.isKinematic)
            {
                UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, true, true);
            }
            AnimationController.Velocity = Vector3.zero;
            AnimationController.UpdatePlayerAnimations = true;
        }
        PoseTracker.SetHoldingBar(newBar);
        ApplyPose();
    }

    public void SetEscapePod(EscapePod? newEscapePod)
    {
        if (EscapePod != newEscapePod)
        {
            if (newEscapePod)
            {
                Attach(newEscapePod.transform, true);
            }
            else
            {
                Detach();
            }

            EscapePod = newEscapePod;
        }
    }

    public void SetVehicle(Vehicle newVehicle)
    {
        if (Vehicle != newVehicle)
        {
            MovementReplicator.Reset();
            if (Vehicle)
            {
                Vehicle.mainAnimator.SetBool(animatorPlayerIn, false);

                Detach();
                ArmsController.SetWorldIKTarget(null, null);

                if (Vehicle.TryGetComponent(out VehicleMovementReplicator vehicleMovementReplicator))
                {
                    vehicleMovementReplicator.Exit();
                }
            }

            if (newVehicle)
            {
                newVehicle.mainAnimator.SetBool(animatorPlayerIn, true);

                Attach(newVehicle.playerPosition.transform);
                ArmsController.SetWorldIKTarget(newVehicle.leftHandPlug, newVehicle.rightHandPlug);

                // From here, a basic issue can happen.
                // When a vehicle is docked since we joined a game and another player undocks him before the local player does,
                // no VehicleMovementReplicator can be found on the vehicle because they are only created when receiving SimulationOwnership packets
                // Therefore we need to make sure that the VehicleMovementReplicator component exists before using it
                switch (newVehicle)
                {
                    case SeaMoth:
                        newVehicle.gameObject.EnsureComponent<SeamothMovementReplicator>().Enter(this);
                        break;
                    case Exosuit:
                        newVehicle.gameObject.EnsureComponent<ExosuitMovementReplicator>().Enter(this);
                        break;
                }

                AnimationController.UpdatePlayerAnimations = false;
            }

            bool isKinematic = newVehicle;
            UWE.Utils.SetIsKinematicAndUpdateInterpolation(RigidBody, isKinematic, true);

            Vehicle = newVehicle;

            AnimationController["in_seamoth"] = newVehicle is SeaMoth;
            AnimationController["in_exosuit"] = AnimationController["using_mechsuit"] = newVehicle is Exosuit;

            // In case we are dismissing the current seamoth to enter the cyclops through a docking,
            // we need to setup the player back in the cyclops
            if (!newVehicle && SubRoot)
            {
                SetSubRoot(SubRoot, true);
            }
        }
    }

    /// <summary>
    /// Drops the remote player, swimming where he is. Resets its animator.
    /// </summary>
    public void ResetStates()
    {
        SetBarRider(null);
        SetPilotingChair(null);
        SetVehicle(null);
        SetSubRoot(null);
        HaltMotion();
        AnimationController.UpdatePlayerAnimations = true;
        AnimationController.Reset();
        // The rebind gave every parameter of the animator its default back: the pose he reported is his again
        ApplyPose();
        ArmsController.SetWorldIKTarget(null, null);
    }

    /// <summary>
    /// Nothing may carry the body on once the player stops sending (death, freecam): the speed the animation follows is only written by movement packets, the velocity of the rigidbody
    /// is only given one by the debug path without the replicator (<see cref="PlayerMovementTuning.Extrapolate" />, the body is kinematic otherwise and has none).
    /// </summary>
    private void HaltMotion()
    {
        AnimationController.Velocity = Vector3.zero;

        if (RigidBody && !RigidBody.isKinematic)
        {
            RigidBody.velocity = Vector3.zero;
            RigidBody.angularVelocity = Vector3.zero;
        }
    }

    public void Destroy()
    {
        Log.Info($"{PlayerName} left the game");
        Log.InGame(Language.main.Get("Nitrox_PlayerLeft").Replace("{PLAYER}", PlayerName));
        ResetStates();
        // Its colliders vanish without leaving the Precursor pedestals and terminals it was standing in
        TriggerOccupancy.PlayerGone(Body);
        NitroxEntity.RemoveFrom(Body);
        Object.DestroyImmediate(Body);
    }

    public void UpdateAnimationAndCollider(AnimChangeType type, AnimChangeState state)
    {
        // Asked before the report is taken in: how long this player had walked in a Cyclops when this is the report that ends the swimming state he was walking in (NaN: it is not)
        float endedAfter = PoseTracker.SwimmingEndedWhileWalking(type, state, Time.unscaledTime);

        // An event that comes before the body exists (a player who just joined) is not lost: PoseTracker keeps it and the body starts from it
        PoseTracker.Report(type, state);
        ApplyInfection(type, state);
        ApplyPose();

        // The evidence of bug 6 for a stale state, in the log of a real session: a state of the player that said swimming, was corrected only now, and not by the first message after the
        // boarding (that is the race of the two messages of the boarding, which sits below PoseRefreshRules.LATE_AFTER). Without the pawn rule the copy would have been drawn in the
        // swimming pose, a meter too high, for this long
        if (PoseRefreshRules.IsLateCorrection(endedAfter))
        {
            Log.Info($"[PassengerHeight] {PlayerName}: the swimming state he reported ended {endedAfter:F1} s after he began to walk in {(SubRoot ? SubRoot.name : "a Cyclops")}, not in the boarding race (limit {PoseRefreshRules.LATE_AFTER:F1} s): a stale or late state, corrected now (the sender announces its state again every {PoseRefreshRules.REFRESH_INTERVAL:F0} s)");
        }
    }

    private void ApplyInfection(AnimChangeType type, AnimChangeState state)
    {
        if (type == AnimChangeType.INFECTION_REVEAL && AnimationController)
        {
            AnimationController["player_infected"] = state != AnimChangeState.UNSET;
        }
    }

    /// <summary>
    /// Writes the pose of <see cref="PoseTracker" /> into the animator and the collider: swimming, unless the player walks in a Cyclops (bug 6, <see cref="RemotePoseTracker" />). Does nothing
    /// before the body exists, the next call after it does.
    /// </summary>
    private void ApplyPose()
    {
        if (!AnimationController)
        {
            return;
        }

        bool swimming = PoseTracker.Swimming;
        AnimationController["is_underwater"] = swimming;

        // Rough estimation for different collider boxes in different animation stages
        if (Collider)
        {
            if (swimming)
            {
                Collider.center = new Vector3(0f, -0.3f, 0f);
                Collider.height = 0.5f;
            }
            else
            {
                Collider.center = new Vector3(0f, -0.8f, 0f);
                Collider.height = 1.5f;
            }
        }
    }

    /// <summary>
    /// The latest movement of the player is that of a pawn in a Cyclops (<paramref name="walking" />) or any other: his pose is that of a walker for as long as he is one, and the one he
    /// last reported when he is not.
    /// </summary>
    private void SetWalkingInHull(bool walking)
    {
        if (PoseTracker.WalkingInHull == walking)
        {
            return;
        }

        // No line here: the first pawn packet of a player who climbs in out of the water comes while his state still says swimming (the state change is a separate message of the same
        // client, in no defined order), so a copy that "was swimming" at this moment is the normal boarding, not evidence of anything. PassengerHeightWatch writes the line for a state
        // that stays (RemotePoseTracker.IsStuckUnderwater), UpdateAnimationAndCollider the one for a state that was corrected late
        PoseTracker.SetWalkingInHull(walking, Time.unscaledTime);
        ApplyPose();
    }

    /// <summary>
    /// Hangs the items that this player holds on another transform: the hand of a custom player model (docs/coop/PLAYER_MODELS.md). Passing the vanilla attach point again gives them back.
    /// </summary>
    public void SetItemAttachPoint(Transform attachPoint)
    {
        ItemAttachPoint = attachPoint;
    }

    public void UpdateEquipmentVisibility(List<TechType> equippedItems)
    {
        PlayerModelManager.UpdateEquipmentVisibility(equipmentVisibilityHandlers, equippedItems);
    }

    /// <summary>
    /// Makes the RemotePlayer recognizable as an obstacle for buildings, and as a target for creatures
    /// </summary>
    private void SetupBody()
    {
        // set as a target for reapers
        EcoTarget sharkEcoTarget = Body.AddComponent<EcoTarget>();
        sharkEcoTarget.SetTargetType(EcoTargetType.Shark);

        EcoTarget playerEcoTarget = Body.AddComponent<EcoTarget>();
        playerEcoTarget.SetTargetType(PLAYER_ECO_TARGET_TYPE);

        TechTag techTag = Body.AddComponent<TechTag>();
        techTag.type = TechType.Player;

        RemotePlayerIdentifier identifier = Body.AddComponent<RemotePlayerIdentifier>();
        identifier.RemotePlayer = this;

        if (Player.mainCollider is CapsuleCollider refCollider)
        {
            // This layer lets us have a collider as a trigger without preventing its detection as an obstacle
            Body.layer = LayerID.Useable;
            Collider = Body.AddComponent<CapsuleCollider>();

            Collider.center = Vector3.zero;
            Collider.height = refCollider.height;
            Collider.radius = refCollider.radius;
            Collider.height = refCollider.height;
            Collider.direction = refCollider.direction;
            Collider.contactOffset = refCollider.contactOffset;
            Collider.isTrigger = true;
        }
        else
        {
            Log.Warn("The main collider of the main Player couldn't be found or is not a CapsuleCollider. Collisions for the RemotePlayer won't be created");
        }
    }

    /// <summary>
    /// Allows the remote player model to have its lighting dynamically adjusted
    /// </summary>
    private void SetupSkyAppliers()
    {
        // SkyAppliers apply the light effects of a lighting source on a set of renderers
        SkyApplier skyApplier = Body.AddComponent<SkyApplier>();
        skyApplier.anchorSky = Skies.Auto;
        skyApplier.emissiveFromPower = false;
        skyApplier.dynamic = true;
        skyApplier.renderers = Body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    /// <summary>
    /// Sets up all bubbles, breathing and diving sounds to be multiplayer ready
    /// </summary>
    private void SetupPlayerSounds()
    {
        GameObject remotePlayerSoundsRoot = new("RemotePlayerSounds");
        remotePlayerSoundsRoot.transform.SetParent(Body.transform);
        FMODEmitterController emitterController = Body.AddComponent<FMODEmitterController>();

        static void CopyEmitter(FMOD_CustomEmitter src, FMOD_CustomEmitter dst)
        {
            dst.asset = src.asset;
            dst.playOnAwake = src.playOnAwake;
            dst.stopImmediatelyOnDisable = src.stopImmediatelyOnDisable;
            dst.followParent = src.followParent;
            dst.restartOnPlay = src.restartOnPlay;
        }

        // Bubbles
        PlayerBreathBubbles localPlayerBubbles = Player.main.GetComponentInChildren<PlayerBreathBubbles>(true);
        FMOD_CustomEmitter bubblesCustomEmitter = remotePlayerSoundsRoot.AddComponent<FMOD_CustomEmitter>();
        CopyEmitter(localPlayerBubbles.bubbleSound, bubblesCustomEmitter);

        if (fmodWhitelist.IsWhitelisted(bubblesCustomEmitter.asset.path, out float bubblesSoundRadius))
        {
            emitterController.AddEmitter(bubblesCustomEmitter.asset.path, bubblesCustomEmitter, bubblesSoundRadius);
        }
        else
        {
            Log.Error($"[{nameof(RemotePlayer)}] Manual created FMOD emitter for {nameof(PlayerBreathBubbles)} but linked sound is not whitelisted: ({bubblesCustomEmitter.asset.path})");
        }

        // Breathing
        BreathingSound breathingSound = Player.main.GetComponentInChildren<BreathingSound>(true);
        FMOD_CustomEmitter breathingSoundCustomEmitter = remotePlayerSoundsRoot.AddComponent<FMOD_CustomEmitter>();
        breathingSoundCustomEmitter.asset = breathingSound.loopingBreathingSound.asset;

        if (fmodWhitelist.IsWhitelisted(breathingSoundCustomEmitter.asset.path, out float breathingSoundRadius))
        {
            emitterController.AddEmitter(breathingSoundCustomEmitter.asset.path, breathingSoundCustomEmitter, breathingSoundRadius);
        }
        else
        {
            Log.Error($"[{nameof(RemotePlayer)}] Manual created FMOD emitter for {nameof(BreathingSound)} but linked sound is not whitelisted: ({breathingSoundCustomEmitter.asset.path})");
        }
    }

    /// <summary>
    /// An InfectedMixin is required for behaviours like <see cref="AggressiveWhenSeeTarget"/> which look for this on the target they find
    /// </summary>
    private void SetupMixins()
    {
        InfectedMixin = Body.AddComponent<InfectedMixin>();
        InfectedMixin.shaderKeyWord = InfectedMixin.uwe_playerinfection;
        Renderer renderer = PlayerModel.transform.Find("male_geo/diveSuit/diveSuit_hands_geo").GetComponent<Renderer>();
        InfectedMixin.renderers = [renderer];

        LiveMixin = Body.AddComponent<LiveMixin>();
        LiveMixin.data = new()
        {
            maxHealth = 100,
            broadcastKillOnDeath = false
        };
        LiveMixin.health = 100;
        // We set the remote player to invincible because we only want this component to be detectable but not to work
        LiveMixin.invincible = true;
    }

    public void RecordVitals(float health, float food, float water, float unscaledTime)
    {
        LastVitals = new PlayerListStatsRules.VitalsSample(health, food, water, unscaledTime);
    }

    public void UpdateHealthAndInfection(float health, float infection)
    {
        if (LiveMixin)
        {
            LiveMixin.health = health;
        }

        if (InfectedMixin)
        {
            InfectedMixin.infectedAmount = infection;
            InfectedMixin.UpdateInfectionShading();
        }
    }

    public void SetGameMode(SubnauticaGameMode gameMode)
    {
        PlayerContext.GameMode = gameMode;
        RefreshVitalsVisibility();
    }

    private void RefreshVitalsVisibility()
    {
        if (vitals)
        {
            // TODO: only show health and oxygen in freedom mode
            bool visible = PlayerContext.GameMode != SubnauticaGameMode.CREATIVE;
            vitals.SetStatsVisible(visible);
        }
    }

    /// <summary>
    /// Adaptation of <see cref="Player.CanBeAttacked"/> for remote players.
    /// NB: This doesn't check for other player's use of 'invisible' command
    /// </summary>
    public bool CanBeAttacked()
    {
        // A rider counts as inside the Seamoth, like a pilot of the local player does
        return !SubRoot && !EscapePod && !RiderBar && PlayerContext.GameMode != SubnauticaGameMode.CREATIVE;
    }
}
