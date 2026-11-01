using System;
#if DEBUG
using System.Text;
#endif
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerModels;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.PlayerModels;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// The third-person view of a player's own emote (docs/coop/PLAYER_MODELS.md, "The emote camera"): while the emote plays the camera leaves his head and looks at his character from a few metres away, the way another
/// player sees him, and the mouse circles it round the character. The character is the custom model that the others see (<see cref="CustomPlayerModels.WearLocal" />: a copy of it on the local body that plays the same
/// clip, while the first-person body is hidden); the real body does not move, and nothing that the game does with the camera is changed: the camera is only moved for the length of its own rendering
/// (<c>Camera.onPreCull</c>, put back in <c>Camera.onPostRender</c>), so that every script of the game, and the movement that is broadcast to the others, keeps seeing the first-person camera. The camera is pulled in
/// so that it never goes through a wall (a ball swept from the character, against the same solid layers as the Scout Drone), and it stays on the character's side of the water surface.
/// <para>
/// While the view is out: the mouse look of the first-person camera is off (so the body is not turned by the mouse), the tool is put away, the number keys and the wheel do not change the slot, the hand does not use
/// what is under the reticle, the reticle is hidden, and Escape does not open the game's menu (the driver takes it as "stop"). Everything is <b>put back</b> by <see cref="End" />, which is safe to call at any time and
/// more than once, and which a watchdog and every failure call.
/// </para>
/// </summary>
internal sealed class PlayerEmoteCamera
{
    /// <summary>The longest that the view stays out, whatever happens: the longest emote and a margin.</summary>
    private const float WATCHDOG_SECONDS = PlayerEmoteRules.MAX_PLAY_SECONDS + 4f;

    /// <summary>A friend further away than this (metres) does not decide which side the camera starts on.</summary>
    private const float FRIEND_RANGE = 25f;

    /// <summary>The ball (metres) that the diagnostic puts at the camera to see whether it is inside a wall: much smaller than the ball that is swept, so that standing off a wall by a hand does not count.</summary>
    private const float INSIDE_CHECK_RADIUS = 0.05f;

    private static int solidMask = -1;

    private CustomPlayerModel model;
    private Camera camera;
    private Transform cameraTransform;
    private bool hooked;
    private bool overriding;
    private Vector3 savedPosition;
    private Quaternion savedRotation = Quaternion.identity;
    private float shownDistance;
    private float slideOffset;
    private float viewYaw;
    private float watchdogAt;
    private bool failed;
    private string lastWall = "none";
    private string startedFrom = "";
    private Vector3 lastPivot;
    private Vector3 lastCameraAt;
    private Vector3 lastForward;
    private bool hasForward;
    private int framesInsideSolid;
    private int framesPlaced;

    // What the game had before, so that it is put back exactly
    private bool changedMouseLook;
    private bool savedMouseLook;
    private bool changedPauseLock;
    private bool savedPauseLock;
    private int pauseLockReleaseFrame;
    private bool suspendedSlots;
    private int savedQuickSlot = -1;
    private bool hidReticle;
    private GUIHand hand;
    private bool changedHand;
    private bool savedHandEnabled;

    /// <summary>Whether the view is out now.</summary>
    public bool Active { get; private set; }

    /// <summary>Degrees: the way the camera looks along (0 along +Z, turning towards +X).</summary>
    public float Yaw { get; private set; }

    /// <summary>Degrees above the horizon that the camera looks (negative: down at the character).</summary>
    public float Pitch { get; private set; }

    /// <summary>The distance that the camera is asked to keep from the character, in metres (the wheel changes it).</summary>
    public float Distance { get; private set; }

    /// <summary>The distance that it really is, after the walls.</summary>
    public float ShownDistance => shownDistance;

    /// <summary>The solid things that pull the camera in: what the Scout Drone also stops at (terrain, bases, hulls and vehicles); triggers, creatures and the players' bodies do not count.</summary>
    internal static int SolidMask
    {
        get
        {
            if (solidMask < 0)
            {
                solidMask = LayerMask.GetMask("Default", "BuiltEntities", "Vehicle", "TerrainCollider", "SubRigidbodyExclude", "BaseClipProxy");
            }
            return solidMask;
        }
    }

    /// <summary>
    /// The way the emote camera looked in its last rendering, for the billboards of the mod (the name and the stats above a remote player turn to the camera that draws them). False while the view is not out or has not
    /// been drawn yet.
    /// </summary>
    internal bool TryGetRenderForward(out Vector3 forward)
    {
        forward = lastForward;
        return Active && hasForward;
    }

    /// <summary>A position as x,y,z with two decimals and no spaces, for a line that a script splits into key=value tokens.</summary>
    internal static string Compact(Vector3 position)
    {
        return $"{position.x:F2},{position.y:F2},{position.z:F2}";
    }

    /// <summary>The line of the Debug command <c>vumodel wheel state</c>: plain tokens.</summary>
    public string StateText()
    {
        return Active
            ? $"camera=out yaw={Yaw:F0} pitch={Pitch:F0} distance={Distance:F2} shown={shownDistance:F2} slid={slideOffset:F0} view={viewYaw:F0} wall={lastWall} from={startedFrom} failed={failed} at={Compact(lastCameraAt)} pivot={Compact(lastPivot)} placed={framesPlaced} insideSolid={framesInsideSolid} ocean={Ocean.GetOceanLevel():F2}"
            : $"camera=in lastPlaced={framesPlaced} lastInsideSolid={framesInsideSolid}";
    }

    /// <summary>
    /// Takes the camera out to the character: puts the model <paramref name="modelId" /> on the local body, plays <paramref name="emoteId" /> on it and starts the orbit. False with the reason in
    /// <paramref name="failure" /> when it cannot be done (the emote goes on in first person then); nothing is left changed in that case.
    /// </summary>
    public bool Begin(string modelId, string emoteId, out string failure)
    {
        failure = null;
        if (Active)
        {
            End("a new emote");
        }
        Player player = Player.main;
        Camera mainCamera = MainCamera.camera;
        if (!player || !mainCamera || !MainCameraControl.main || !FPSInputModule.current || Inventory.main == null || Inventory.main.quickSlots == null)
        {
            failure = "the camera, the input or the inventory is not ready";
            return false;
        }
        try
        {
            if (!CustomPlayerModels.WearLocal(modelId, out string wearMessage))
            {
                failure = wearMessage;
                return false;
            }
            CustomPlayerModel worn = CustomPlayerModels.Local;
            string playMessage = "";
            if (!worn || !worn.PlayEmote(emoteId, out playMessage))
            {
                failure = worn ? playMessage : "the copy of the model is gone";
                CustomPlayerModels.ReleaseLocalFromEmote();
                return false;
            }

            model = worn;
            camera = mainCamera;
            cameraTransform = mainCamera.transform;
            failed = false;
            overriding = false;
            hasForward = false;
            lastWall = "none";
            framesPlaced = 0;
            framesInsideSolid = 0;
            Vector3 facing = worn.Facing;
            float idealYaw = EmoteCameraRules.StartYaw(EmoteCameraRules.YawOf(facing.x, facing.z));
            startedFrom = "front";
            if (TryFriendYaw(worn.AimPoint, out float friendYaw))
            {
                idealYaw = friendYaw;
                startedFrom = "friend";
            }
            Pitch = EmoteCameraRules.START_PITCH;
            Distance = EmoteCameraRules.DEFAULT_DISTANCE;
            // he may stand at a console with his face to a wall: the camera starts on the nearest side that has room
            Vector3 startPivot = worn.AimPoint + Vector3.up * EmoteCameraRules.PIVOT_ABOVE_HIPS;
            Yaw = EmoteCameraRules.PickViewYaw(idealYaw, yaw => RoomAlong(startPivot, yaw, Pitch, Distance), Distance, out float startRoom);
            slideOffset = 0f;
            viewYaw = Yaw;
            string turned = Math.Abs(EmoteCameraRules.WrapDegrees(Yaw - idealYaw)) > 0.5f ? $", turned {EmoteCameraRules.WrapDegrees(Yaw - idealYaw):F0} degrees for room ({startRoom:F1} m)" : "";
            shownDistance = Math.Min(Distance, Math.Max(startRoom, EmoteCameraRules.FLOOR_DISTANCE));
            watchdogAt = Time.unscaledTime + WATCHDOG_SECONDS;

            // from here the game is changed: End puts all of it back
            Active = true;
            TakeInput(player);
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
            hooked = true;
            Log.Info($"[PlayerModels] the emote camera is out for '{emoteId}': {worn.Summary}; it starts {startedFrom} of the character, yaw {Yaw:F0}, {Distance:F1} m{turned}");
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[PlayerModels] the emote camera could not be taken out");
            failure = $"{exception.GetType().Name}: {exception.Message}";
            End("it could not be taken out");
            return false;
        }
    }

    /// <summary>
    /// What the view does each frame (the driver calls it from its Update, also when the view is in, for the housekeeping): the orbit follows the mouse and the wheel, and a view that must go (the model is gone, the camera
    /// failed, the watchdog ran out) goes.
    /// </summary>
    public void Tick()
    {
        if (!Active)
        {
            return;
        }
        if (overriding)
        {
            // the render did not put the camera back (it was not drawn this frame): do it before the game looks at it
            RestoreCameraPose();
        }
        bool watchdogRan = Time.unscaledTime > watchdogAt;
#if DEBUG
        if (PlayerEmoteDriver.DebugForever)
        {
            watchdogRan = false;
        }
#endif
        if (failed || !model || !camera || watchdogRan)
        {
            End(failed ? "the camera failed" : !model || !camera ? "the character or the camera is gone" : "the watchdog ran out");
            return;
        }
        MainCameraControl control = MainCameraControl.main;
        if (control && control.mouseLookEnabled)
        {
            // something turned it on again: the mouse must not turn the real body
            control.mouseLookEnabled = false;
        }
        Vector2 look = GameInput.GetLookDelta();
        Yaw = EmoteCameraRules.WrapDegrees(Yaw + look.x);
        Pitch = EmoteCameraRules.ClampPitch(Pitch + look.y);
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            Distance = EmoteCameraRules.ClampDistance(Distance - wheel * EmoteCameraRules.ZOOM_STEP);
        }
    }

#if DEBUG
    /// <summary>Debug (the live check, which cannot move a real mouse): turns the orbit to a yaw, a pitch and a distance, as the mouse and the wheel would.</summary>
    internal void DebugSet(float yaw, float pitch, float distance)
    {
        Yaw = EmoteCameraRules.WrapDegrees(yaw);
        Pitch = EmoteCameraRules.ClampPitch(pitch);
        Distance = EmoteCameraRules.ClampDistance(distance);
    }

    /// <summary>
    /// Debug (the live check): what the sweeps of the camera see along one yaw and pitch from the camera's pivot, as plain tokens with no spaces: the first thing that the ball and the ray hit (as the game's sweeps
    /// see it and with the back of a wall too: <c>ball</c>, <c>ray</c>, <c>bball</c>, <c>bray</c>: distance and what), the distance that the camera may go (<c>allowed</c>, and <c>by</c> what), and the solid things
    /// that touch the pivot (<c>atPivot</c>) and the spot where the camera would be (<c>atEnd</c>): count[name@layer/kind/size|...].
    /// </summary>
    internal string DebugProbe(float yaw, float pitch, float distance)
    {
        Vector3 pivot = Active ? lastPivot : Player.main ? Player.main.transform.position : Vector3.zero;
        (float x, float y, float z) = EmoteCameraRules.Direction(yaw, pitch);
        Vector3 back = -new Vector3(x, y, z);
        float reach = distance + EmoteCameraRules.WALL_SKIN;
        StringBuilder text = new();
        text.Append($"yaw={yaw:F0} pitch={pitch:F0} dist={distance:F2} pivot={Compact(pivot)}");
        bool savedBackfaces = Physics.queriesHitBackfaces;
        try
        {
            foreach (bool backfaces in new[] { false, true })
            {
                Physics.queriesHitBackfaces = backfaces;
                string tag = backfaces ? "b" : "";
                bool ballHit = Physics.SphereCast(pivot, EmoteCameraRules.SPHERE_RADIUS, back, out RaycastHit ball, reach, SolidMask, QueryTriggerInteraction.Ignore);
                text.Append($" {tag}ball={(ballHit ? $"{ball.distance:F2}:{Describe(ball.collider)}" : "none")}");
                bool rayHit = Physics.Raycast(pivot, back, out RaycastHit ray, reach, SolidMask, QueryTriggerInteraction.Ignore);
                text.Append($" {tag}ray={(rayHit ? $"{ray.distance:F2}:{Describe(ray.collider)}" : "none")}");
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = savedBackfaces;
        }
        float allowed = SweepAllowed(pivot, back, distance, out string wall);
        text.Append($" allowed={allowed:F2} by={wall ?? "none"}");
        text.Append($" atPivot={Overlapping(pivot, EmoteCameraRules.SPHERE_RADIUS)} atEnd={Overlapping(pivot + back * distance, INSIDE_CHECK_RADIUS)}");
        return text.ToString();
    }

    private static string Overlapping(Vector3 center, float radius)
    {
        Collider[] found = Physics.OverlapSphere(center, radius, SolidMask, QueryTriggerInteraction.Ignore);
        if (found.Length == 0)
        {
            return "none";
        }
        StringBuilder list = new();
        for (int i = 0; i < found.Length && i < 4; i++)
        {
            list.Append(i == 0 ? "" : "|").Append(Describe(found[i]));
        }
        return $"{found.Length}[{list}]";
    }

    private static string Describe(Collider collider)
    {
        if (!collider)
        {
            return "?";
        }
        string kind = collider switch
        {
            BoxCollider => "Box",
            SphereCollider => "Sphere",
            CapsuleCollider => "Capsule",
            MeshCollider mesh => mesh.sharedMesh ? $"Mesh{(mesh.convex ? "Convex" : "")}v{mesh.sharedMesh.vertexCount}" : "MeshNone",
            _ => collider.GetType().Name,
        };
        Vector3 size = collider.bounds.size;
        return $"{NameOf(collider)}@{LayerMask.LayerToName(collider.gameObject.layer)}/{kind}/{size.x:F1}x{size.y:F1}x{size.z:F1}";
    }
#endif

    /// <summary>Puts the camera back in the head and gives the game everything that was taken. Safe at any time and more than once.</summary>
    public void End(string reason)
    {
        if (!Active && !hooked && !changedMouseLook && !changedPauseLock && !suspendedSlots && !hidReticle && !changedHand)
        {
            return;
        }
        bool wasActive = Active;
        Active = false;
        if (hooked)
        {
            Camera.onPreCull -= OnPreCull;
            Camera.onPostRender -= OnPostRender;
            hooked = false;
        }
        Guard("put the camera back", RestoreCameraPose);
        Guard("give the copy of the model back", CustomPlayerModels.ReleaseLocalFromEmote);
        model = null;
        Guard("give the input back", GiveInputBack);
        if (wasActive)
        {
            Log.Info($"[PlayerModels] the emote camera is back in the head ({reason})");
        }
    }

    /// <summary>
    /// The housekeeping that must outlive the view (the driver calls it every frame, and with <paramref name="force" /> when it is destroyed): the lock of the pause menu is given back a frame after the view ends,
    /// because the key that ended the emote may be Escape and the game would open its menu on it in the same frame.
    /// </summary>
    public void Settle(bool force)
    {
        if (!changedPauseLock || Active || pauseLockReleaseFrame == 0)
        {
            return;
        }
        if (!force && Time.frameCount < pauseLockReleaseFrame)
        {
            return;
        }
        changedPauseLock = false;
        pauseLockReleaseFrame = 0;
        if (FPSInputModule.current)
        {
            FPSInputModule.current.lockPauseMenu = savedPauseLock;
        }
    }

    // ---- the camera

    private void OnPreCull(Camera culling)
    {
        if (!Active || culling != camera || !model || !cameraTransform)
        {
            return;
        }
        try
        {
            Vector3 pivot = model.AimPoint + Vector3.up * EmoteCameraRules.PIVOT_ABOVE_HIPS;
            // the way the mouse points, unless there is no room on that side (his shoulder is to a wall in a corridor): then the camera slides round him to the nearest side that has room, and back
            float targetYaw = EmoteCameraRules.PickViewYaw(Yaw, yaw => RoomAlong(pivot, yaw, Pitch, Distance), Distance, out _);
            slideOffset = EmoteCameraRules.SlideToward(slideOffset, EmoteCameraRules.WrapDegrees(targetYaw - Yaw), Time.unscaledDeltaTime);
            viewYaw = EmoteCameraRules.WrapDegrees(Yaw + slideOffset);
            (float lookX, float lookY, float lookZ) = EmoteCameraRules.Direction(viewYaw, Pitch);
            Vector3 back = -new Vector3(lookX, lookY, lookZ);

            // the first wall in the way pulls the camera in (at once); when the way is free it drifts back out
            float allowed = SweepAllowed(pivot, back, Distance, out string wall);
            if (wall != null && allowed < Distance)
            {
                lastWall = wall;
            }
            shownDistance = EmoteCameraRules.EasedDistance(shownDistance, allowed, Time.unscaledDeltaTime);
            Vector3 position = pivot + back * shownDistance;

            // in the sea the camera stays on the character's side of the surface (inside a base or a Cyclops the surface is not a thing)
            Player player = Player.main;
            bool inRoom = player && player.currentSub;
            position.y = EmoteCameraRules.KeepOnWaterSide(position.y, pivot.y, Ocean.GetOceanLevel(), !inRoom);

            Vector3 toPivot = pivot - position;
            if (toPivot.sqrMagnitude < 1e-6f)
            {
                return;
            }
            lastPivot = pivot;
            lastCameraAt = position;
            lastForward = toPivot.normalized;
            hasForward = true;
            framesPlaced++;
            if (Physics.CheckSphere(position, INSIDE_CHECK_RADIUS, SolidMask, QueryTriggerInteraction.Ignore))
            {
                // a diagnostic: the camera ended up inside something solid (the live check counts these)
                framesInsideSolid++;
            }
            savedPosition = cameraTransform.position;
            savedRotation = cameraTransform.rotation;
            overriding = true;
            cameraTransform.SetPositionAndRotation(position, Quaternion.LookRotation(toPivot, Vector3.up));
        }
        catch (Exception exception)
        {
            // once: the next Tick ends the view
            failed = true;
            Log.Error(exception, "[PlayerModels] the emote camera failed while it placed the camera");
        }
    }

    /// <summary>
    /// How far from <paramref name="pivot" /> the camera may go along <paramref name="back" /> (a unit vector away from the character) towards <paramref name="wanted" /> metres without going through a wall:
    /// a ball is swept (made smaller while a wall already touches it at the character, because a cast does not report what it starts inside of: in the first live check he stood a hand's width from the hull of a
    /// Cyclops, the ball started inside it and the camera ended outside), and a thin ray as well (it sees what the ball slides by). Both see the back of a wall too (<c>Physics.queriesHitBackfaces</c>, put back at
    /// once), so that a wall that faces one way also stops the camera from its other side (the live probe saw no difference in a Cyclops: it is insurance for the mesh walls of bases).
    /// <paramref name="wall" /> is the name of what stopped it, null when nothing did.
    /// </summary>
    private static float SweepAllowed(Vector3 pivot, Vector3 back, float wanted, out string wall)
    {
        wall = null;
        float distance = EmoteCameraRules.ClampDistance(wanted);
        float reach = distance + EmoteCameraRules.WALL_SKIN;
        float allowed = distance;
        bool savedBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        try
        {
            float radius = EmoteCameraRules.SPHERE_RADIUS;
            while (radius > EmoteCameraRules.MIN_BALL_RADIUS && Physics.CheckSphere(pivot, radius, SolidMask, QueryTriggerInteraction.Ignore))
            {
                radius *= 0.5f;
            }
            if (Physics.SphereCast(pivot, radius, back, out RaycastHit ball, reach, SolidMask, QueryTriggerInteraction.Ignore))
            {
                float byBall = EmoteCameraRules.AllowedDistance(distance, true, ball.distance);
                if (byBall < allowed)
                {
                    allowed = byBall;
                    wall = NameOf(ball.collider);
                }
            }
            if (Physics.Raycast(pivot, back, out RaycastHit ray, reach, SolidMask, QueryTriggerInteraction.Ignore))
            {
                float byRay = EmoteCameraRules.AllowedDistance(distance, true, ray.distance);
                if (byRay < allowed)
                {
                    allowed = byRay;
                    wall = NameOf(ray.collider);
                }
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = savedBackfaces;
        }
        return allowed;
    }

    /// <summary>The room that the camera has along a yaw (and a pitch) from the pivot, in metres.</summary>
    private static float RoomAlong(Vector3 pivot, float yaw, float pitch, float wanted)
    {
        (float x, float y, float z) = EmoteCameraRules.Direction(yaw, pitch);
        return SweepAllowed(pivot, -new Vector3(x, y, z), wanted, out _);
    }

    private static string NameOf(Collider collider)
    {
        return collider ? collider.name.Replace(' ', '_') : "?";
    }

    private void OnPostRender(Camera rendered)
    {
        if (overriding && rendered == camera)
        {
            RestoreCameraPose();
        }
    }

    private void RestoreCameraPose()
    {
        if (!overriding)
        {
            return;
        }
        overriding = false;
        if (cameraTransform)
        {
            cameraTransform.SetPositionAndRotation(savedPosition, savedRotation);
        }
    }

    /// <summary>The nearest other player within range decides the side that the camera starts on: the player sees himself from where his friend sees him.</summary>
    private static bool TryFriendYaw(Vector3 pivot, out float yaw)
    {
        yaw = 0f;
        try
        {
            PlayerManager players = NitroxServiceLocator.LocateService<PlayerManager>();
            float nearest = FRIEND_RANGE * FRIEND_RANGE;
            Vector3 friend = default;
            bool found = false;
            foreach (RemotePlayer remote in players.GetAll())
            {
                if (remote == null || !remote.Body || !remote.Body.activeInHierarchy)
                {
                    continue;
                }
                Vector3 there = remote.Body.transform.position;
                float distance = (there - pivot).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    friend = there;
                    found = true;
                }
            }
            return found && EmoteCameraRules.TryYawFromFriend(pivot.x, pivot.z, friend.x, friend.z, out yaw);
        }
        catch (Exception exception)
        {
            Log.Warn($"[PlayerModels] the emote camera could not look for a friend: {exception.Message}");
            return false;
        }
    }

    // ---- the input

    private void TakeInput(Player player)
    {
        MainCameraControl control = MainCameraControl.main;
        savedMouseLook = control.mouseLookEnabled;
        control.mouseLookEnabled = false;
        changedMouseLook = true;

        FPSInputModule input = FPSInputModule.current;
        if (!changedPauseLock)
        {
            savedPauseLock = input.lockPauseMenu;
            changedPauseLock = true;
        }
        pauseLockReleaseFrame = 0;
        input.lockPauseMenu = true;

        Inventory inventory = Inventory.main;
        savedQuickSlot = inventory.quickSlots.activeSlot;
        inventory.ReturnHeld(true);
        inventory.quickSlots.SetSuspendSlotActivation(true);
        suspendedSlots = true;

        if (HandReticle.main)
        {
            HandReticle.main.RequestCrosshairHide();
            hidReticle = true;
        }
        hand = player.guiHand;
        if (hand)
        {
            savedHandEnabled = hand.enabled;
            hand.enabled = false;
            changedHand = true;
        }
    }

    private void GiveInputBack()
    {
        if (changedMouseLook)
        {
            changedMouseLook = false;
            if (MainCameraControl.main)
            {
                MainCameraControl.main.mouseLookEnabled = savedMouseLook;
            }
        }
        if (changedPauseLock && pauseLockReleaseFrame == 0)
        {
            // not at once (Settle does it a frame later): the key that ended the emote may be Escape, and the game looks at it later in this very frame, where it would open the menu
            pauseLockReleaseFrame = Time.frameCount + 1;
        }
        if (changedHand)
        {
            changedHand = false;
            if (hand)
            {
                hand.enabled = savedHandEnabled;
            }
        }
        hand = null;
        if (hidReticle)
        {
            hidReticle = false;
            if (HandReticle.main)
            {
                HandReticle.main.UnrequestCrosshairHide();
            }
        }
        if (suspendedSlots)
        {
            suspendedSlots = false;
            Inventory inventory = Inventory.main;
            if (inventory != null && inventory.quickSlots != null)
            {
                inventory.quickSlots.SetSuspendSlotActivation(false);
                if (savedQuickSlot >= 0 && Player.main && Player.main.GetMode() == Player.Mode.Normal)
                {
                    inventory.quickSlots.SelectImmediate(savedQuickSlot);
                }
            }
        }
        savedQuickSlot = -1;
    }

    /// <summary>Runs one step of the putting back and logs it if it fails, so that one step that breaks does not leave the others undone.</summary>
    private static void Guard(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[PlayerModels] the emote camera could not {what}");
        }
    }
}
