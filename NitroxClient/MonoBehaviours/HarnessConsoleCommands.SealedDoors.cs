#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NitroxClient.GameLogic;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "sealeddoors" : the laser-cut doors of wrecks and of the Aurora and the damaged wall panels that unlock the other doors (bug 10 of the first
// play sessions). Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]"
// (at most two arguments, all lower case). Every command logs "[SyncHarness] <name>: key=value ..." lines; numbers are written with the invariant culture.
//
//   sealedprobe [radius] [count]   the nearest Sealed objects (laser-cut doors, sealed supply crates) within the radius (default 80 m, at most count = 6): one line each with name, NitroxId, position,
//                                  distance, sealed / opened amount, the StarshipDoor facts (open method, lock, what the vanilla hand text would be), the LaserCutObject facts, the colliders, and whether
//                                  the local hand is on it right now (aimed=True: then reticle='<what the hand reticle shows>'). The index of a line is what sealedcut and sealedlook take
//   sealedcut [which] [amount]     cuts the object the way the laser cutter does (Sealed.Weld(amount), then the cutting FX while it is not open): which = index of the last sealedprobe, "nearest"
//                                  (default) or the first characters of a NitroxId; amount = a number or "all" (default: what is left). Skips the cutter's battery and its "from the front" rule
//   sealedlook [which] [seconds]   puts the local player 1.5 m from the object, aims the camera at it and logs for that many seconds (default 3) every 0.5 s what the game's own hover finds there:
//                                  Targeting.GetTarget, GUIHand.activeTarget, the hand reticle text, and whether it equals the text that the vanilla code would write (match=True)
//   panelprobe [radius] [count]    the same for the damaged wall panels (WeldableWallPanelGeneric): health, repaired, whether LiveMixin's looping damage effect (the sparks) exists, whether the
//                                  vanilla rule would spawn it, the particle systems under it, the Flashes accessibility option (which switches flashing effects off), the door next to it
//   panelspark [which]             spawns the sparks of a panel the way LiveMixin.Start does when the rule says so (index of the last panelprobe or "nearest"), and says why it did not
//   doorprobe [radius] [count]     the same for ALL starship doors (default 40 m, at most 8): open method, lock, what the vanilla hand text would be ("nothing" for a locked manual door: the plain
//                                  closed door with no interaction), colliders, the nearest damaged wall panel and whether the vanilla UnlockDoor broadcast of that panel can reach the door
//   doorlook [which] [seconds]     sealedlook for a door of the last doorprobe (index, "nearest" or the first characters of a NitroxId)
// sealedprobe and doorprobe also say whether the world data of this client holds the object twice: stacked=<other objects of the kind within half a metre> idUsers=<objects that carry its NitroxId>
// world=<ok|stacked|idshared|stacked+idshared> (the server generated a wreck a second time with other ids, a harness script that is not part of this repository "Outside this change")
public partial class HarnessConsoleCommands
{
    private const float SD_DEFAULT_RADIUS = 80f;
    private const int SD_DEFAULT_COUNT = 6;
    private const float SD_DOOR_RADIUS = 40f;
    private const int SD_DOOR_COUNT = 8;
    private const float SD_STACKED_DISTANCE = 0.5f;
    private const float SD_PANEL_REACH = 20f;

    // The objects that the last sealedprobe / panelprobe / doorprobe listed, so that the index of a line can be used by the next command
    private readonly List<Sealed> sdLastSealed = new List<Sealed>();
    private readonly List<WeldableWallPanelGeneric> sdLastPanels = new List<WeldableWallPanelGeneric>();
    private readonly List<StarshipDoor> sdLastDoors = new List<StarshipDoor>();

    private static string SdF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SdVec(Vector3 value)
    {
        return $"{SdF(value.x)},{SdF(value.y)},{SdF(value.z)}";
    }

    private static Vector3 SdPlayerPosition()
    {
        return Player.main ? Player.main.transform.position : Vector3.zero;
    }

    // The NitroxId on the object itself, or "parent:<id>" when only an ancestor has one (the Weld patch only looks at the object itself: a Sealed without an id of its own does not sync), or "none"
    private static string SdIdOf(Component component)
    {
        NitroxEntity own = component.GetComponent<NitroxEntity>();
        if (own && own.Id != null)
        {
            return own.Id.ToString();
        }

        NitroxEntity above = component.GetComponentInParent<NitroxEntity>();
        return above && above.Id != null ? $"parent:{above.Id}" : "none";
    }

    private static string SdOpenMethodName(int method)
    {
        switch (method)
        {
            case StarshipDoorRules.MANUAL:
                return "Manual";
            case StarshipDoorRules.SEALED:
                return "Sealed";
            case StarshipDoorRules.KEYCARD:
                return "Keycard";
            case StarshipDoorRules.POWERCELL:
                return "Powercell";
            default:
                return method.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string SdState(Sealed sealedObject)
    {
        return $"{(sealedObject._sealed ? "sealed" : "open")}:{SdF(sealedObject.openedAmount)}";
    }

    private static string SdQuote(string text)
    {
        return $"'{(text ?? "").Replace('\'', '"').Replace('\r', ' ').Replace('\n', ' ')}'";
    }

    // How many objects of this client carry each NitroxId: a count above 1 is two entities of the world data with one id (the id registry keeps only the last object)
    private static Dictionary<string, int> SdIdCounts()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (NitroxEntity entity in UnityEngine.Object.FindObjectsOfType<NitroxEntity>())
        {
            if (!entity || entity.Id == null)
            {
                continue;
            }

            string key = entity.Id.ToString();
            counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
        }
        return counts;
    }

    private static int SdIdUsers(Component component, Dictionary<string, int> idCounts)
    {
        NitroxEntity own = component.GetComponent<NitroxEntity>();
        return own && own.Id != null && idCounts.TryGetValue(own.Id.ToString(), out int count) ? count : 0;
    }

    // Other objects of the same kind within half a metre of this one: the same wreck twice in the world data
    private static int SdStackedCopies<T>(T item, T[] all) where T : Component
    {
        int copies = 0;
        float limit = SD_STACKED_DISTANCE * SD_STACKED_DISTANCE;
        foreach (T other in all)
        {
            if (other && other.GetInstanceID() != item.GetInstanceID() && (other.transform.position - item.transform.position).sqrMagnitude < limit)
            {
                copies++;
            }
        }
        return copies;
    }

    private static string SdWorldData<T>(T item, T[] all, Dictionary<string, int> idCounts) where T : Component
    {
        int stacked = SdStackedCopies(item, all);
        int idUsers = SdIdUsers(item, idCounts);
        return $"stacked={stacked} idUsers={idUsers} world={StarshipDoorRules.DescribeWorldData(stacked, idUsers)}";
    }

    private static List<T> SdNearest<T>(float radius, int count) where T : Component
    {
        Vector3 origin = SdPlayerPosition();
        List<T> found = new List<T>(UnityEngine.Object.FindObjectsOfType<T>());
        found.RemoveAll(candidate => !candidate || (candidate.transform.position - origin).magnitude > radius);
        found.Sort((a, b) => (a.transform.position - origin).sqrMagnitude.CompareTo((b.transform.position - origin).sqrMagnitude));
        if (found.Count > count)
        {
            found.RemoveRange(count, found.Count - count);
        }
        return found;
    }

    // "nearest" (within 300 m), a small number (the index in the last list), or the first characters of a NitroxId (at least 4)
    private static T SdPick<T>(string which, List<T> lastList) where T : Component
    {
        if (which == "nearest" || which.Length == 0)
        {
            List<T> nearest = SdNearest<T>(300f, 1);
            return nearest.Count > 0 ? nearest[0] : null;
        }

        if (which.Length <= 3 && int.TryParse(which, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return index >= 0 && index < lastList.Count && lastList[index] ? lastList[index] : null;
        }

        string prefix = which.ToLowerInvariant();
        foreach (T candidate in UnityEngine.Object.FindObjectsOfType<T>())
        {
            if (candidate && SdIdOf(candidate).ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal))
            {
                return candidate;
            }
        }
        return null;
    }

    // What the hand reticle shows right now (the text components, after the last LateUpdate) and whether the game's hand target is this object
    private static string SdReticle()
    {
        if (!HandReticle.main)
        {
            return "reticle=none";
        }

        string main = HandReticle.main.compTextHand ? HandReticle.main.compTextHand.text : null;
        string sub = HandReticle.main.compTextHandSubscript ? HandReticle.main.compTextHandSubscript.text : null;
        return $"reticle={SdQuote(main)}|{SdQuote(sub)}";
    }

    private static GameObject SdHandTarget()
    {
        return Player.main && Player.main.guiHand ? Player.main.guiHand.activeTarget : null;
    }

    private static string SdLocalized(string key)
    {
        try
        {
            return Language.main ? Language.main.Get(key) : key;
        }
        catch (Exception)
        {
            return key;
        }
    }

    private static string SdDescribeSealed(int index, Sealed sealedObject, Vector3 origin, Sealed[] all, Dictionary<string, int> idCounts)
    {
        StringBuilder line = new StringBuilder();
        Transform sealedTransform = sealedObject.transform;
        line.Append($"item={index} name={sealedObject.name} id={SdIdOf(sealedObject)} pos={SdVec(sealedTransform.position)} dist={SdF(Vector3.Distance(origin, sealedTransform.position))}");
        line.Append($" activeInHierarchy={sealedObject.gameObject.activeInHierarchy} enabled={sealedObject.enabled} sealed={sealedObject._sealed} opened={SdF(sealedObject.openedAmount)}/{SdF(sealedObject.maxOpenedAmount)} frontOnly={sealedObject.requireOpenFromFront}");

        StarshipDoor door = sealedObject.GetComponent<StarshipDoor>();
        SupplyCrate crate = sealedObject.GetComponent<SupplyCrate>();
        if (door)
        {
            int method = (int)door.doorOpenMethod;
            StarshipDoorRules.HandText expected = StarshipDoorRules.Describe(method, door.doorLocked, door.doorOpen, door.openText, door.closeText);
            string expectedText = expected.IsEmpty ? "nothing" : SdQuote($"{SdLocalized(expected.Main)}|{SdLocalized(expected.Sub)}");
            line.Append($" kind=door method={SdOpenMethodName(method)} locked={door.doorLocked} doorOpen={door.doorOpen} doorEnabled={door.enabled} doorStarted={(bool)door.sealedComponent}");
            line.Append($" expectHand={expectedText} ring={expected.ShowsProgress}");
        }
        else if (crate)
        {
            string expectedText = crate.open ? "take" : SdQuote($"{SdLocalized("Sealed_SupplyCrate")}|{SdLocalized("SealedInstructions")}");
            line.Append($" kind=crate crateOpen={crate.open} crateStarted={(bool)crate.sealedComp}");
            line.Append($" expectHand={expectedText}");
        }
        else
        {
            line.Append(" kind=other");
        }

        LaserCutObject laser = sealedObject.GetComponent<LaserCutObject>();
        if (laser)
        {
            Vector3 stored = laser.matchingNodePos;
            line.Append($" laser=yes cutOpen={laser.isCutOpen} uncutActive={(laser.uncutObject ? laser.uncutObject.activeSelf.ToString() : "none")} cutActive={(laser.cutObject ? laser.cutObject.activeSelf.ToString() : "none")} loosePiece={(bool)laser.cutFloatAwayObject}");
            line.Append($" nodeStored={SdVec(stored)} nodeStale={LaserCutFxRules.IsStaleNodePosition(stored == Vector3.zero, (stored - sealedTransform.position).sqrMagnitude)}");
        }
        else
        {
            line.Append(" laser=no");
        }

        Collider[] colliders = sealedObject.GetComponentsInChildren<Collider>(false);
        int solid = colliders.Count(collider => collider.enabled && !collider.isTrigger);
        int triggers = colliders.Count(collider => collider.enabled && collider.isTrigger);
        GameObject handTarget = SdHandTarget();
        bool aimed = handTarget && handTarget.transform.IsChildOf(sealedTransform);
        line.Append($" colliders={solid} triggers={triggers} {SdWorldData(sealedObject, all, idCounts)} aimed={aimed}");
        if (aimed)
        {
            line.Append($" {SdReticle()}");
        }

        return line.ToString();
    }

    private void LiveCommand_sealedprobe(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, SD_DEFAULT_RADIUS);
            int count = Mathf.Max(1, (int)LiveArgFloat(n, 1, SD_DEFAULT_COUNT));
            Vector3 origin = SdPlayerPosition();
            Sealed[] all = UnityEngine.Object.FindObjectsOfType<Sealed>();
            Dictionary<string, int> idCounts = SdIdCounts();
            sdLastSealed.Clear();
            sdLastSealed.AddRange(SdNearest<Sealed>(radius, count));
            Log.Info($"[SyncHarness] sealedprobe: at={SdVec(origin)} radius={SdF(radius)} listed={sdLastSealed.Count} loadedSealedObjects={all.Length} flashes={MiscSettings.flashes}");
            for (int i = 0; i < sdLastSealed.Count; i++)
            {
                Log.Info($"[SyncHarness] sealedprobe: {SdDescribeSealed(i, sdLastSealed[i], origin, all, idCounts)}");
            }

            int open = sdLastSealed.Count(sealedObject => !sealedObject._sealed);
            int duplicated = sdLastSealed.Count(sealedObject => StarshipDoorRules.DescribeWorldData(SdStackedCopies(sealedObject, all), SdIdUsers(sealedObject, idCounts)) != StarshipDoorRules.WORLD_DATA_OK);
            Log.Info($"[SyncHarness] sealedprobe: summary listed={sdLastSealed.Count} sealed={sdLastSealed.Count - open} open={open} duplicated={duplicated}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sealedprobe: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_sealedcut(NotificationCenter.Notification n)
    {
        try
        {
            Sealed target = SdPick(LiveArgString(n, 0, "nearest"), sdLastSealed);
            if (!target)
            {
                Log.Warn("[SyncHarness] sealedcut: command error, no Sealed object found for that index / id (run sealedprobe first, or use nearest)");
                return;
            }

            float remaining = Mathf.Max(0f, target.maxOpenedAmount - target.openedAmount);
            string amountText = LiveArgString(n, 1, "all");
            float amount = amountText == "all" ? remaining : LiveArgFloat(n, 1, remaining);
            string before = SdState(target);

            // What LaserCutter.LaserCut does with its target (ildump LaserCutter::LaserCut): Sealed.Weld(healthPerWeld), and the cutting FX of the object while it is not open yet.
            // Sealed.Weld is patched by Nitrox (Sealed_Weld_Patch reports the new state), exactly as when the cutter calls it.
            target.Weld(amount);
            LaserCutObject laser = target.GetComponent<LaserCutObject>();
            if (target.openedAmount < target.maxOpenedAmount && laser)
            {
                laser.ActivateFX();
            }

            Log.Info($"[SyncHarness] sealedcut: name={target.name} id={SdIdOf(target)} pos={SdVec(target.transform.position)} amount={SdF(amount)} before={before} after={SdState(target)} laser={(laser ? "yes" : "no")}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sealedcut: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_sealedlook(NotificationCenter.Notification n)
    {
        try
        {
            if (!Player.main || !MainCamera.camera || !MainCameraControl.main)
            {
                Log.Warn("[SyncHarness] sealedlook: command error, the client is not in the world");
                return;
            }

            Sealed target = SdPick(LiveArgString(n, 0, "nearest"), sdLastSealed);
            if (!target)
            {
                Log.Warn("[SyncHarness] sealedlook: command error, no Sealed object found for that index / id (run sealedprobe first, or use nearest)");
                return;
            }

            StartCoroutine(SdLook("sealedlook", target, Mathf.Clamp(LiveArgFloat(n, 1, 3f), 0.5f, 60f)));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] sealedlook: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    // The middle of the solid colliders of the object, else its root
    private static Vector3 SdCenter(Component component)
    {
        Collider[] colliders = component.GetComponentsInChildren<Collider>(false).Where(collider => collider.enabled && !collider.isTrigger).ToArray();
        if (colliders.Length == 0)
        {
            return component.transform.position;
        }

        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
        {
            bounds.Encapsulate(colliders[i].bounds);
        }
        return bounds.center;
    }

    // command: the name the lines are written under ("sealedlook" for a Sealed object, "doorlook" for a StarshipDoor); target: that object
    private static IEnumerator SdLook(string command, Component target, float seconds)
    {
        Vector3 center = SdCenter(target);
        Sealed sealedTarget = target.GetComponent<Sealed>();
        Vector3 away = Player.main.transform.position - center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
        {
            away = target.transform.forward;
            away.y = 0f;
        }

        Vector3 standAt = center + away.normalized * 1.5f;
        Player.main.OnPlayerPositionCheat();
        Player.main.SetPosition(standAt);
        Log.Info($"[SyncHarness] {command}: name={target.name} id={SdIdOf(target)} center={SdVec(center)} standing={SdVec(standAt)} for {SdF(seconds)} s");
        yield return null;
        yield return null;

        for (float elapsed = 0f; elapsed <= seconds && target; elapsed += 0.5f)
        {
            // The camera is aimed again at every sample: the player drifts a little in the water
            Vector3 toTarget = center - MainCamera.camera.transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                MainCameraControl.main.rotationX = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                MainCameraControl.main.rotationY = -Mathf.Asin(-toTarget.y / toTarget.magnitude) * Mathf.Rad2Deg;
            }

            yield return new WaitForSeconds(0.25f);

            GameObject viaTargeting = null;
            float targetingDistance = 0f;
            bool targeted = Targeting.GetTarget(Player.main.gameObject, GUIHand.kUseDistance, out viaTargeting, out targetingDistance);
            GameObject handTarget = SdHandTarget();
            IHandTarget handTargetComponent = handTarget ? handTarget.GetComponent<IHandTarget>() : null;
            bool aimed = handTarget && handTarget.transform.IsChildOf(target.transform);

            string shown = HandReticle.main && HandReticle.main.compTextHand ? HandReticle.main.compTextHand.text : "";
            string match = "n/a";
            StarshipDoor door = target.GetComponent<StarshipDoor>();
            if (door && aimed)
            {
                StarshipDoorRules.HandText expected = StarshipDoorRules.Describe((int)door.doorOpenMethod, door.doorLocked, door.doorOpen, door.openText, door.closeText);
                match = (expected.IsEmpty ? string.IsNullOrEmpty(shown) : shown == SdLocalized(expected.Main)).ToString();
            }

            Log.Info($"[SyncHarness] {command}: t={SdF(elapsed)} targeting={(targeted && viaTargeting ? viaTargeting.name : "none")} targetingDistance={SdF(targetingDistance)} guiHandTarget={(handTarget ? handTarget.name : "none")} handTargetType={(handTargetComponent != null ? handTargetComponent.GetType().Name : "none")} aimedAtThisObject={aimed} {SdReticle()} match={match} state={(sealedTarget ? SdState(sealedTarget) : "n/a")}");
            yield return new WaitForSeconds(0.25f);
        }

        Log.Info($"[SyncHarness] {command}: done");
    }

    private static string SdDescribePanel(int index, WeldableWallPanelGeneric panel, Vector3 origin)
    {
        StringBuilder line = new StringBuilder();
        LiveMixin liveMixin = panel.liveMixin;
        line.Append($"item={index} name={panel.name} id={SdIdOf(panel)} pos={SdVec(panel.transform.position)} dist={SdF(Vector3.Distance(origin, panel.transform.position))} activeInHierarchy={panel.gameObject.activeInHierarchy}");
        line.Append($" repaired={panel.repaired} updatePanel={panel.updatePanel} started={(bool)panel.sendMessageFrom}");
        if (!liveMixin)
        {
            line.Append(" liveMixin=none");
            return line.ToString();
        }

        GameObject effectPrefab = liveMixin.loopingDamageEffect;
        GameObject effectObject = liveMixin.loopingDamageEffectObj;
        float fraction = liveMixin.GetHealthFraction();
        float below = liveMixin.loopEffectBelowPercent;
#pragma warning disable CS0618 // LiveMixin.startHealthPercent is obsolete, the probe only reads what the prefab says
        line.Append($" health={SdF(liveMixin.health)}/{SdF(liveMixin.maxHealth)} startHealthPercent={SdF(liveMixin.startHealthPercent)} canResurrect={liveMixin.canResurrect} weldable={liveMixin.weldable}");
#pragma warning restore CS0618
        line.Append($" sparkPrefab={(bool)effectPrefab} below={SdF(below)} fraction={SdF(fraction)} sparksObject={(bool)effectObject} vanillaRuleSaysSparks={RemoteHitRules.ShouldSpawnLoopingEffect(fraction, below, effectPrefab, effectObject)}");
        if (effectObject)
        {
            ParticleSystem[] particles = effectObject.GetComponentsInChildren<ParticleSystem>(true);
            line.Append($" sparksActive={effectObject.activeInHierarchy} particleSystems={particles.Length} playing={particles.Count(system => system.isPlaying)}");
        }

        line.Append($" flashes={MiscSettings.flashes} animator={(bool)panel.animator}");
        Transform parent = panel.transform.parent;
        StarshipDoor door = parent ? parent.GetComponentInChildren<StarshipDoor>() : null;
        if (door)
        {
            line.Append($" door={door.name} doorMethod={SdOpenMethodName((int)door.doorOpenMethod)} doorLocked={door.doorLocked} doorOpen={door.doorOpen} doorId={SdIdOf(door)}");
        }
        else
        {
            line.Append(" door=none");
        }

        return line.ToString();
    }

    private void LiveCommand_panelprobe(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, SD_DEFAULT_RADIUS);
            int count = Mathf.Max(1, (int)LiveArgFloat(n, 1, SD_DEFAULT_COUNT));
            Vector3 origin = SdPlayerPosition();
            sdLastPanels.Clear();
            sdLastPanels.AddRange(SdNearest<WeldableWallPanelGeneric>(radius, count));
            Log.Info($"[SyncHarness] panelprobe: at={SdVec(origin)} radius={SdF(radius)} listed={sdLastPanels.Count} flashes={MiscSettings.flashes}");
            for (int i = 0; i < sdLastPanels.Count; i++)
            {
                Log.Info($"[SyncHarness] panelprobe: {SdDescribePanel(i, sdLastPanels[i], origin)}");
            }
            Log.Info($"[SyncHarness] panelprobe: summary listed={sdLastPanels.Count} repaired={sdLastPanels.Count(panel => panel.repaired)}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] panelprobe: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_panelspark(NotificationCenter.Notification n)
    {
        try
        {
            WeldableWallPanelGeneric panel = SdPick(LiveArgString(n, 0, "nearest"), sdLastPanels);
            if (!panel || !panel.liveMixin)
            {
                Log.Warn("[SyncHarness] panelspark: command error, no panel found for that index / id (run panelprobe first, or use nearest)");
                return;
            }

            LiveMixin liveMixin = panel.liveMixin;
            GameObject effectPrefab = liveMixin.loopingDamageEffect;
            float fraction = liveMixin.GetHealthFraction();
            float below = liveMixin.loopEffectBelowPercent;
            bool rule = RemoteHitRules.ShouldSpawnLoopingEffect(fraction, below, effectPrefab, liveMixin.loopingDamageEffectObj);
            if (rule)
            {
                // What LiveMixin.Start does
                liveMixin.loopingDamageEffectObj = global::Utils.SpawnZeroedAt(effectPrefab, liveMixin.transform, false);
            }

            Log.Info($"[SyncHarness] panelspark: name={panel.name} id={SdIdOf(panel)} rule={rule} sparkPrefab={(bool)effectPrefab} sparksObject={(bool)liveMixin.loopingDamageEffectObj} fraction={SdF(fraction)} below={SdF(below)} flashes={MiscSettings.flashes}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] panelspark: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    // The damaged wall panel nearest to a door (within SD_PANEL_REACH) and whether the vanilla repair can reach the door: WeldableWallPanelGeneric sends "UnlockDoor" with BroadcastMessage from
    // its parent (sendMessageFrom, set in Start), which only reaches the objects below that parent that exist at that moment
    private static string SdPanelOf(StarshipDoor door, WeldableWallPanelGeneric[] panels)
    {
        WeldableWallPanelGeneric best = null;
        float bestDistance = SD_PANEL_REACH;
        foreach (WeldableWallPanelGeneric panel in panels)
        {
            if (!panel)
            {
                continue;
            }

            float distance = Vector3.Distance(panel.transform.position, door.transform.position);
            if (distance < bestDistance)
            {
                best = panel;
                bestDistance = distance;
            }
        }

        if (!best)
        {
            return "panel=none";
        }

        Transform root = best.transform.parent ? best.transform.parent : best.transform;
        bool reaches = door.transform.IsChildOf(root);
        float health = best.liveMixin ? best.liveMixin.health : -1f;
        return $"panel={best.name} panelDist={SdF(bestDistance)} panelRepaired={best.repaired} panelHealth={SdF(health)} unlockReaches={reaches}";
    }

    private static string SdDescribeDoor(int index, StarshipDoor door, Vector3 origin, StarshipDoor[] all, Dictionary<string, int> idCounts, WeldableWallPanelGeneric[] panels)
    {
        StringBuilder line = new StringBuilder();
        Transform doorTransform = door.transform;
        int method = (int)door.doorOpenMethod;
        StarshipDoorRules.HandText expected = StarshipDoorRules.Describe(method, door.doorLocked, door.doorOpen, door.openText, door.closeText);
        string expectedText = expected.IsEmpty ? "nothing" : SdQuote($"{SdLocalized(expected.Main)}|{SdLocalized(expected.Sub)}");
        line.Append($"item={index} name={door.name} id={SdIdOf(door)} pos={SdVec(doorTransform.position)} dist={SdF(Vector3.Distance(origin, doorTransform.position))}");
        line.Append($" activeInHierarchy={door.gameObject.activeInHierarchy} enabled={door.enabled} method={SdOpenMethodName(method)} locked={door.doorLocked} doorOpen={door.doorOpen}");
        line.Append($" expectHand={expectedText} ring={expected.ShowsProgress}");

        Collider[] colliders = door.GetComponentsInChildren<Collider>(false);
        int solid = colliders.Count(collider => collider.enabled && !collider.isTrigger);
        int triggers = colliders.Count(collider => collider.enabled && collider.isTrigger);
        GameObject handTarget = SdHandTarget();
        bool aimed = handTarget && handTarget.transform.IsChildOf(doorTransform);
        line.Append($" colliders={solid} triggers={triggers} {SdWorldData(door, all, idCounts)} {SdPanelOf(door, panels)} aimed={aimed}");
        if (aimed)
        {
            line.Append($" {SdReticle()}");
        }

        return line.ToString();
    }

    private void LiveCommand_doorprobe(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, SD_DOOR_RADIUS);
            int count = Mathf.Max(1, (int)LiveArgFloat(n, 1, SD_DOOR_COUNT));
            Vector3 origin = SdPlayerPosition();
            StarshipDoor[] all = UnityEngine.Object.FindObjectsOfType<StarshipDoor>();
            WeldableWallPanelGeneric[] panels = UnityEngine.Object.FindObjectsOfType<WeldableWallPanelGeneric>();
            Dictionary<string, int> idCounts = SdIdCounts();
            sdLastDoors.Clear();
            sdLastDoors.AddRange(SdNearest<StarshipDoor>(radius, count));
            Log.Info($"[SyncHarness] doorprobe: at={SdVec(origin)} radius={SdF(radius)} listed={sdLastDoors.Count} loadedDoors={all.Length} loadedPanels={panels.Length}");
            for (int i = 0; i < sdLastDoors.Count; i++)
            {
                Log.Info($"[SyncHarness] doorprobe: {SdDescribeDoor(i, sdLastDoors[i], origin, all, idCounts, panels)}");
            }

            // plain = the doors that write no hand text at all (a manual door that is locked), duplicated = stacked copies or a NitroxId shared with another object
            int plain = sdLastDoors.Count(door => StarshipDoorRules.Describe((int)door.doorOpenMethod, door.doorLocked, door.doorOpen, door.openText, door.closeText).IsEmpty);
            int sealedDoors = sdLastDoors.Count(door => (int)door.doorOpenMethod == StarshipDoorRules.SEALED);
            int duplicated = sdLastDoors.Count(door => StarshipDoorRules.DescribeWorldData(SdStackedCopies(door, all), SdIdUsers(door, idCounts)) != StarshipDoorRules.WORLD_DATA_OK);
            Log.Info($"[SyncHarness] doorprobe: summary listed={sdLastDoors.Count} plain={plain} sealed={sealedDoors} duplicated={duplicated}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] doorprobe: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_doorlook(NotificationCenter.Notification n)
    {
        try
        {
            if (!Player.main || !MainCamera.camera || !MainCameraControl.main)
            {
                Log.Warn("[SyncHarness] doorlook: command error, the client is not in the world");
                return;
            }

            StarshipDoor target = SdPick(LiveArgString(n, 0, "nearest"), sdLastDoors);
            if (!target)
            {
                Log.Warn("[SyncHarness] doorlook: command error, no door found for that index / id (run doorprobe first, or use nearest)");
                return;
            }

            StartCoroutine(SdLook("doorlook", target, Mathf.Clamp(LiveArgFloat(n, 1, 3f), 0.5f, 60f)));
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] doorlook: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
