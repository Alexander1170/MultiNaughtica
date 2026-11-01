#if DEBUG
using System.Collections;
using Nitrox.Model.Core;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Gui.HUD;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     The rider's hand on the path of a real player (a harness script that is not part of this repository, round D2): <c>vurider aim bar|body</c> stands the local player 1.2 m behind the nearest built Rider Bar (or beside the
///     Seamoth's hull), looks at it by the eye ray and prints what the game's hand points at and what the reticle says; the click itself is a real mouse click that the script sends (keys.ps1 -Click left).
///     <c>vurider deny</c> is the 2 s window of the refusal (<see cref="DenyOwnershipHand" />) in one command, because a script is too slow for it: another player pilots the Seamoth, the local player clicks the hull
///     (the vanilla <c>Vehicle.OnHandClick</c>, which the patch answers with a lock request that the server refuses), looks at the hull for a few frames (the refusal must show there), then looks at the Rider Bar for a few
///     frames (the bar's own text, "Hold on", must show there while the refusal is still alive).
/// </summary>
public partial class HarnessConsoleCommands
{
    private const float VU_HAND_STAND_METERS = 1.2f;

    private static bool VuHandSpot(SeamothRiderBar bar, bool body, out Vector3 stand, out Vector3 aim)
    {
        stand = default;
        aim = default;
        if (!bar || !bar.IsBuilt || !bar.Vehicle)
        {
            return false;
        }

        Transform hull = bar.Vehicle.transform;
        if (body)
        {
            // beside the hull, at the middle of its box, looking at the middle
            if (!HullBounds.TryGet(hull, out Bounds box))
            {
                return false;
            }
            aim = hull.TransformPoint(box.center);
            stand = hull.TransformPoint(box.center + Vector3.right * (box.extents.x + VU_HAND_STAND_METERS));
            return true;
        }

        // behind the rod (the rear of a Seamoth is its local -z), looking at the middle of the rod
        aim = (bar.LeftHand.position + bar.RightHand.position) * 0.5f;
        stand = aim - hull.forward * VU_HAND_STAND_METERS;
        return true;
    }

    /// <summary>Puts the camera at <paramref name="stand" /> (the player's root is a little below the eyes) and turns it to <paramref name="aim" />, then lets a few frames pass so that the game's hand has looked.</summary>
    private static IEnumerator VuHandGo(Vector3 stand, Vector3 aim)
    {
        Player player = Player.main;
        player.OnPlayerPositionCheat();
        player.SetPosition(stand);
        yield return null;
        yield return null;
        player.SetPosition(player.transform.position + (stand - MainCamera.camera.transform.position));
        yield return null;
        yield return null;
        Vector3 toAim = aim - MainCamera.camera.transform.position;
        MainCameraControl.main.rotationX = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
        MainCameraControl.main.rotationY = -Mathf.Asin(-toAim.y / toAim.magnitude) * Mathf.Rad2Deg;
        yield return null;
        yield return null;
        yield return null;
    }

    /// <summary>What the hand reticle says: <c>hold</c> (the bar's "Hold on"), <c>deny</c> (the refusal), <c>other</c>, <c>none</c> (no text); the text itself comes out too.</summary>
    private static string VuHandReticle(out string text)
    {
        text = HandReticle.main && HandReticle.main.compTextHand ? HandReticle.main.compTextHand.text : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            text = string.Empty;
            return "none";
        }
        if (Language.main)
        {
            string deny = Language.main.Get("Nitrox_DenyOwnershipHand");
            string hold = Language.main.Get("Nitrox_RiderBarHold");
            if (!string.IsNullOrEmpty(deny) && text.Contains(deny))
            {
                return "deny";
            }
            if (!string.IsNullOrEmpty(hold) && text.Contains(hold))
            {
                return "hold";
            }
        }
        return "other";
    }

    /// <summary>What the game's hand points at: <c>bar</c> (the Rider Bar's hand target), <c>hull</c> (the Seamoth itself), <c>other</c>, <c>none</c>.</summary>
    private static string VuHandActive(SeamothRiderBar bar, out string activeName)
    {
        GUIHand hand = Player.main ? Player.main.guiHand : null;
        GameObject active = hand ? hand.GetActiveTarget() : null;
        activeName = active ? active.name : "none";
        if (!active)
        {
            return "none";
        }
        if (active.GetComponent<RiderBarHandTarget>())
        {
            return "bar";
        }
        return bar && bar.Vehicle && active == bar.Vehicle.gameObject ? "hull" : "other";
    }

    /// <summary>vurider aim bar|body: stands the player at the bar (or the hull), looks at it and prints one <c>vurider: aim</c> line.</summary>
    private IEnumerator VuRiderAimAsync(string what)
    {
        SeamothRiderBar bar = VuNearestBar();
        bool body = what == "body";
        if (!Player.main || !MainCamera.camera || !MainCameraControl.main || !bar || RiderBars.IsLocalRiding || !VuHandSpot(bar, body, out Vector3 stand, out Vector3 aim))
        {
            Log.Info($"{VU}vurider: aim want={what} stood=False reason={(!bar ? "no-bar" : RiderBars.IsLocalRiding ? "riding" : "no-spot")}");
            yield break;
        }

        yield return VuHandGo(stand, aim);
        string kind = VuHandActive(bar, out string activeName);
        string reticle = VuHandReticle(out string text);
        Log.Info(string.Concat($"{VU}vurider: aim want={what} stood=True active={kind} activeName={activeName} reticle={reticle} text=\"{text}\" decision={RiderBars.DecisionFor(bar)} ",
                               $"distance={VuNum(Vector3.Distance(MainCamera.camera.transform.position, aim))} riding={RiderBars.IsLocalRiding} t={VuSyncedTime()}"));
        VuTargetDiag();
    }

    /// <summary>
    /// What the game's own targeting finds from the camera (the call GUIHand makes every frame: 2 m, the player ignored) and, hit by hit, the cast it starts with (every layer except Trigger and OnlyVehicle, triggers
    /// included) with whether the filter would accept each: a trigger only on the Useable layer, anything else unless it is on NotUseable.
    /// </summary>
    private static void VuTargetDiag()
    {
        Player player = Player.main;
        if (!player || !MainCamera.camera)
        {
            return;
        }
        Transform camera = MainCamera.camera.transform;
        bool got = Targeting.GetTarget(player.gameObject, 2f, out GameObject target, out float distance);
        GUIHand hand = player.guiHand;
        GameObject active = hand ? hand.GetActiveTarget() : null;
        Log.Info(string.Concat($"{VU}vurider: target got={got} result={(target ? target.GetFullHierarchyPath() : "none")} layer={(target ? target.layer : -1)} distance={VuNum(distance)} guiHand={(active ? active.GetFullHierarchyPath() : "none")} ",
                               $"layerIds=useable:{LayerID.Useable},notUseable:{LayerID.NotUseable},trigger:{LayerID.Trigger},onlyVehicle:{LayerID.OnlyVehicle} camera={camera.position.ToString("F2")} forward={camera.forward.ToString("F2")} mode={player.GetMode()} cinematic={player.cinematicModeActive}"));
        int mask = ~((1 << (LayerID.Trigger & 31)) | (1 << (LayerID.OnlyVehicle & 31)));
        RaycastHit[] hits = Physics.RaycastAll(camera.position, camera.forward, 2f, mask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            int layer = hit.collider.gameObject.layer;
            bool accepted = hit.collider.isTrigger ? layer == LayerID.Useable : layer != LayerID.NotUseable;
            Log.Info($"{VU}vurider: target hit {hit.collider.gameObject.GetFullHierarchyPath()} layer={layer} trigger={hit.collider.isTrigger} distance={VuNum(hit.distance)} accepted={accepted} underPlayer={hit.collider.transform.IsChildOf(player.transform)}");
        }
    }

    /// <summary>vurider deny: the refusal window, see the class comment. Prints <c>vurider: deny ...</c> lines and ends with <c>vurider: deny done</c>.</summary>
    private IEnumerator VuRiderDenyAsync()
    {
        SeamothRiderBar bar = VuNearestBar();
        Player player = Player.main;
        if (!player || !MainCamera.camera || !MainCameraControl.main || !bar || RiderBars.IsLocalRiding)
        {
            Log.Info($"{VU}vurider: deny done ok=False reason={(!bar ? "no-bar" : RiderBars.IsLocalRiding ? "riding" : "no-player")}");
            yield break;
        }

        // Only a refusal can be looked at: another player pilots the Seamoth, so that the server says no to the click (else the click would put this player into the Seamoth)
        Vehicle vehicle = bar.Vehicle;
        string pilot = "none";
        foreach (RemotePlayer remote in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
        {
            if (remote.Vehicle == vehicle)
            {
                pilot = remote.PlayerName;
            }
        }
        if (pilot == "none" || VuLock(vehicle) == "exclusive")
        {
            Log.Info($"{VU}vurider: deny done ok=False reason={(pilot == "none" ? "nobody-pilots" : "own-lock")} lock={VuLock(vehicle)}");
            yield break;
        }
        Log.Info($"{VU}vurider: deny begin pilot={pilot} vehicle={VuId(vehicle.gameObject)} lock={VuLock(vehicle)}");

        if (!VuHandSpot(bar, true, out Vector3 hullStand, out Vector3 hullAim) || !VuHandSpot(bar, false, out Vector3 barStand, out Vector3 barAim))
        {
            Log.Info($"{VU}vurider: deny done ok=False reason=no-spot");
            yield break;
        }

        // 1. behind the rod, and the click on the hull from there (the click is the hand's call and needs no aim): the refusal lives 2 s, and on a machine that runs both clients a frame takes about a second, so the
        //    bar is looked at at once, with no trip between the two spots in the window
        yield return VuHandGo(barStand, barAim);
        float clickedAt = Time.time;
        vehicle.OnHandClick(player.guiHand);

        // 2. the answer of the server: the component of the refusal
        DenyOwnershipHand deny = null;
        while (Time.time - clickedAt < 3f && !(deny = vehicle.GetComponent<DenyOwnershipHand>()))
        {
            yield return null;
        }
        if (!deny)
        {
            Log.Info($"{VU}vurider: deny done ok=False reason=no-refusal lock={VuLock(vehicle)} riding={RiderBars.IsLocalRiding}");
            yield break;
        }
        float answerAfter = Time.time - clickedAt;

        // 3. at the bar, with the refusal alive: the bar's own text must show
        VuDenyCount atBar = new();
        for (int frame = 0; frame < 10; frame++)
        {
            yield return null;
            VuDenySample(bar, vehicle, atBar);
        }
        VuHandReticle(out string lastText);
        Log.Info($"{VU}vurider: deny phase=bar samples={atBar.Samples} deny={atBar.Deny} hold={atBar.Hold} other={atBar.Other} none={atBar.None} activeHull={atBar.ActiveHull} activeBar={atBar.ActiveBar} hullDeny={atBar.HullDeny} barDeny={atBar.BarDeny} barHold={atBar.BarHold} barHoldAlive={atBar.BarHoldAlive} alive={atBar.Alive} lastText=\"{lastText}\"");

        // 4. beside the hull, the click again (a new refusal), and a few frames on the hull: the refusal must show here
        yield return VuHandGo(hullStand, hullAim);
        clickedAt = Time.time;
        vehicle.OnHandClick(player.guiHand);
        deny = null;
        while (Time.time - clickedAt < 3f && !(deny = vehicle.GetComponent<DenyOwnershipHand>()))
        {
            yield return null;
        }
        VuDenyCount hull = new();
        for (int frame = 0; frame < 5; frame++)
        {
            yield return null;
            VuDenySample(bar, vehicle, hull);
        }
        Log.Info($"{VU}vurider: deny phase=hull samples={hull.Samples} deny={hull.Deny} hold={hull.Hold} other={hull.Other} none={hull.None} activeHull={hull.ActiveHull} activeBar={hull.ActiveBar} hullDeny={hull.HullDeny} barDeny={hull.BarDeny} barHold={hull.BarHold} barHoldAlive={hull.BarHoldAlive} alive={hull.Alive}");
        Log.Info($"{VU}vurider: deny done ok=True pilot={pilot} answerAfter={VuNum(answerAfter)} decision={RiderBars.DecisionFor(bar)} riding={RiderBars.IsLocalRiding} t={VuSyncedTime()}");
    }

    private sealed class VuDenyCount
    {
        public int Samples;
        public int Deny;
        public int Hold;
        public int Other;
        public int None;
        public int ActiveHull;
        public int ActiveBar;

        /// <summary>Samples in which the hand pointed at the hull and the reticle showed the refusal.</summary>
        public int HullDeny;

        /// <summary>Samples in which the hand pointed at the bar and the reticle showed the refusal (the old fault) or the bar's own text.</summary>
        public int BarDeny;

        public int BarHold;

        /// <summary>Samples in which the hand pointed at the bar, the reticle showed the bar's own text and the refusal of the hull was still alive: the only ones that prove the bar wins over the refusal.</summary>
        public int BarHoldAlive;

        public int Alive;
    }

    private static void VuDenySample(SeamothRiderBar bar, Vehicle vehicle, VuDenyCount count)
    {
        count.Samples++;
        string reticle = VuHandReticle(out string _);
        string active = VuHandActive(bar, out string _);
        switch (reticle)
        {
            case "deny":
                count.Deny++;
                break;
            case "hold":
                count.Hold++;
                break;
            case "other":
                count.Other++;
                break;
            default:
                count.None++;
                break;
        }
        switch (active)
        {
            case "hull":
                count.ActiveHull++;
                count.HullDeny += reticle == "deny" ? 1 : 0;
                break;
            case "bar":
                count.ActiveBar++;
                count.BarDeny += reticle == "deny" ? 1 : 0;
                count.BarHold += reticle == "hold" ? 1 : 0;
                count.BarHoldAlive += reticle == "hold" && vehicle && vehicle.GetComponent<DenyOwnershipHand>() ? 1 : 0;
                break;
        }
        if (vehicle && vehicle.GetComponent<DenyOwnershipHand>())
        {
            count.Alive++;
        }
    }
}
#endif
