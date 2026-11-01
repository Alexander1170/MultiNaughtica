#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours.Cyclops;
using NitroxClient.MonoBehaviours.Vehicles;
using UnityEngine;
using UWE;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the live check of the second group of vehicle upgrades (a harness script that is not part of this repository, docs/coop/VEHICLE_UPGRADES.md). Each logs one
///     <c>[SyncHarness] vu...: key=value</c> line per fact. <c>vushare [seconds]</c> the energy of every Cyclops and vehicle and what the power share moved; <c>vutow state|hook|cyclops|release|probe|track</c>
///     the tow hooks; <c>vurider state|hold|release|track|aim|deny</c> the rider bar (<c>aim</c> and <c>deny</c> are in HarnessConsoleCommands.RiderHand.cs); <c>vubar x y z</c> nudges the bar on the nearest Seamoth. The time in a line (<c>t=</c>) is the synced movement clock, so that the
///     lines of two clients can be put side by side.
/// </summary>
public partial class HarnessConsoleCommands
{
    private static string VuSyncedTime()
    {
        TimeManager timeManager = NitroxServiceLocator.LocateService<TimeManager>();
        return VuNum((float)MovementClock.Now(timeManager.RealTimeElapsed));
    }

    private static string VuVector(Vector3 vector) => $"{VuNum(vector.x)},{VuNum(vector.y)},{VuNum(vector.z)}";

    private static string VuName(Vehicle vehicle) => vehicle ? vehicle.GetName() : "none";

    // ---- vushare

    /// <summary>vushare &lt;seconds&gt;: every 2 s for that long the energy of every Cyclops and every Seamoth and Prawn Suit, and the totals of what the power share moved on this client.</summary>
    private void LiveCommand_vushare(NotificationCenter.Notification n)
    {
        float seconds = Mathf.Clamp(LiveArgFloat(n, 0, 0f), 0f, 600f);
        StartCoroutine(VuShareAsync(seconds));
    }

    private IEnumerator VuShareAsync(float seconds)
    {
        float end = Time.time + seconds;
        do
        {
            VuShareLines();
            if (seconds <= 0f)
            {
                break;
            }
            yield return new WaitForSeconds(2f);
        }
        while (Time.time < end);
        Log.Info($"{VU}vushare: done");
    }

    private static void VuShareLines()
    {
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (!VuIsRealCyclops(subRoot) || !subRoot.powerRelay)
            {
                continue;
            }
            float power = subRoot.powerRelay.GetPower();
            float max = subRoot.powerRelay.GetMaxPower();
            CyclopsPowerShare share = subRoot.GetComponent<CyclopsPowerShare>();
            Vehicle docked = share ? share.DockedVehicle() : null;
            Log.Info($"{VU}vushare: kind=cyclops id={VuId(subRoot.gameObject)} switch={PowerShareSwitch.IsOn(subRoot)} energy={VuNum(power)}/{VuNum(max)} percent={VuNum(max > 0f ? power / max * 100f : 0f)} module={PowerShare.HasModule(subRoot)} lock={VuLock(subRoot)} docked={(docked ? VuId(docked.gameObject) : "none")} dockedName={VuName(docked)} emergency={(subRoot.TryGetNitroxId(out NitroxId markId) && PowerShare.TryGetEmergencyMark(markId, out string markName) ? markName.Replace(' ', '_') : "none")} t={VuSyncedTime()}");
        }
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            if (vehicle is not SeaMoth && vehicle is not Exosuit)
            {
                continue;
            }
            vehicle.GetEnergyValues(out float charge, out float capacity);
            string nearest = "none";
            float nearestDistance = float.MaxValue;
            foreach (CyclopsPowerShare share in PowerShare.Cyclopses)
            {
                if (share && share.DistanceToHull(vehicle.transform.position) < nearestDistance)
                {
                    nearestDistance = share.DistanceToHull(vehicle.transform.position);
                    nearest = VuId(share.gameObject);
                }
            }
            Log.Info($"{VU}vushare: kind=vehicle type={(vehicle is SeaMoth ? "seamoth" : "prawn")} id={VuId(vehicle.gameObject)} energy={VuNum(charge)}/{VuNum(capacity)} lock={VuLock(vehicle)} docked={vehicle.docked} piloted={vehicle.GetPilotingMode()} nearestCyclops={nearest} distance={VuNum(nearestDistance == float.MaxValue ? -1f : nearestDistance)} t={VuSyncedTime()}");
        }
        PowerShare.Totals totals = PowerShare.Stats;
        Log.Info($"{VU}vushare: totals requests={totals.RequestsSent} offers={totals.OffersAnswered} given={VuNum(totals.EnergyGiven)} deliveries={totals.DeliveriesApplied} received={VuNum(totals.EnergyReceived)} local={VuNum(totals.EnergyMovedLocally)} returned={VuNum(totals.EnergyReturned)} refunded={VuNum(totals.EnergyRefunded)} pausedDockCharges={totals.DockChargesPaused} t={VuSyncedTime()}");
    }

    // ---- vupanel, vubutton

    /// <summary>The lighting panel buttons of the Cyclops the player is in, or of the nearest Cyclops.</summary>
    private static CyclopsPanelButtons VuPanel()
    {
        SubRoot current = Player.main ? Player.main.GetCurrentSub() : null;
        CyclopsPanelButtons best = null;
        float bestDistance = float.MaxValue;
        foreach (CyclopsPanelButtons buttons in FindObjectsOfType<CyclopsPanelButtons>())
        {
            if (current && buttons.GetComponentInParent<SubRoot>() == current)
            {
                return buttons;
            }
            float distance = Player.main ? Vector3.Distance(Player.main.transform.position, buttons.transform.position) : 0f;
            if (distance < bestDistance)
            {
                best = buttons;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static string VuButton(string name, CyclopsPanelButtons.PanelButton button)
    {
        if (button == null)
        {
            return $"{name}Visible=False {name}Lit=False {name}Pos=none";
        }
        return $"{name}Visible={button.Visible} {name}Lit={button.Lit} {name}Pos={VuNum(button.Root.anchoredPosition.x)},{VuNum(button.Root.anchoredPosition.y)}";
    }

    /// <summary>vupanel [dump]: the state of the power share and tow hook buttons on the lighting panel of the Cyclops the player is in (or the nearest), and with "dump" the panel's whole hierarchy.</summary>
    private void LiveCommand_vupanel(NotificationCenter.Notification n)
    {
        CyclopsPanelButtons buttons = VuPanel();
        if (!buttons)
        {
            Log.Warn($"{VU}vupanel: no Cyclops lighting panel with the button component");
            return;
        }
        SubRoot cyclops = buttons.GetComponentInParent<SubRoot>();
        CyclopsLightingPanel lights = buttons.GetComponent<CyclopsLightingPanel>();
        Log.Info($"{VU}vupanel: cyclops={VuId(cyclops.gameObject)} built={buttons.Built} failed={buttons.BuildFailed} internal={(lights ? lights.lightingOn : false)} flood={(lights ? lights.floodlightsOn : false)} {VuButton("share", buttons.PowerShareButton)} {VuButton("tow", buttons.TowButton)} switch={PowerShareSwitch.IsOn(cyclops)} shareModule={PowerShare.HasModule(cyclops)} hookModule={VehicleTows.HasCyclopsHook(cyclops)} towing={VehicleTows.TowedName(cyclops) ?? "none"} lock={VuLock(cyclops)} t={VuSyncedTime()}");
        if (LiveArgString(n, 0, "") == "dump")
        {
            Log.Info($"{VU}vupanel: hierarchy {CyclopsPanelButtons.Describe(buttons.GetComponent<CyclopsLightingPanel>())}");
        }
    }

    /// <summary>
    /// vulookpanel [share|tow|panel] [tag]: a picture of the lighting panel of the Cyclops the player is in (or of the nearest one): the panel's buttons are switched on as when a player walks up to it, and a second camera
    /// (a copy of the main one, put 0.7 m in front of the power share button, the tow hook button or the panel on the side the player is on) renders it into shot-&lt;player&gt;-&lt;tag&gt;.png in the log folder. The picture
    /// is not a screenshot of the window: the chat window of Nitrox, the HUD and the hands of the player are not in it, and the camera of the player (which the harness could not turn reliably, 26 degrees off on the
    /// client in front and 152 on the one in the background) is not used. It logs `liveshot: saved file=...` like liveshot and where the buttons are in the picture, so that the picture can be judged.
    /// </summary>
    private void LiveCommand_vulookpanel(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "panel");
        string tag = System.Text.RegularExpressions.Regex.Replace(LiveArgString(n, 1, what), "[^A-Za-z0-9_-]", string.Empty);
        if (tag.Length == 0)
        {
            tag = "panel";
        }
        CyclopsPanelButtons buttons = VuPanel();
        CyclopsLightingPanel panel = buttons ? buttons.GetComponent<CyclopsLightingPanel>() : null;
        if (!panel || !Camera.main || !MainCameraControl.main)
        {
            Log.Warn($"{VU}vulookpanel: no Cyclops lighting panel with the button component, or no camera");
            return;
        }
        panel.ButtonsOn();
        Transform share = buttons.PowerShareButton != null ? buttons.PowerShareButton.Root : null;
        Transform tow = buttons.TowButton != null ? buttons.TowButton.Root : null;
        Vector3 target = panel.uiPanel ? panel.uiPanel.transform.position : panel.transform.position;
        if (what == "share" && share)
        {
            target = share.position;
        }
        else if (what == "tow" && tow)
        {
            target = tow.position;
        }
        else if (share && tow)
        {
            target = (share.position + tow.position) * 0.5f;
        }
        StartCoroutine(VuPanelShotAsync(panel, target, buttons, what, tag));
    }

    private IEnumerator VuPanelShotAsync(CyclopsLightingPanel panel, Vector3 target, CyclopsPanelButtons buttons, string what, string tag)
    {
        const int width = 1600;
        const int height = 900;
        // the buttons light up and fade in over about a second after ButtonsOn
        yield return new WaitForSeconds(1.5f);
        Camera main = Camera.main;
        if (!main || !panel)
        {
            Log.Warn($"{VU}vulookpanel: no Cyclops lighting panel with the button component, or no camera");
            yield break;
        }
        GameObject holder = null;
        RenderTexture texture = null;
        Texture2D picture = null;
        try
        {
            Transform ui = panel.uiPanel ? panel.uiPanel.transform : panel.transform;
            Vector3 normal = ui.forward;
            // the side of the panel the player stands on is the side a world-space canvas is read from
            float side = Vector3.Dot(main.transform.position - target, normal) >= 0f ? 1f : -1f;
            Vector3 position = target + normal * side * 0.7f;
            holder = new GameObject("VuPanelCamera");
            Camera shot = holder.AddComponent<Camera>();
            shot.CopyFrom(main);
            shot.enabled = false;
            shot.fieldOfView = 55f;
            shot.nearClipPlane = 0.03f;
            shot.transform.position = position;
            shot.transform.rotation = Quaternion.LookRotation((target - position).normalized, Vector3.up);
            texture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            shot.targetTexture = texture;
            yield return new WaitForEndOfFrame();
            shot.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            byte[] png = picture.EncodeToPNG();
            string name = $"shot-{NitroxServiceLocator.LocateService<LocalPlayer>().PlayerName.Replace(' ', '_')}-{tag}.png";
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Log.LogDirectory, name), png);
            string shareScreen = VuScreen(shot, buttons.PowerShareButton, width, height);
            string towScreen = VuScreen(shot, buttons.TowButton, width, height);
            Log.Info($"[SyncHarness] liveshot: saved file={name} bytes={png.Length} size={width}x{height} alpha=0.00 since=none text=none");
            Log.Info($"{VU}vulookpanel: what={what} distance={VuNum(Vector3.Distance(position, target))} offAxis=0.00 shareScreen={shareScreen} towScreen={towScreen}");
        }
        finally
        {
            if (texture)
            {
                RenderTexture.ReleaseTemporary(texture);
            }
            if (picture)
            {
                Destroy(picture);
            }
            if (holder)
            {
                Destroy(holder);
            }
        }
    }

    /// <summary>Where a panel button is in the picture of a camera (pixels, origin bottom left, with the depth in front of the camera): 'x,y,z', or 'none' / 'behind' / 'outside' (not within the picture).</summary>
    private static string VuScreen(Camera camera, CyclopsPanelButtons.PanelButton button, int width, int height)
    {
        if (button == null || !button.Root || !camera)
        {
            return "none";
        }
        Vector3 point = camera.WorldToScreenPoint(button.Root.position);
        if (point.z <= 0f)
        {
            return "behind";
        }
        if (point.x < 0f || point.x > width || point.y < 0f || point.y > height)
        {
            return "outside";
        }
        return $"{VuNum(point.x)},{VuNum(point.y)},{VuNum(point.z)}";
    }

    /// <summary>
    /// vubutton share|tow|lights|forget|forgetlights: a click on the button of that name on the lighting panel (the same path as the player's click, which cannot aim at a world-space button). lights is the game's own toggle of the
    /// internal lights (what a press on the light button does; Nitrox reports the whole panel). forget drops this client's Power Share switch of the Cyclops WITHOUT a packet: the view of a client that has not
    /// received the press of another player (or never will), which a press on a light then reports (the race the live check "vu2-sw-race-stale" is about). forgetlights turns the INTERIOR lights of this client's
    /// panel over without a packet (the same for the lights; "vu2-sw-race-share": the press of the share button then reports lights that are only that client's view).
    /// </summary>
    private void LiveCommand_vubutton(NotificationCenter.Notification n)
    {
        string which = LiveArgString(n, 0, "share");
        CyclopsPanelButtons buttons = VuPanel();
        if (which is "lights" or "forget" or "forgetlights")
        {
            CyclopsLightingPanel lighting = buttons ? buttons.GetComponent<CyclopsLightingPanel>() : null;
            if (!lighting || !lighting.cyclopsRoot || !lighting.cyclopsRoot.TryGetNitroxId(out NitroxId cyclopsId))
            {
                Log.Warn($"{VU}vubutton: the {which} button is not there (no lighting panel with an id)");
                return;
            }
            if (which == "forgetlights")
            {
                bool shown = lighting.lightingOn;
                lighting.lightingOn = !shown;
                lighting.UpdateLightingButtons();
                Log.Info($"{VU}vubutton: forgetlights internal={shown}->{lighting.lightingOn} (no packet sent)");
                return;
            }
            if (which == "forget")
            {
                bool was = PowerShareSwitch.IsOn(cyclopsId);
                PowerShareSwitch.Set(cyclopsId, false);
                Log.Info($"{VU}vubutton: forget switch was={was} now=False (no packet sent)");
                return;
            }
            bool before = lighting.lightingOn;
            lighting.ToggleInternalLighting();
            Log.Info($"{VU}vubutton: lights clicked internal={before}->{lighting.lightingOn} flood={lighting.floodlightsOn}");
            return;
        }
        CyclopsPanelButtons.PanelButton button = !buttons ? null : which == "tow" ? buttons.TowButton : buttons.PowerShareButton;
        if (button == null || !button.Visible)
        {
            Log.Warn($"{VU}vubutton: the {which} button is not there (panel={(buttons ? "yes" : "no")} built={(buttons && buttons.Built)})");
            return;
        }
        button.Click();
        Log.Info($"{VU}vubutton: {which} clicked");
    }

    // ---- vuline, vukill

    /// <summary>vuline: the lines the local player would see under the exit prompt (power share and tow), the Cyclops that draw emergency power and the vehicles that are being charged.</summary>
    private void LiveCommand_vuline(NotificationCenter.Notification n)
    {
        bool power = PowerShare.TryGetPilotLine(out string powerLine);
        bool tow = VehicleTows.TryGetLine(out string towLine);
        List<string> emergency = [];
        List<string> charging = [];
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (subRoot.isCyclops && subRoot.TryGetNitroxId(out NitroxId cyclopsId) && PowerShare.TryGetEmergencyMark(cyclopsId, out string vehicleName))
            {
                emergency.Add($"{VuId(subRoot.gameObject)}:{vehicleName.Replace(' ', '_')}");
            }
        }
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            if (vehicle.TryGetNitroxId(out NitroxId vehicleId) && PowerShare.IsChargingMarked(vehicleId))
            {
                charging.Add(VuId(vehicle.gameObject));
            }
        }
        Log.Info($"{VU}vuline: power=\"{(power ? powerLine : "none")}\" tow=\"{(tow ? towLine : "none")}\" emergency={(emergency.Count > 0 ? string.Join(",", emergency) : "none")} charging={(charging.Count > 0 ? string.Join(",", charging) : "none")} t={VuSyncedTime()}");
    }

    /// <summary>vukill &lt;seamoth|prawn&gt; [id]: kills the nearest vehicle of that kind, or the one whose id (the 8 hex digits of vushare and vehicles) starts with the second word (the game's own death, so that OnKill runs: a rider has to be let go first).</summary>
    private void LiveCommand_vukill(NotificationCenter.Notification n)
    {
        string kind = LiveArgString(n, 0, "seamoth");
        string idPrefix = LiveArgString(n, 1, "");
        Vehicle target = null;
        float best = float.MaxValue;
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            bool wanted = (kind == "prawn" ? vehicle is Exosuit : vehicle is SeaMoth) && (idPrefix.Length == 0 || VuId(vehicle.gameObject).StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase));
            float distance = Player.main ? Vector3.Distance(Player.main.transform.position, vehicle.transform.position) : 0f;
            if (wanted && distance < best)
            {
                best = distance;
                target = vehicle;
            }
        }
        if (!target || !target.liveMixin)
        {
            Log.Warn($"{VU}vukill: no {kind} to kill");
            return;
        }
        Log.Info($"{VU}vukill: kind={kind} id={VuId(target.gameObject)} distance={VuNum(best)} killing");
        target.liveMixin.Kill(DamageType.Normal);
    }

    // ---- vutow

    /// <summary>vutow state|hook|cyclops|release|probe|track [seconds]: the tow hooks.</summary>
    private void LiveCommand_vutow(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        switch (what)
        {
            case "state":
                VuTowState();
                break;
            case "hook":
                VuTowHook();
                break;
            case "cyclops":
            case "cyclopshook":
                // The tow hook of a Cyclops is a button: the same path as a click on it (whoever simulates the Cyclops)
                VehicleTows.DebugCyclopsButton();
                Log.Info($"{VU}vutow: cyclops tow button pressed");
                break;
            case "release":
                VehicleTows.ReleaseLocal(TowReleaseReason.Hand);
                Log.Info($"{VU}vutow: released by hand");
                break;
            case "probe":
                VuTowProbe();
                break;
            case "track":
                StartCoroutine(VuTowTrackAsync(Mathf.Clamp(LiveArgFloat(n, 1, 10f), 1f, 120f)));
                break;
            default:
                Log.Warn($"{VU}vutow: unknown argument {what}");
                break;
        }
    }

    private static void VuTowHook()
    {
        SeaMoth seaMoth = Player.main && Player.main.GetVehicle() is SeaMoth piloted ? piloted : null;
        if (!seaMoth)
        {
            Log.Warn($"{VU}vutow: hook needs the player to pilot a Seamoth");
            return;
        }
        for (int slot = 0; slot < seaMoth.slotIDs.Length; slot++)
        {
            if (seaMoth.modules.GetTechTypeInSlot(seaMoth.slotIDs[slot]) == (TechType)CoopItems.SEAMOTH_TOW_HOOK)
            {
                // The key of the quick slot: the game's own path (cooldown, toggling, OnUpgradeModuleToggle)
                seaMoth.SlotKeyDown(slot);
                // the state right after the key, before VehicleTows.UntoggleNextFrame switches a slot off that hooked nothing
                Log.Info($"{VU}vutow: slot key {slot} pressed, toggled={seaMoth.IsToggled(slot)}");
                CoroutineHost.StartCoroutine(VuSlotSettled(seaMoth, slot));
                return;
            }
        }
        Log.Warn($"{VU}vutow: this Seamoth has no tow hook in a slot");
    }

    /// <summary>One second after the key of the tow hook's quick slot: is the slot still switched on (it is while the vehicle tows; it is switched off again one frame after a hooking that did not happen).</summary>
    private static IEnumerator VuSlotSettled(SeaMoth seaMoth, int slot)
    {
        yield return new WaitForSeconds(1f);
        Log.Info($"{VU}vutow: slot key {slot} settled, toggled={(seaMoth && seaMoth.IsToggled(slot))}");
    }

    private static void VuTowState()
    {
        int count = 0;
        foreach (VehicleTows.Tow tow in VehicleTows.Tows)
        {
            count++;
            NitroxEntity.TryGetObjectFrom(tow.TowerId, out GameObject towerObject);
            NitroxEntity.TryGetObjectFrom(tow.TowedId, out GameObject towedObject);
            Vehicle towed = towedObject ? towedObject.GetComponent<Vehicle>() : null;
            float length = -1f;
            if (towerObject && towedObject)
            {
                Vector3 towerForward = towerObject.TryGetComponent(out SubRoot cyclops) && cyclops.subAxis ? cyclops.subAxis.forward : towerObject.transform.forward;
                bool haveA = HullBounds.TryGetAnchor(towerObject.transform, towerForward, false, out Vector3 a);
                bool haveB = HullBounds.TryGetAnchor(towedObject.transform, towedObject.transform.forward, true, out Vector3 b);
                length = haveA && haveB ? Vector3.Distance(a, b) : Vector3.Distance(towerObject.transform.position, towedObject.transform.position);
            }
            Log.Info(string.Concat($"{VU}vutow: state tower={VuId(towerObject)} towerName={(towerObject ? towerObject.name : "gone")} towed={VuId(towedObject)} towedName={VuName(towed)} local={tow.Local} slot={tow.Slot} length={VuNum(length)} ",
                     $"rest={VuNum(TowRules.RestLength(VehicleUpgradeSettings.TowMaxDistance))} max={VuNum(VehicleUpgradeSettings.TowMaxDistance)} line={(tow.Line ? "yes" : "no")} cable={(tow.Cable ? "yes" : "no")} cableVisible={(tow.Cable && tow.Cable.Visible)} ",
                     $"cableMaterial={(tow.Cable ? tow.Cable.MaterialName : "none")} lockTower={VuLock(towerObject ? towerObject.transform : null)} lockTowed={VuLock(towed)} age={VuNum(Time.time - tow.StartedAt)} t={VuSyncedTime()}"));
        }
        Log.Info($"{VU}vutow: state done count={count} enabled={VehicleUpgradeSettings.Enabled} speedFactor={VuNum(VehicleUpgradeSettings.TowSpeedFactor)} maxDistance={VuNum(VehicleUpgradeSettings.TowMaxDistance)} attachRange={VuNum(TowRules.AttachRange(VehicleUpgradeSettings.TowMaxDistance))}");
    }

    private static void VuTowProbe()
    {
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            Rigidbody body = vehicle.useRigidbody;
            WorldForces forces = vehicle.worldForces;
            bool hull = HullBounds.TryGet(vehicle.transform, out Bounds box);
            Log.Info(string.Concat($"{VU}vutow: probe kind={(vehicle is SeaMoth ? "seamoth" : vehicle is Exosuit ? "prawn" : "vehicle")} id={VuId(vehicle.gameObject)} mass={(body ? VuNum(body.mass) : "none")} drag={(body ? VuNum(body.drag) : "none")} angularDrag={(body ? VuNum(body.angularDrag) : "none")} ",
                     $"constraints={(body ? body.constraints.ToString() : "none")} kinematic={(body && body.isKinematic)} forward={VuNum(vehicle.forwardForce)} backward={VuNum(vehicle.backwardForce)} sideward={VuNum(vehicle.sidewardForce)} vertical={VuNum(vehicle.verticalForce)} ",
                     $"gravityAbove={(forces ? VuNum(forces.aboveWaterGravity) : "none")} gravityUnder={(forces ? VuNum(forces.underwaterGravity) : "none")} dragAbove={(forces ? VuNum(forces.aboveWaterDrag) : "none")} dragUnder={(forces ? VuNum(forces.underwaterDrag) : "none")} ",
                     $"hull={(hull ? $"{VuVector(box.center)}/{VuVector(box.size)}" : "unknown")} docked={vehicle.docked} piloted={vehicle.GetPilotingMode()}"));
        }
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (!VuIsRealCyclops(subRoot))
            {
                continue;
            }
            Rigidbody body = subRoot.GetComponent<Rigidbody>();
            bool hull = HullBounds.TryGet(subRoot.transform, out Bounds box);
            Log.Info($"{VU}vutow: probe kind=cyclops id={VuId(subRoot.gameObject)} mass={(body ? VuNum(body.mass) : "none")} drag={(body ? VuNum(body.drag) : "none")} hull={(hull ? $"{VuVector(box.center)}/{VuVector(box.size)}" : "unknown")} axis={(subRoot.subAxis ? VuVector(subRoot.subAxis.forward) : "none")} hook={VehicleTows.HasCyclopsHook(subRoot)}");
        }
        Material material = TowCableMaterial.Get();
        Log.Info($"{VU}vutow: probe cableMaterial={(material ? $"{material.name}/{material.shader.name}" : "none")}");
    }

    private IEnumerator VuTowTrackAsync(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            foreach (VehicleTows.Tow tow in VehicleTows.Tows)
            {
                if (NitroxEntity.TryGetObjectFrom(tow.TowerId, out GameObject tower) && NitroxEntity.TryGetObjectFrom(tow.TowedId, out GameObject towed))
                {
                    Log.Info(string.Concat($"{VU}vutow: track local={tow.Local} towerPos={VuVector(tower.transform.position)} towedPos={VuVector(towed.transform.position)} towedRot={VuNum(towed.transform.eulerAngles.y)} distance={VuNum(Vector3.Distance(tower.transform.position, towed.transform.position))} ",
                             $"cableVisible={(tow.Cable && tow.Cable.Visible)} t={VuSyncedTime()}"));
                }
            }
            yield return null;
        }
        Log.Info($"{VU}vutow: track done");
    }

    // ---- vurider, vubar

    /// <summary>vurider state|hold|release|track [seconds]|aim bar|body|deny: the rider bar.</summary>
    private void LiveCommand_vurider(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        switch (what)
        {
            case "state":
                VuRiderState();
                break;
            case "hold":
                VuRiderHold();
                break;
            case "release":
                RiderBars.ReleaseLocal(RiderReleaseReason.Hand, true);
                Log.Info($"{VU}vurider: released by hand");
                break;
            case "leavesub":
                VuRiderLeaveSub();
                break;
            case "track":
                StartCoroutine(VuRiderTrackAsync(Mathf.Clamp(LiveArgFloat(n, 1, 10f), 1f, 120f)));
                break;
            case "aim":
                StartCoroutine(VuRiderAimAsync(LiveArgString(n, 1, "bar")));
                break;
            case "deny":
                StartCoroutine(VuRiderDenyAsync());
                break;
            default:
                Log.Warn($"{VU}vurider: unknown argument {what}");
                break;
        }
    }

    private static SeamothRiderBar VuNearestBar()
    {
        Player player = Player.main;
        SeamothRiderBar best = null;
        float bestDistance = float.MaxValue;
        foreach (SeamothRiderBar bar in FindObjectsOfType<SeamothRiderBar>())
        {
            if (!bar.IsBuilt)
            {
                continue;
            }
            float distance = player ? Vector3.Distance(player.transform.position, bar.Anchor.position) : 0f;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = bar;
            }
        }
        return best;
    }

    private static void VuRiderHold()
    {
        SeamothRiderBar bar = VuNearestBar();
        if (!bar)
        {
            Log.Warn($"{VU}vurider: no built bar in the world");
            return;
        }
        Log.Info($"{VU}vurider: hold requested on {VuId(bar.gameObject)} decision={RiderBars.DecisionFor(bar)} distance={VuNum(Vector3.Distance(Player.main.transform.position, bar.Anchor.position))}");
        RiderBars.RequestHold(bar);
    }

    /// <summary>
    /// vurider leavesub: takes the local player out of the sub the game still counts them in. A respawned player is put into the Cyclops by the game; the harness then teleports them away without a hatch (the game only
    /// clears the current sub through the hatch's trigger), so the Rider Bar sees "in a sub" (decision No) and the player cannot be attacked.
    /// </summary>
    private static void VuRiderLeaveSub()
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Warn($"{VU}vurider: leavesub: no player");
            return;
        }
        string before = player.currentSub ? player.currentSub.name.Replace(' ', '_') : "none";
        player.SetCurrentSub(null, true);
        Log.Info($"{VU}vurider: leavesub before={before} after={(player.currentSub ? player.currentSub.name.Replace(' ', '_') : "none")} escapePod={player.escapePod.value} canBeAttacked={player.CanBeAttacked()}");
    }

    private static void VuRiderState()
    {
        Player player = Player.main;
        Log.Info(string.Concat($"{VU}vurider: state local={RiderBars.IsLocalRiding} mode={(player ? player.GetMode().ToString() : "none")} parent={(player && player.transform.parent ? player.transform.parent.name : "none")} oxygen={(player ? VuNum(player.GetOxygenAvailable()) : "none")}/{(player ? VuNum(player.GetOxygenCapacity()) : "none")} ",
                 $"underwater={(player && player.IsUnderwater())} canBeAttacked={(player && player.CanBeAttacked())} inVehicle={(player && player.GetVehicle())} t={VuSyncedTime()} ",
                 $"health={(player && player.liveMixin ? VuNum(player.liveMixin.health) : "none")} sub={(player && player.currentSub ? player.currentSub.name.Replace(' ', '_') : "none")} escapePod={(player && player.escapePod.value)} justSpawned={(player && player.justSpawned)}"));
        foreach (SeamothRiderBar bar in FindObjectsOfType<SeamothRiderBar>())
        {
            SessionId session = default;
            bool hasRider = bar.VehicleId != null && RiderBars.TryGetRider(bar.VehicleId, out session);
            Log.Info($"{VU}vurider: bar vehicle={VuId(bar.gameObject)} installed={RiderBars.IsBarInstalled(bar.Vehicle)} built={bar.IsBuilt} rider={(hasRider ? session.ToString() : "none")} docked={bar.Vehicle.docked} lock={VuLock(bar.Vehicle)}");
        }
        foreach (RemotePlayer remote in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
        {
            Log.Info($"{VU}vurider: remote name={remote.PlayerName} session={remote.SessionId} onBar={(remote.RiderBar ? VuId(remote.RiderBar.gameObject) : "none")} canBeAttacked={remote.CanBeAttacked()} swimmingPose={remote.PoseTracker.Swimming} parent={(remote.Body && remote.Body.transform.parent ? remote.Body.transform.parent.name : "none")}");
        }
    }

    private IEnumerator VuRiderTrackAsync(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            Player player = Player.main;
            if (RiderBars.IsLocalRiding && player)
            {
                Transform seamoth = RiderBars.LocalBar.Vehicle.transform;
                Vector3 local = seamoth.InverseTransformPoint(player.transform.position);
                Log.Info(string.Concat($"{VU}vurider: track side=local relative={VuVector(local)} seamothPos={VuVector(seamoth.position)} speed={VuNum(RiderBars.LocalBar.Vehicle.useRigidbody ? RiderBars.LocalBar.Vehicle.useRigidbody.velocity.magnitude : 0f)} oxygen={VuNum(player.GetOxygenAvailable())} ",
                         $"mode={player.GetMode()} underwater={player.IsUnderwater()} t={VuSyncedTime()}"));
            }
            foreach (RemotePlayer remote in NitroxServiceLocator.LocateService<PlayerManager>().GetAll())
            {
                if (remote.RiderBar && remote.Body)
                {
                    Transform seamoth = remote.RiderBar.transform;
                    Vector3 local = seamoth.InverseTransformPoint(remote.Body.transform.position);
                    Log.Info($"{VU}vurider: track side=remote name={remote.PlayerName} relative={VuVector(local)} seamothPos={VuVector(seamoth.position)} t={VuSyncedTime()}");
                }
            }
            yield return null;
        }
        Log.Info($"{VU}vurider: track done");
    }

    /// <summary>vubar &lt;x&gt; &lt;y&gt; &lt;z&gt;: moves the bar and the rider's place on the nearest Seamoth by an offset in the Seamoth's space (to find a good place for the bar live).</summary>
    private void LiveCommand_vubar(NotificationCenter.Notification n)
    {
        SeamothRiderBar bar = VuNearestBar();
        if (!bar)
        {
            Log.Warn($"{VU}vubar: no built bar in the world");
            return;
        }
        Vector3 offset = new(LiveArgFloat(n, 0, 0f), LiveArgFloat(n, 1, 0f), LiveArgFloat(n, 2, 0f));
        bar.Nudge(offset);
        Log.Info($"{VU}vubar: nudged {VuId(bar.gameObject)} by {VuVector(offset)} built={bar.IsBuilt}");
    }
}
#endif
