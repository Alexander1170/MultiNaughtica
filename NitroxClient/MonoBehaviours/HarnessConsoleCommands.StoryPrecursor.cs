#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check "story_precursor" (a harness script that is not part of this repository, script story_precursor.ps1): Precursor tablets and doors, ion cubes and teleporters, the gun terminal and the aquarium triggers with
// two players. Debug builds only. The commands are registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's
// "/console <player> <command> [arg] [arg]" (at most two arguments, all lower case; coordinates travel as ONE token x,y,z) and log one "[SyncHarness] <name>: key=value ..." line per fact
// (no spaces inside a value, floats with two decimals and the invariant culture). Nothing here throws out of a Unity callback. Members are prefixed Sp and do not depend on the other live files.
//
//   spstate [seconds]            one line: player, position, cinematic, world settled, inventory counts of the tablets and the ion cube, active teleporter pairs, gunDisabled, remote players; with seconds it repeats every 2 s
//   spnear <kind> [radius]       a block "begin / row ... / end" with the nearest objects of a kind (key door doorkey tele teleterm telefull gun cube tablet aq pillar all), nearest first, 12 rows at most:
//                                id= (the NitroxEntity on the component's own object, noid when none) ancestor= (the NitroxEntity of the nearest parent) ownId= pos= dist= path= and the state of the object
//   spnearloop <kind> <seconds>  the same block every 3 s (radius 200), so that a script waits for streaming with ONE chat command
//   spgive <tech> [n]            CraftData.AddToInventory (the real pickup path, Inventory.Pickup), then waits up to 15 s for the count to rise and logs the new ids
//   spitems [techs]              census of every Pickupable of the tablet / ion cube techs (or the given comma list), where it is (inventory, ghost:<player>, world, socket, slot, other:<name>, detached:<name>)
//   spkey <nearest|idprefix>     PrecursorKeyTerminal.OnHandClick (what the hand click does), logs before / clicked / done
//   spcube take [maxm]           Inventory.Pickup of the nearest loose ion cube (the pedestal cube)
//   spcube use [nearest|idprefix] PrecursorTeleporterActivationTerminal.OnProxyHandClick (what the proxy's hand click does), logs before / clicked / done
//   sptele state|trigger|call|watch [x]  the teleporters; trigger puts the player into the portal's trigger collider (the real PrecursorTeleporterCollider.OnTriggerEnter), call sends BeginTeleportPlayer,
//                                watch logs the active teleporter and the movement of this player every second for x seconds
//   spgun state|enter|leave|use|watch [x] the gun terminal: its area, the local and the remote bodies inside, the hand click, the changes of opened / usingPlayer / gunDisabled / playerCured
//   spaq state|watch [x]         the aquarium teleporter trigger (the Sea Emperor's animator booleans and the nearest player distance)
//   spremote [seconds]           the positions of the remote players seen from this client, every second for that long
//   spwatch <seconds>            a compact change log of the nearest doors, key terminals, portals, activation terminals and gun terminal
//   spgoto x,y,z                 Player.SetPosition for a short move in the same region (long jumps use /teleport and /warp)
//   spreset                      last resort when a cinematic hangs: Player.cinematicModeActive false, input enabled
//   sptelediag [radius]          why is no portal near the player: every PrecursorTeleporter of the loaded scenes with activeSelf / activeInHierarchy (inactive ones too), the registered objects called Teleporter, the number of registered objects within 100 / 200 m, terrainLoaded
//   spplaces [words]             the named places of the game's own teleport table (the "goto" command) whose name contains one of the words (separated by |, default the Precursor list, all = every place)
public partial class HarnessConsoleCommands
{
    private const int SP_MAX_ROWS = 12;
    private const string SP_GHOST_PREFIX = "NitroxInventoryStorage_";
    private static readonly TechType[] spDefaultTechs =
    {
        TechType.PrecursorKey_Purple, TechType.PrecursorKey_Blue, TechType.PrecursorKey_Orange, TechType.PrecursorKey_Red, TechType.PrecursorKey_White, TechType.PrecursorIonCrystal
    };

    private static int spNearSeq;
    private static bool spGunReturnSet;
    private static Vector3 spGunReturn;
    private Coroutine spStateRoutine;
    private Coroutine spNearRoutine;
    private Coroutine spRemoteRoutine;
    private Coroutine spWatchRoutine;
    private Coroutine spTeleRoutine;
    private Coroutine spTeleWatchRoutine;
    private Coroutine spGunWatchRoutine;
    private Coroutine spAqWatchRoutine;

    private sealed class SpRow
    {
        public Component Item;
        public float Dist;

        public SpRow(Component item, float dist)
        {
            Item = item;
            Dist = dist;
        }
    }

    // ---- small helpers

    private static string SpF(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string SpVec(Vector3 value)
    {
        return $"{SpF(value.x)},{SpF(value.y)},{SpF(value.z)}";
    }

    private static bool SpTryVec(string text, out Vector3 value)
    {
        value = Vector3.zero;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        float x;
        float y;
        float z;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    private static string SpTok(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "none";
        }

        return text.Replace(' ', '_').Replace('|', '_').Replace('=', '_').Replace('/', '>');
    }

    private static string SpT()
    {
        return Time.realtimeSinceStartup.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static void SpWarn(string command, Exception exception)
    {
        Log.Warn($"[SyncHarness] {command}: error {exception.GetType().Name}: {exception.Message}");
    }

    private static string SpMe()
    {
        try
        {
            string name = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.LocalPlayer>().PlayerName;
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    private static string SpPath(Transform transform)
    {
        if (!transform)
        {
            return "none";
        }

        StringBuilder path = new StringBuilder();
        int guard = 0;
        for (Transform current = transform; current && guard < 12; current = current.parent)
        {
            if (path.Length > 0)
            {
                path.Insert(0, '>');
            }

            path.Insert(0, SpTok(current.name));
            guard++;
        }

        return path.Length > 180 ? path.ToString(path.Length - 180, 180) : path.ToString();
    }

    // the NitroxEntity on the component's own object: its id, or "noid"
    private static string SpOwnId(Component component)
    {
        NitroxId id;
        return component && component.TryGetNitroxId(out id) ? id.ToString() : "noid";
    }

    // the NitroxEntity of the nearest parent (the object itself included): its id, or "none"
    private static string SpAncestorId(Component component)
    {
        NitroxEntity entity;
        if (component && component.TryGetComponentInParent(out entity, true) && entity.Id != null)
        {
            return entity.Id.ToString();
        }

        return "none";
    }

    // own id, else the id of the parent object, else noid
    private static string SpEffectiveId(Component component)
    {
        string own = SpOwnId(component);
        if (own != "noid")
        {
            return own;
        }

        string ancestor = SpAncestorId(component);
        return ancestor == "none" ? "noid" : ancestor;
    }

    private static string SpCommon(Component component, float dist)
    {
        string own = SpOwnId(component);
        return $"id={own} ancestor={SpAncestorId(component)} ownId={own != "noid"} pos={SpVec(component.transform.position)} dist={SpF(dist)}";
    }

    private static List<SpRow> SpFind<T>(Vector3 origin, float radius) where T : Component
    {
        List<SpRow> rows = new List<SpRow>();
        foreach (T item in FindObjectsOfType<T>())
        {
            if (!item)
            {
                continue;
            }

            float dist = Vector3.Distance(origin, item.transform.position);
            if (dist <= radius)
            {
                rows.Add(new SpRow(item, dist));
            }
        }

        rows.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        return rows;
    }

    private static bool SpTeleActive(string identifier)
    {
        try
        {
            return TeleporterManager.main && TeleporterManager.GetTeleporterActive(identifier);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string SpActiveList()
    {
        if (!TeleporterManager.main || TeleporterManager.main.activeTeleporters == null)
        {
            return "";
        }

        List<string> active = new List<string>(TeleporterManager.main.activeTeleporters);
        active.Sort(StringComparer.Ordinal);
        return string.Join(",", active.ToArray());
    }

    private static int SpCount(TechType tech)
    {
        if (!Inventory.main || Inventory.main.container == null)
        {
            return -1;
        }

        return Inventory.main.container.GetCount(tech);
    }

    private static bool SpInside(Collider collider, Vector3 point)
    {
        if (!collider)
        {
            return false;
        }

        Vector3 closest = collider.ClosestPoint(point);
        return (closest - point).sqrMagnitude < 0.0004f;
    }

    // the player and the bodies of the remote players: name, position
    private static List<KeyValuePair<string, Vector3>> SpBodies(bool includeLocal)
    {
        List<KeyValuePair<string, Vector3>> bodies = new List<KeyValuePair<string, Vector3>>();
        if (includeLocal && Player.main)
        {
            bodies.Add(new KeyValuePair<string, Vector3>(SpMe(), Player.main.transform.position));
        }

        try
        {
            foreach (NitroxClient.GameLogic.RemotePlayer remote in Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>().GetAll())
            {
                if (remote.Body)
                {
                    bodies.Add(new KeyValuePair<string, Vector3>(remote.PlayerName, remote.Body.transform.position));
                }
            }
        }
        catch (Exception)
        {
            // no remote players known yet
        }

        return bodies;
    }

    // Where a Pickupable is: socket (the tablet or cube that a click holds), slot, inventory, ghost:<player> (the copy in a remote player's container), other:<container>, world, detached:<parent>
    private static string SpWhere(Pickupable pickupable)
    {
        if (Inventory.main && Inventory.main.toolSocket && pickupable.transform.parent == Inventory.main.toolSocket)
        {
            return "socket";
        }

        InventoryItem held = pickupable.inventoryItem;
        IItemsContainer container = held != null ? held.container : null;
        if (container is Equipment)
        {
            return "slot";
        }

        if (container != null)
        {
            if (Inventory.main && object.ReferenceEquals(container, Inventory.main.container))
            {
                return "inventory";
            }

            string label = container.label ?? "";
            if (label.StartsWith(SP_GHOST_PREFIX, StringComparison.Ordinal))
            {
                return $"ghost:{SpTok(label.Substring(SP_GHOST_PREFIX.Length))}";
            }

            return $"other:{SpTok(label)}";
        }

        if (pickupable.gameObject.activeInHierarchy)
        {
            return "world";
        }

        Transform parent = pickupable.transform.parent;
        return $"detached:{SpTok(parent ? parent.name : "noparent")}";
    }

    private static string SpDoorsText(Transform root)
    {
        if (!root)
        {
            return "n/a";
        }

        PrecursorDoorway[] doors = root.GetComponentsInChildren<PrecursorDoorway>(true);
        int open = 0;
        foreach (PrecursorDoorway door in doors)
        {
            if (door && door.isOpen)
            {
                open++;
            }
        }

        return $"{open}/{doors.Length}";
    }

    private static string SpActive(GameObject target)
    {
        return target ? target.activeSelf.ToString() : "n/a";
    }

    private static Collider SpPortalCollider(PrecursorTeleporter portal)
    {
        PrecursorTeleporterCollider marker = portal.GetComponentInChildren<PrecursorTeleporterCollider>(true);
        if (!marker)
        {
            return null;
        }

        Collider own = marker.GetComponent<Collider>();
        return own ? own : marker.GetComponentInChildren<Collider>(true);
    }

    private static string SpPortalColliderText(PrecursorTeleporter portal)
    {
        Collider collider = SpPortalCollider(portal);
        return collider ? SpVec(collider.bounds.center) : "none";
    }

    private static PrecursorTeleporter SpTeleporterOf(PrecursorTeleporterActivationTerminal terminal)
    {
        Transform scope = terminal.root ? terminal.root.transform : terminal.transform.parent;
        PrecursorTeleporter found = scope ? scope.GetComponentInChildren<PrecursorTeleporter>(true) : null;
        if (found)
        {
            return found;
        }

        List<SpRow> near = SpFind<PrecursorTeleporter>(terminal.transform.position, 15f);
        return near.Count > 0 ? (PrecursorTeleporter)near[0].Item : null;
    }

    private static PrecursorDisableGunTerminal SpGunTerminal(Vector3 origin)
    {
        List<SpRow> rows = SpFind<PrecursorDisableGunTerminal>(origin, 100000f);
        return rows.Count > 0 ? (PrecursorDisableGunTerminal)rows[0].Item : null;
    }

    private static PrecursorDisableGunTerminalArea SpGunArea(PrecursorDisableGunTerminal terminal)
    {
        PrecursorDisableGunTerminalArea best = null;
        foreach (PrecursorDisableGunTerminalArea area in FindObjectsOfType<PrecursorDisableGunTerminalArea>())
        {
            if (area && area.terminal == terminal)
            {
                best = area;
                break;
            }
        }

        return best;
    }

    private static Collider SpGunAreaCollider(PrecursorDisableGunTerminal terminal)
    {
        PrecursorDisableGunTerminalArea area = SpGunArea(terminal);
        if (!area)
        {
            return null;
        }

        Collider own = area.GetComponent<Collider>();
        return own ? own : area.GetComponentInChildren<Collider>(true);
    }

    private static string SpGunDisabledText()
    {
        return StoryGoalCustomEventHandler.main ? StoryGoalCustomEventHandler.main.gunDisabled.ToString() : "none";
    }

    private static string SpGoalKey(Story.StoryGoal goal)
    {
        return goal != null && !string.IsNullOrEmpty(goal.key) ? goal.key : "none";
    }

    private static bool SpHasParameter(Animator animator, string name)
    {
        if (!animator || string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static string SpBool(Animator animator, string name)
    {
        return SpHasParameter(animator, name) ? animator.GetBool(name).ToString() : "n/a";
    }

    // ---- spstate

    private void LiveCommand_spstate(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 0f);
            SpLogState();
            if (seconds > 0f)
            {
                if (spStateRoutine != null)
                {
                    StopCoroutine(spStateRoutine);
                }

                spStateRoutine = StartCoroutine(SpStateLoop(seconds));
            }
        }
        catch (Exception exception)
        {
            SpWarn("spstate", exception);
        }
    }

    private IEnumerator SpStateLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(2f);
            SpLogState();
        }
    }

    private static void SpLogState()
    {
        try
        {
            Player player = Player.main;
            string position = player ? SpVec(player.transform.position) : "none";
            bool cinematic = player && player.cinematicModeActive;
            bool settled = LargeWorldStreamer.main && LargeWorldStreamer.main.IsWorldSettled();
            List<string> remotes = new List<string>();
            foreach (KeyValuePair<string, Vector3> body in SpBodies(false))
            {
                remotes.Add($"{SpTok(body.Key)}@{SpVec(body.Value)}");
            }

            string playerId = player ? SpOwnId(player) : "none";
            Log.Info($"[SyncHarness] spstate: player={SpMe()} pos={position} cinematic={cinematic} settled={settled} purple={SpCount(TechType.PrecursorKey_Purple)} blue={SpCount(TechType.PrecursorKey_Blue)} orange={SpCount(TechType.PrecursorKey_Orange)} red={SpCount(TechType.PrecursorKey_Red)} white={SpCount(TechType.PrecursorKey_White)} cubes={SpCount(TechType.PrecursorIonCrystal)} active=[{SpActiveList()}] gunDisabled={SpGunDisabledText()} remote={(remotes.Count > 0 ? string.Join(";", remotes.ToArray()) : "none")} playerId={playerId}");
        }
        catch (Exception exception)
        {
            SpWarn("spstate", exception);
        }
    }

    // ---- spnear / spnearloop

    private void LiveCommand_spnear(NotificationCenter.Notification n)
    {
        try
        {
            SpNearBlock(LiveArgString(n, 0, "all"), LiveArgFloat(n, 1, 200f));
        }
        catch (Exception exception)
        {
            SpWarn("spnear", exception);
        }
    }

    private void LiveCommand_spnearloop(NotificationCenter.Notification n)
    {
        try
        {
            string kind = LiveArgString(n, 0, "all");
            float seconds = LiveArgFloat(n, 1, 30f);
            if (spNearRoutine != null)
            {
                StopCoroutine(spNearRoutine);
            }

            spNearRoutine = StartCoroutine(SpNearLoop(kind, seconds));
        }
        catch (Exception exception)
        {
            SpWarn("spnearloop", exception);
        }
    }

    private IEnumerator SpNearLoop(string kind, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        SpSafeNear(kind, 200f);
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(3f);
            SpSafeNear(kind, 200f);
        }
    }

    private static void SpSafeNear(string kind, float radius)
    {
        try
        {
            SpNearBlock(kind, radius);
        }
        catch (Exception exception)
        {
            SpWarn("spnear", exception);
        }
    }

    private static void SpNearBlock(string kind, float radius)
    {
        Player player = Player.main;
        int seq = ++spNearSeq;
        if (!player)
        {
            Log.Warn($"[SyncHarness] spnear: failed reason=no-player kind={kind} seq={seq}");
            return;
        }

        Vector3 origin = player.transform.position;
        Log.Info($"[SyncHarness] spnear: begin kind={kind} radius={SpF(radius)} player={SpVec(origin)} seq={seq}");
        List<string> rows = new List<string>();
        switch (kind)
        {
            case "key":
                SpRowsKey(rows, origin, radius);
                break;
            case "door":
                SpRowsDoor(rows, origin, radius);
                break;
            case "doorkey":
                SpRowsDoor(rows, origin, radius);
                SpRowsKey(rows, origin, radius);
                break;
            case "tele":
                SpRowsTele(rows, origin, radius);
                break;
            case "teleterm":
                SpRowsTeleterm(rows, origin, radius);
                break;
            case "telefull":
                SpRowsTele(rows, origin, radius);
                SpRowsTeleterm(rows, origin, radius);
                break;
            case "gun":
                SpRowsGun(rows, origin, radius);
                break;
            case "cube":
                SpRowsPickups(rows, origin, radius, "cube", new[] { TechType.PrecursorIonCrystal });
                break;
            case "tablet":
                SpRowsPickups(rows, origin, radius, "tablet", new[] { TechType.PrecursorKey_Purple, TechType.PrecursorKey_Blue, TechType.PrecursorKey_Orange, TechType.PrecursorKey_Red, TechType.PrecursorKey_White });
                break;
            case "aq":
                SpRowsAq(rows, origin, radius);
                break;
            case "pillar":
                SpRowsPillar(rows, origin, radius);
                break;
            default:
                SpRowsDoor(rows, origin, radius);
                SpRowsKey(rows, origin, radius);
                SpRowsTele(rows, origin, radius);
                SpRowsTeleterm(rows, origin, radius);
                SpRowsGun(rows, origin, radius);
                SpRowsPickups(rows, origin, radius, "cube", new[] { TechType.PrecursorIonCrystal });
                SpRowsPickups(rows, origin, radius, "tablet", new[] { TechType.PrecursorKey_Purple, TechType.PrecursorKey_Blue, TechType.PrecursorKey_Orange, TechType.PrecursorKey_Red, TechType.PrecursorKey_White });
                SpRowsAq(rows, origin, radius);
                SpRowsPillar(rows, origin, radius);
                break;
        }

        foreach (string row in rows)
        {
            Log.Info($"[SyncHarness] spnear: row seq={seq} {row}");
        }

        Log.Info($"[SyncHarness] spnear: end kind={kind} count={rows.Count} seq={seq}");
    }

    private static void SpRowsKey(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorKeyTerminal>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorKeyTerminal terminal = (PrecursorKeyTerminal)row.Item;
            rows.Add($"kind=key {SpCommon(terminal, row.Dist)} accept={terminal.acceptKeyType} slotted={terminal.slotted} doors={SpDoorsText(terminal.transform.parent)} path={SpPath(terminal.transform)}");
        }
    }

    private static void SpRowsDoor(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorDoorway>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorDoorway door = (PrecursorDoorway)row.Item;
            rows.Add($"kind=door {SpCommon(door, row.Dist)} isOpen={door.isOpen} collider={SpActive(door.colliderAndSound)} field={SpActive(door.forceField)} path={SpPath(door.transform)}");
        }
    }

    private static void SpRowsTele(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorTeleporter>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorTeleporter portal = (PrecursorTeleporter)row.Item;
            string identifier = SpTok(portal.teleporterIdentifier);
            rows.Add($"kind=tele {SpCommon(portal, row.Dist)} ident={identifier} isOpen={portal.isOpen} alwaysOn={portal.alwaysOn} active={SpTeleActive(portal.teleporterIdentifier)} hasFirstUse={portal.hasFirstUse} warpTo={SpVec(portal.warpToPos)} angle={SpF(portal.warpToAngle)} collider={SpPortalColliderText(portal)} path={SpPath(portal.transform)}");
        }
    }

    private static void SpRowsTeleterm(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorTeleporterActivationTerminal>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorTeleporterActivationTerminal terminal = (PrecursorTeleporterActivationTerminal)row.Item;
            PrecursorTeleporter portal = SpTeleporterOf(terminal);
            string identifier = portal ? SpTok(portal.teleporterIdentifier) : "n/a";
            bool portalOpen = portal && portal.isOpen;
            rows.Add($"kind=teleterm {SpCommon(terminal, row.Dist)} unlocked={terminal.unlocked} ident={identifier} portalOpen={portalOpen} crystal={SpActive(terminal.crystalObject)} path={SpPath(terminal.transform)}");
        }
    }

    private static void SpRowsGun(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorDisableGunTerminal>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorDisableGunTerminal terminal = (PrecursorDisableGunTerminal)row.Item;
            Collider area = SpGunAreaCollider(terminal);
            string areaCenter = area ? SpVec(area.bounds.center) : "none";
            string areaSize = area ? SpVec(area.bounds.size) : "none";
            string usingName = terminal.usingPlayer ? SpTok(terminal.usingPlayer.name) : "none";
            rows.Add($"kind=gun {SpCommon(terminal, row.Dist)} opened={terminal.opened} playerCured={terminal.playerCured} firstUse={terminal.firstUse} ignore={terminal.ignorePlayer} using={usingName} area={areaCenter} areaSize={areaSize} curedKey={SpGoalKey(terminal.onPlayerCuredGoal)} disableKey={SpGoalKey(terminal.gunDeactivate)} gunDisabled={SpGunDisabledText()} path={SpPath(terminal.transform)}");
        }
    }

    private static void SpRowsPickups(List<string> rows, Vector3 origin, float radius, string kind, TechType[] techs)
    {
        List<SpRow> found = new List<SpRow>();
        foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
        {
            if (!pickupable || Array.IndexOf(techs, pickupable.GetTechType()) < 0)
            {
                continue;
            }

            float dist = Vector3.Distance(origin, pickupable.transform.position);
            if (dist <= radius)
            {
                found.Add(new SpRow(pickupable, dist));
            }
        }

        found.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        int taken = 0;
        foreach (SpRow row in found)
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            Pickupable pickupable = (Pickupable)row.Item;
            rows.Add($"kind={kind} {SpCommon(pickupable, row.Dist)} tech={pickupable.GetTechType()} where={SpWhere(pickupable)} path={SpPath(pickupable.transform)}");
        }
    }

    private static float SpNearestPlayerDistance(Vector3 point, out float localDistance)
    {
        localDistance = Player.main ? Vector3.Distance(Player.main.transform.position, point) : -1f;
        float nearest = localDistance >= 0f ? localDistance : float.MaxValue;
        foreach (KeyValuePair<string, Vector3> body in SpBodies(false))
        {
            nearest = Mathf.Min(nearest, Vector3.Distance(body.Value, point));
        }

        return nearest == float.MaxValue ? -1f : nearest;
    }

    private static void SpRowsAq(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorAquariumTeleporterTrigger>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorAquariumTeleporterTrigger trigger = (PrecursorAquariumTeleporterTrigger)row.Item;
            float localDistance;
            float nearest = SpNearestPlayerDistance(trigger.transform.position, out localDistance);
            string animatorName = trigger.targetAnimator ? SpTok(trigger.targetAnimator.name) : "none";
            string triggerEnabled = trigger.trigger ? trigger.trigger.enabled.ToString() : "n/a";
            rows.Add($"kind=aq {SpCommon(trigger, row.Dist)} swimRange={SpF(trigger.swimRange)} minRange={SpF(trigger.minRange)} maxRange={SpF(trigger.maxRange)} incubatorKey={SpTok(trigger.listenForIncubatorActiveGoal)} uncoverKey={SpTok(trigger.listenForTeleporterUncoverGoal)} activeKey={SpTok(trigger.listenForTeleporterActiveGoal)} animator={animatorName} atParam={SpTok(trigger.atTeleporterParameterName)} blowParam={SpTok(trigger.blowTeleporterParameterName)} at={SpBool(trigger.targetAnimator, trigger.atTeleporterParameterName)} blow={SpBool(trigger.targetAnimator, trigger.blowTeleporterParameterName)} triggerEnabled={triggerEnabled} nearest={SpF(nearest)} local={SpF(localDistance)} path={SpPath(trigger.transform)}");
        }
    }

    private static void SpRowsPillar(List<string> rows, Vector3 origin, float radius)
    {
        int taken = 0;
        foreach (SpRow row in SpFind<PrecursorActivatedPillar>(origin, radius))
        {
            if (taken++ >= SP_MAX_ROWS)
            {
                break;
            }

            PrecursorActivatedPillar pillar = (PrecursorActivatedPillar)row.Item;
            rows.Add($"kind=pillar {SpCommon(pillar, row.Dist)} active={pillar.active} extended={pillar.extended} path={SpPath(pillar.transform)}");
        }
    }

    // ---- spgive / spitems

    private static HashSet<string> SpItemIds(TechType tech)
    {
        HashSet<string> ids = new HashSet<string>();
        if (Inventory.main && Inventory.main.container != null)
        {
            IList<InventoryItem> items = Inventory.main.container.GetItems(tech);
            if (items != null)
            {
                foreach (InventoryItem item in items)
                {
                    if (item != null && item.item)
                    {
                        ids.Add(SpOwnId(item.item));
                    }
                }
            }
        }

        return ids;
    }

    private void LiveCommand_spgive(NotificationCenter.Notification n)
    {
        try
        {
            string name = LiveArgString(n, 0, "");
            int count = Mathf.Clamp((int)LiveArgFloat(n, 1, 1f), 1, 5);
            TechType tech;
            if (!Enum.TryParse(name, true, out tech))
            {
                Log.Warn($"[SyncHarness] spgive: failed reason=unknown-tech name={name}");
                return;
            }

            if (!Player.main || !Inventory.main || Inventory.main.container == null)
            {
                Log.Warn("[SyncHarness] spgive: failed reason=no-player");
                return;
            }

            int before = SpCount(tech);
            HashSet<string> known = SpItemIds(tech);
            float started = Time.realtimeSinceStartup;
            CraftData.AddToInventory(tech, count, true, false);
            StartCoroutine(SpGiveWait(tech, count, before, known, started));
        }
        catch (Exception exception)
        {
            SpWarn("spgive", exception);
        }
    }

    private IEnumerator SpGiveWait(TechType tech, int count, int before, HashSet<string> known, float started)
    {
        float limit = started + 15f;
        while (Time.realtimeSinceStartup < limit && SpCount(tech) < before + count)
        {
            yield return new WaitForSecondsRealtime(0.25f);
        }

        SpLogGive(tech, count, before, known, started);
    }

    private static void SpLogGive(TechType tech, int count, int before, HashSet<string> known, float started)
    {
        try
        {
            int after = SpCount(tech);
            List<string> fresh = new List<string>();
            foreach (string id in SpItemIds(tech))
            {
                if (!known.Contains(id))
                {
                    fresh.Add(id);
                }
            }

            Log.Info($"[SyncHarness] spgive: tech={tech} wanted={count} before={before} after={after} ids={(fresh.Count > 0 ? string.Join(";", fresh.ToArray()) : "none")} result={after >= before + count} t={SpF(Time.realtimeSinceStartup - started)}");
        }
        catch (Exception exception)
        {
            SpWarn("spgive", exception);
        }
    }

    private void LiveCommand_spitems(NotificationCenter.Notification n)
    {
        try
        {
            List<TechType> techs = new List<TechType>();
            string list = LiveArgString(n, 0, "");
            if (list.Length == 0)
            {
                techs.AddRange(spDefaultTechs);
            }
            else
            {
                foreach (string part in list.Split(','))
                {
                    TechType parsed;
                    if (Enum.TryParse(part, true, out parsed))
                    {
                        techs.Add(parsed);
                    }
                }
            }

            Dictionary<TechType, Dictionary<string, int>> counts = new Dictionary<TechType, Dictionary<string, int>>();
            foreach (TechType tech in techs)
            {
                counts[tech] = new Dictionary<string, int>();
            }

            int rows = 0;
            foreach (Pickupable pickupable in Resources.FindObjectsOfTypeAll<Pickupable>())
            {
                if (!pickupable || !pickupable.gameObject.scene.IsValid())
                {
                    continue;
                }

                TechType tech = pickupable.GetTechType();
                if (!counts.ContainsKey(tech))
                {
                    continue;
                }

                string place = SpWhere(pickupable);
                string bucket = place.IndexOf(':') > 0 ? place.Substring(0, place.IndexOf(':')) : place;
                Dictionary<string, int> perPlace = counts[tech];
                perPlace[bucket] = (perPlace.ContainsKey(bucket) ? perPlace[bucket] : 0) + 1;
                rows++;
                Log.Info($"[SyncHarness] spitems: item id={SpOwnId(pickupable)} tech={tech} where={place} pos={SpVec(pickupable.transform.position)} active={pickupable.gameObject.activeInHierarchy}");
            }

            foreach (TechType listed in techs)
            {
                Dictionary<string, int> perPlace = counts[listed];
                Log.Info($"[SyncHarness] spitems: counts tech={listed} inventory={SpGet(perPlace, "inventory")} ghost={SpGet(perPlace, "ghost")} world={SpGet(perPlace, "world")} socket={SpGet(perPlace, "socket")} slot={SpGet(perPlace, "slot")} other={SpGet(perPlace, "other")} detached={SpGet(perPlace, "detached")}");
            }

            Log.Info($"[SyncHarness] spitems: end rows={rows}");
        }
        catch (Exception exception)
        {
            SpWarn("spitems", exception);
        }
    }

    private static int SpGet(Dictionary<string, int> map, string key)
    {
        int value;
        return map.TryGetValue(key, out value) ? value : 0;
    }

    // ---- spkey

    private static PrecursorKeyTerminal SpPickKey(string selector, Vector3 origin, out float distance)
    {
        distance = -1f;
        PrecursorKeyTerminal best = null;
        float bestDist = float.MaxValue;
        bool bestFree = false;
        bool nearest = selector.Length == 0 || selector == "nearest";
        foreach (PrecursorKeyTerminal terminal in FindObjectsOfType<PrecursorKeyTerminal>())
        {
            if (!terminal)
            {
                continue;
            }

            float dist = Vector3.Distance(origin, terminal.transform.position);
            bool free = !terminal.slotted;
            if (nearest)
            {
                if (dist > 40f)
                {
                    continue;
                }
            }
            else if (!SpEffectiveId(terminal).StartsWith(selector, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool better = !best || (nearest && free && !bestFree) || ((!nearest || free == bestFree) && dist < bestDist);
            if (better)
            {
                best = terminal;
                bestDist = dist;
                bestFree = free;
                distance = dist;
            }
        }

        return best;
    }

    private static TechType SpKeyTech(PrecursorKeyTerminal terminal)
    {
        TechType tech;
        return Enum.TryParse(terminal.acceptKeyType.ToString(), true, out tech) ? tech : TechType.None;
    }

    private void LiveCommand_spkey(NotificationCenter.Notification n)
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] spkey: failed reason=no-player");
                return;
            }

            float distance;
            PrecursorKeyTerminal terminal = SpPickKey(LiveArgString(n, 0, "nearest"), player.transform.position, out distance);
            if (!terminal)
            {
                Log.Warn("[SyncHarness] spkey: refused reason=none");
                return;
            }

            string id = SpEffectiveId(terminal);
            TechType tech = SpKeyTech(terminal);
            int have = SpCount(tech);
            Log.Info($"[SyncHarness] spkey: before id={id} accept={terminal.acceptKeyType} slotted={terminal.slotted} have={have} dist={SpF(distance)}");
            if (terminal.slotted)
            {
                Log.Info($"[SyncHarness] spkey: refused id={id} reason=slotted have={have}");
                return;
            }

            if (have < 1)
            {
                Log.Info($"[SyncHarness] spkey: refused id={id} reason=no-tablet have={have}");
                return;
            }

            float started = Time.realtimeSinceStartup;
            terminal.OnHandClick(player.guiHand);
            Log.Info($"[SyncHarness] spkey: clicked id={id} slotted={terminal.slotted} have={SpCount(tech)} cinematic={player.cinematicModeActive} t={SpT()}");
            StartCoroutine(SpKeyWait(terminal, id, tech, started));
        }
        catch (Exception exception)
        {
            SpWarn("spkey", exception);
        }
    }

    private IEnumerator SpKeyWait(PrecursorKeyTerminal terminal, string id, TechType tech, float started)
    {
        float limit = started + 60f;
        while (Time.realtimeSinceStartup < limit)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (!terminal)
            {
                break;
            }

            bool cinematic = Player.main && Player.main.cinematicModeActive;
            // the key object is destroyed 0.45 s after the cinematic ended (DestroyKey)
            if (!cinematic && !terminal.keyObject && Time.realtimeSinceStartup - started > 1f)
            {
                SpLogKeyEnd("done", terminal, id, tech, started);
                yield break;
            }
        }

        SpLogKeyEnd("timeout", terminal, id, tech, started);
    }

    private static void SpLogKeyEnd(string word, PrecursorKeyTerminal terminal, string id, TechType tech, float started)
    {
        try
        {
            bool cinematic = Player.main && Player.main.cinematicModeActive;
            string slotted = terminal ? terminal.slotted.ToString() : "gone";
            string doors = terminal ? SpDoorsText(terminal.transform.parent) : "n/a";
            Log.Info($"[SyncHarness] spkey: {word} id={id} seconds={SpF(Time.realtimeSinceStartup - started)} slotted={slotted} have={SpCount(tech)} doors={doors} cinematic={cinematic}");
        }
        catch (Exception exception)
        {
            SpWarn("spkey", exception);
        }
    }

    // ---- spcube

    private void LiveCommand_spcube(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, "take");
            if (mode == "take")
            {
                SpCubeTake(LiveArgFloat(n, 1, 60f));
            }
            else if (mode == "use")
            {
                SpCubeUse(LiveArgString(n, 1, "nearest"));
            }
            else
            {
                Log.Warn($"[SyncHarness] spcube: failed reason=unknown-mode mode={mode}");
            }
        }
        catch (Exception exception)
        {
            SpWarn("spcube", exception);
        }
    }

    private static void SpCubeTake(float maxMeters)
    {
        Player player = Player.main;
        if (!player || !Inventory.main)
        {
            Log.Warn("[SyncHarness] spcube: take failed reason=no-player");
            return;
        }

        Pickupable best = null;
        float bestDist = float.MaxValue;
        foreach (Pickupable pickupable in FindObjectsOfType<Pickupable>())
        {
            if (!pickupable || pickupable.GetTechType() != TechType.PrecursorIonCrystal || SpWhere(pickupable) != "world")
            {
                continue;
            }

            float dist = Vector3.Distance(player.transform.position, pickupable.transform.position);
            if (dist <= maxMeters && dist < bestDist)
            {
                best = pickupable;
                bestDist = dist;
            }
        }

        if (!best)
        {
            Log.Info($"[SyncHarness] spcube: take refused reason=none max={SpF(maxMeters)} have={SpCount(TechType.PrecursorIonCrystal)}");
            return;
        }

        string id = SpOwnId(best);
        string position = SpVec(best.transform.position);
        bool result = Inventory.main.Pickup(best, false);
        Log.Info($"[SyncHarness] spcube: take id={id} result={result} have={SpCount(TechType.PrecursorIonCrystal)} pos={position} dist={SpF(bestDist)} t={SpT()}");
    }

    private void SpCubeUse(string selector)
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Warn("[SyncHarness] spcube: use failed reason=no-player");
            return;
        }

        PrecursorTeleporterActivationTerminal terminal = null;
        float bestDist = float.MaxValue;
        bool nearest = selector.Length == 0 || selector == "nearest";
        foreach (SpRow row in SpFind<PrecursorTeleporterActivationTerminal>(player.transform.position, 40f))
        {
            PrecursorTeleporterActivationTerminal candidate = (PrecursorTeleporterActivationTerminal)row.Item;
            if (!nearest && !SpEffectiveId(candidate).StartsWith(selector, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (row.Dist < bestDist)
            {
                terminal = candidate;
                bestDist = row.Dist;
            }
        }

        if (!terminal)
        {
            Log.Info("[SyncHarness] spcube: refused reason=none");
            return;
        }

        string id = SpEffectiveId(terminal);
        PrecursorTeleporter portal = SpTeleporterOf(terminal);
        string identifier = portal ? SpTok(portal.teleporterIdentifier) : "n/a";
        int have = SpCount(TechType.PrecursorIonCrystal);
        Log.Info($"[SyncHarness] spcube: before id={id} unlocked={terminal.unlocked} have={have} ident={identifier} portalOpen={(portal && portal.isOpen)} dist={SpF(bestDist)}");
        if (terminal.unlocked)
        {
            Log.Info($"[SyncHarness] spcube: refused id={id} reason=unlocked");
            return;
        }

        if (have < 1)
        {
            Log.Info($"[SyncHarness] spcube: refused id={id} reason=no-cube");
            return;
        }

        float started = Time.realtimeSinceStartup;
        terminal.OnProxyHandClick(player.guiHand);
        Log.Info($"[SyncHarness] spcube: clicked id={id} unlocked={terminal.unlocked} have={SpCount(TechType.PrecursorIonCrystal)} cinematic={player.cinematicModeActive} t={SpT()}");
        StartCoroutine(SpCubeWait(terminal, id, started));
    }

    private IEnumerator SpCubeWait(PrecursorTeleporterActivationTerminal terminal, string id, float started)
    {
        float limit = started + 60f;
        while (Time.realtimeSinceStartup < limit)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (!terminal)
            {
                break;
            }

            bool cinematic = Player.main && Player.main.cinematicModeActive;
            // the crystal object is destroyed when the cinematic ends (OnPlayerCinematicModeEnd), which also opens the portal of the pair
            if (!cinematic && !terminal.crystalObject && Time.realtimeSinceStartup - started > 1f)
            {
                SpLogCubeEnd("done", terminal, id, started);
                yield break;
            }
        }

        SpLogCubeEnd("timeout", terminal, id, started);
    }

    private static void SpLogCubeEnd(string word, PrecursorTeleporterActivationTerminal terminal, string id, float started)
    {
        try
        {
            PrecursorTeleporter portal = terminal ? SpTeleporterOf(terminal) : null;
            string identifier = portal ? SpTok(portal.teleporterIdentifier) : "n/a";
            string active = portal ? SpTeleActive(portal.teleporterIdentifier).ToString() : "n/a";
            string unlocked = terminal ? terminal.unlocked.ToString() : "gone";
            Log.Info($"[SyncHarness] spcube: {word} id={id} seconds={SpF(Time.realtimeSinceStartup - started)} unlocked={unlocked} have={SpCount(TechType.PrecursorIonCrystal)} ident={identifier} portalOpen={(portal && portal.isOpen)} active={active}");
        }
        catch (Exception exception)
        {
            SpWarn("spcube", exception);
        }
    }

    // ---- sptele

    private void LiveCommand_sptele(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, "state");
            string argument = LiveArgString(n, 1, "");
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] sptele: failed reason=no-player");
                return;
            }

            if (mode == "state")
            {
                SpNearBlock("telefull", 300f);
                Log.Info($"[SyncHarness] sptele: state activeTeleporter={SpActiveTeleporterText()} player={SpVec(player.transform.position)} cinematic={player.cinematicModeActive} active=[{SpActiveList()}]");
            }
            else if (mode == "watch")
            {
                float seconds = LiveArgFloat(n, 1, 30f);
                if (spTeleWatchRoutine != null)
                {
                    StopCoroutine(spTeleWatchRoutine);
                }

                spTeleWatchRoutine = StartCoroutine(SpTeleWatch(seconds));
            }
            else if (mode == "trigger" || mode == "call")
            {
                SpTeleStart(mode, argument);
            }
            else
            {
                Log.Warn($"[SyncHarness] sptele: failed reason=unknown-mode mode={mode}");
            }
        }
        catch (Exception exception)
        {
            SpWarn("sptele", exception);
        }
    }

    private static string SpActiveTeleporterText()
    {
        PrecursorTeleporter active = PrecursorTeleporter.activeTeleporter;
        return active ? SpTok(active.teleporterIdentifier) : "none";
    }

    private IEnumerator SpTeleWatch(float seconds)
    {
        Player player = Player.main;
        if (!player)
        {
            yield break;
        }

        Vector3 start = player.transform.position;
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            SpLogTeleWatch(start);
            yield return new WaitForSecondsRealtime(1f);
        }

        SpLogTeleWatch(start);
    }

    private static void SpLogTeleWatch(Vector3 start)
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                return;
            }

            Vector3 here = player.transform.position;
            Log.Info($"[SyncHarness] sptele: watch t={SpT()} active={SpActiveTeleporterText()} moved={SpF(Vector3.Distance(start, here))} pos={SpVec(here)} cinematic={player.cinematicModeActive}");
        }
        catch (Exception exception)
        {
            SpWarn("sptele", exception);
        }
    }

    private void SpTeleStart(string mode, string selector)
    {
        Player player = Player.main;
        if (PrecursorTeleporter.activeTeleporter)
        {
            Log.Info($"[SyncHarness] sptele: refused reason=busy mode={mode}");
            return;
        }

        PrecursorTeleporter portal = null;
        float bestDist = float.MaxValue;
        bool nearest = selector.Length == 0 || selector == "nearest";
        bool closedSeen = false;
        foreach (SpRow row in SpFind<PrecursorTeleporter>(player.transform.position, 60f))
        {
            PrecursorTeleporter candidate = (PrecursorTeleporter)row.Item;
            if (!nearest && !SpEffectiveId(candidate).StartsWith(selector, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!(candidate.isOpen || candidate.alwaysOn || SpTeleActive(candidate.teleporterIdentifier)))
            {
                closedSeen = true;
                continue;
            }

            if (row.Dist < bestDist)
            {
                portal = candidate;
                bestDist = row.Dist;
            }
        }

        if (!portal)
        {
            Log.Info($"[SyncHarness] sptele: refused reason={(closedSeen ? "closed" : "none")} mode={mode}");
            return;
        }

        string id = SpEffectiveId(portal);
        Vector3 start = player.transform.position;
        Log.Info($"[SyncHarness] sptele: start mode={mode} id={id} ident={SpTok(portal.teleporterIdentifier)} from={SpVec(start)} warpTo={SpVec(portal.warpToPos)} angle={SpF(portal.warpToAngle)} hasFirstUse={portal.hasFirstUse} collider={SpPortalColliderText(portal)} dist={SpF(bestDist)}");
        bool fallback = false;
        if (mode == "trigger")
        {
            Collider collider = SpPortalCollider(portal);
            player.OnPlayerPositionCheat();
            player.SetPosition(collider ? collider.bounds.center : portal.transform.position);
        }
        else
        {
            portal.SendMessage("BeginTeleportPlayer", player.gameObject, SendMessageOptions.DontRequireReceiver);
        }

        if (spTeleRoutine != null)
        {
            StopCoroutine(spTeleRoutine);
        }

        spTeleRoutine = StartCoroutine(SpTeleFollow(portal, mode == "trigger", start, fallback));
    }

    private IEnumerator SpTeleFollow(PrecursorTeleporter portal, bool mayFallBack, Vector3 start, bool fallbackUsed)
    {
        float started = Time.realtimeSinceStartup;
        bool began = false;
        bool moved = false;
        bool fallback = fallbackUsed;
        while (Time.realtimeSinceStartup - started < 150f)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            Player player = Player.main;
            if (!player)
            {
                continue;
            }

            if (!began && PrecursorTeleporter.activeTeleporter)
            {
                began = true;
                Log.Info($"[SyncHarness] sptele: began t={SpT()} ident={SpActiveTeleporterText()} fallback={fallback}");
            }

            if (!began && mayFallBack && !fallback && Time.realtimeSinceStartup - started > 6f && portal)
            {
                // the trigger did not fire (no physics event for a teleported player): the call that PrecursorTeleporterCollider.OnTriggerEnter makes
                fallback = true;
                portal.SendMessage("BeginTeleportPlayer", player.gameObject, SendMessageOptions.DontRequireReceiver);
                Log.Info($"[SyncHarness] sptele: fallback call t={SpT()}");
            }

            float jump = Vector3.Distance(start, player.transform.position);
            if (!moved && jump > 50f)
            {
                moved = true;
                Log.Info($"[SyncHarness] sptele: moved t={SpT()} pos={SpVec(player.transform.position)} jump={SpF(jump)}");
            }

            if (moved && !PrecursorTeleporter.activeTeleporter && !player.cinematicModeActive && !player.isWaitingForTeleportation)
            {
                float toWarp = portal ? Vector3.Distance(player.transform.position, portal.warpToPos) : -1f;
                Log.Info($"[SyncHarness] sptele: arrived pos={SpVec(player.transform.position)} toWarp={SpF(toWarp)} seconds={SpF(Time.realtimeSinceStartup - started)} cinematic={player.cinematicModeActive} fallback={fallback}");
                yield break;
            }
        }

        Log.Info($"[SyncHarness] sptele: timeout began={began} moved={moved} fallback={fallback} activeTeleporter={SpActiveTeleporterText()} pos={(Player.main ? SpVec(Player.main.transform.position) : "none")} cinematic={(Player.main && Player.main.cinematicModeActive)}");
    }

    // ---- spremote

    private void LiveCommand_spremote(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 0f);
            SpLogRemote();
            if (seconds > 0f)
            {
                if (spRemoteRoutine != null)
                {
                    StopCoroutine(spRemoteRoutine);
                }

                spRemoteRoutine = StartCoroutine(SpRemoteLoop(seconds));
            }
        }
        catch (Exception exception)
        {
            SpWarn("spremote", exception);
        }
    }

    private IEnumerator SpRemoteLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            yield return new WaitForSecondsRealtime(1f);
            SpLogRemote();
        }
    }

    private static void SpLogRemote()
    {
        try
        {
            Vector3 local = Player.main ? Player.main.transform.position : Vector3.zero;
            Log.Info($"[SyncHarness] spremote: local pos={SpVec(local)} t={SpT()}");
            foreach (KeyValuePair<string, Vector3> body in SpBodies(false))
            {
                Log.Info($"[SyncHarness] spremote: player={SpTok(body.Key)} pos={SpVec(body.Value)} body=True dist={SpF(Vector3.Distance(local, body.Value))} t={SpT()}");
            }
        }
        catch (Exception exception)
        {
            SpWarn("spremote", exception);
        }
    }

    // ---- spgun

    private void LiveCommand_spgun(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, "state");
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] spgun: failed reason=no-player");
                return;
            }

            PrecursorDisableGunTerminal terminal = SpGunTerminal(player.transform.position);
            if (!terminal)
            {
                Log.Info($"[SyncHarness] spgun: refused reason=none mode={mode}");
                return;
            }

            if (mode == "state")
            {
                SpNearBlock("gun", 2000f);
                SpLogGunState(terminal);
            }
            else if (mode == "enter")
            {
                SpGunEnter(terminal);
            }
            else if (mode == "leave")
            {
                SpGunLeave();
            }
            else if (mode == "use")
            {
                SpGunUse(terminal);
            }
            else if (mode == "watch")
            {
                float seconds = LiveArgFloat(n, 1, 30f);
                if (spGunWatchRoutine != null)
                {
                    StopCoroutine(spGunWatchRoutine);
                }

                spGunWatchRoutine = StartCoroutine(SpGunWatch(terminal, seconds));
            }
            else
            {
                Log.Warn($"[SyncHarness] spgun: failed reason=unknown-mode mode={mode}");
            }
        }
        catch (Exception exception)
        {
            SpWarn("spgun", exception);
        }
    }

    private static void SpLogGunState(PrecursorDisableGunTerminal terminal)
    {
        Collider area = SpGunAreaCollider(terminal);
        bool inside = Player.main && SpInside(area, Player.main.transform.position);
        List<string> remoteInside = new List<string>();
        foreach (KeyValuePair<string, Vector3> body in SpBodies(false))
        {
            if (SpInside(area, body.Value))
            {
                remoteInside.Add(SpTok(body.Key));
            }
        }

        Log.Info($"[SyncHarness] spgun: state inside={inside} remoteInside={(remoteInside.Count > 0 ? string.Join(";", remoteInside.ToArray()) : "none")} opened={terminal.opened} playerCured={terminal.playerCured} ignore={terminal.ignorePlayer} firstUse={terminal.firstUse} gunDisabled={SpGunDisabledText()} area={(area ? SpVec(area.bounds.center) : "none")} areaSize={(area ? SpVec(area.bounds.size) : "none")} player={(Player.main ? SpVec(Player.main.transform.position) : "none")}");
    }

    private static void SpGunEnter(PrecursorDisableGunTerminal terminal)
    {
        Player player = Player.main;
        Collider area = SpGunAreaCollider(terminal);
        if (!area)
        {
            Log.Info("[SyncHarness] spgun: enter refused reason=no-area");
            return;
        }

        if (!spGunReturnSet)
        {
            spGunReturn = player.transform.position;
            spGunReturnSet = true;
        }

        Vector3 target = area.bounds.center + Vector3.up * 0.3f;
        player.OnPlayerPositionCheat();
        player.SetPosition(target);
        Log.Info($"[SyncHarness] spgun: enter pos={SpVec(target)} return={SpVec(spGunReturn)} inside={SpInside(area, target)} opened={terminal.opened}");
    }

    private static void SpGunLeave()
    {
        Player player = Player.main;
        Vector3 target = spGunReturnSet ? spGunReturn : player.transform.position + Vector3.right * 25f;
        spGunReturnSet = false;
        player.OnPlayerPositionCheat();
        player.SetPosition(target);
        Log.Info($"[SyncHarness] spgun: leave pos={SpVec(target)}");
    }

    private void SpGunUse(PrecursorDisableGunTerminal terminal)
    {
        Player player = Player.main;
        string id = SpEffectiveId(terminal);
        if (terminal.usingPlayer)
        {
            Log.Info($"[SyncHarness] spgun: refused id={id} reason=busy");
            return;
        }

        if (!terminal.opened)
        {
            Log.Info($"[SyncHarness] spgun: refused id={id} reason=closed gunDisabled={SpGunDisabledText()} ignore={terminal.ignorePlayer}");
            return;
        }

        // the cinematic moves the player to the terminal, which is inside the area: remember where he was, spgun leave takes him back
        if (!spGunReturnSet)
        {
            spGunReturn = player.transform.position;
            spGunReturnSet = true;
        }

        float started = Time.realtimeSinceStartup;
        bool curedBefore = terminal.playerCured;
        terminal.OnHandClick(player.guiHand);
        Log.Info($"[SyncHarness] spgun: clicked id={id} using={(terminal.usingPlayer ? "yes" : "no")} cured={curedBefore} firstUse={terminal.firstUse} cinematic={player.cinematicModeActive} t={SpT()}");
        StartCoroutine(SpGunUseWait(terminal, id, started));
    }

    private IEnumerator SpGunUseWait(PrecursorDisableGunTerminal terminal, string id, float started)
    {
        float limit = started + 60f;
        while (Time.realtimeSinceStartup < limit)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (!terminal)
            {
                break;
            }

            if (!terminal.usingPlayer && Time.realtimeSinceStartup - started > 1f)
            {
                SpLogGunEnd("used", terminal, id, started);
                yield break;
            }
        }

        SpLogGunEnd("timeout", terminal, id, started);
    }

    private static void SpLogGunEnd(string word, PrecursorDisableGunTerminal terminal, string id, float started)
    {
        try
        {
            string opened = terminal ? terminal.opened.ToString() : "gone";
            string cured = terminal ? terminal.playerCured.ToString() : "gone";
            string firstUse = terminal ? terminal.firstUse.ToString() : "gone";
            Log.Info($"[SyncHarness] spgun: {word} id={id} seconds={SpF(Time.realtimeSinceStartup - started)} opened={opened} gunDisabled={SpGunDisabledText()} playerCured={cured} firstUse={firstUse} granted={SpGunDisabledText()}");
        }
        catch (Exception exception)
        {
            SpWarn("spgun", exception);
        }
    }

    private IEnumerator SpGunWatch(PrecursorDisableGunTerminal terminal, float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        string last = "";
        while (Time.realtimeSinceStartup < end && terminal)
        {
            string now = $"opened={terminal.opened} using={(terminal.usingPlayer ? "yes" : "no")} gunDisabled={SpGunDisabledText()} cured={terminal.playerCured} ignore={terminal.ignorePlayer}";
            if (now != last)
            {
                last = now;
                Log.Info($"[SyncHarness] spgun: change t={SpT()} {now}");
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    // ---- spaq

    private void LiveCommand_spaq(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, "state");
            if (!Player.main)
            {
                Log.Warn("[SyncHarness] spaq: failed reason=no-player");
                return;
            }

            if (mode == "watch")
            {
                float seconds = LiveArgFloat(n, 1, 8f);
                if (spAqWatchRoutine != null)
                {
                    StopCoroutine(spAqWatchRoutine);
                }

                spAqWatchRoutine = StartCoroutine(SpAqWatch(seconds));
            }
            else
            {
                SpNearBlock("aq", 3000f);
            }
        }
        catch (Exception exception)
        {
            SpWarn("spaq", exception);
        }
    }

    private IEnumerator SpAqWatch(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end)
        {
            SpLogAq();
            yield return new WaitForSecondsRealtime(1f);
        }
    }

    private static void SpLogAq()
    {
        try
        {
            if (!Player.main)
            {
                return;
            }

            List<SpRow> rows = SpFind<PrecursorAquariumTeleporterTrigger>(Player.main.transform.position, 3000f);
            if (rows.Count == 0)
            {
                Log.Info($"[SyncHarness] spaq: t={SpT()} none=True");
                return;
            }

            PrecursorAquariumTeleporterTrigger trigger = (PrecursorAquariumTeleporterTrigger)rows[0].Item;
            float localDistance;
            float nearest = SpNearestPlayerDistance(trigger.transform.position, out localDistance);
            Log.Info($"[SyncHarness] spaq: t={SpT()} at={SpBool(trigger.targetAnimator, trigger.atTeleporterParameterName)} blow={SpBool(trigger.targetAnimator, trigger.blowTeleporterParameterName)} nearest={SpF(nearest)} local={SpF(localDistance)} swimRange={SpF(trigger.swimRange)} minRange={SpF(trigger.minRange)} maxRange={SpF(trigger.maxRange)}");
        }
        catch (Exception exception)
        {
            SpWarn("spaq", exception);
        }
    }

    // ---- spwatch

    private void LiveCommand_spwatch(NotificationCenter.Notification n)
    {
        try
        {
            float seconds = LiveArgFloat(n, 0, 30f);
            if (spWatchRoutine != null)
            {
                StopCoroutine(spWatchRoutine);
            }

            spWatchRoutine = StartCoroutine(SpWatchLoop(seconds));
        }
        catch (Exception exception)
        {
            SpWarn("spwatch", exception);
        }
    }

    private IEnumerator SpWatchLoop(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        string last = "";
        float lastLogged = -100f;
        while (Time.realtimeSinceStartup < end)
        {
            string signature = SpWatchSignature();
            if (signature != last || Time.realtimeSinceStartup - lastLogged >= 5f)
            {
                last = signature;
                lastLogged = Time.realtimeSinceStartup;
                Log.Info($"[SyncHarness] spwatch: t={SpT()} {signature}");
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    private static string SpWatchSignature()
    {
        try
        {
            if (!Player.main)
            {
                return "noplayer=True";
            }

            Vector3 origin = Player.main.transform.position;
            List<string> doors = new List<string>();
            foreach (SpRow row in SpFind<PrecursorDoorway>(origin, 150f))
            {
                PrecursorDoorway door = (PrecursorDoorway)row.Item;
                doors.Add($"{SpWatchId(door)}:{door.isOpen}");
            }

            List<string> keys = new List<string>();
            foreach (SpRow row in SpFind<PrecursorKeyTerminal>(origin, 150f))
            {
                PrecursorKeyTerminal terminal = (PrecursorKeyTerminal)row.Item;
                keys.Add($"{SpWatchId(terminal)}:{terminal.slotted}");
            }

            List<string> portals = new List<string>();
            foreach (SpRow row in SpFind<PrecursorTeleporter>(origin, 300f))
            {
                PrecursorTeleporter portal = (PrecursorTeleporter)row.Item;
                portals.Add($"{SpTok(portal.teleporterIdentifier)}:{portal.isOpen}");
            }

            List<string> terminals = new List<string>();
            foreach (SpRow row in SpFind<PrecursorTeleporterActivationTerminal>(origin, 300f))
            {
                PrecursorTeleporterActivationTerminal terminal = (PrecursorTeleporterActivationTerminal)row.Item;
                terminals.Add($"{SpWatchId(terminal)}:{terminal.unlocked}");
            }

            PrecursorDisableGunTerminal gun = SpGunTerminal(origin);
            string gunText = gun ? $"{gun.opened}/{SpGunDisabledText()}" : "none";
            return $"door={SpJoinOrNone(doors)} key={SpJoinOrNone(keys)} tele={SpJoinOrNone(portals)} teleterm={SpJoinOrNone(terminals)} gun={gunText} active=[{SpActiveList()}]";
        }
        catch (Exception exception)
        {
            return $"error={exception.GetType().Name}";
        }
    }

    private static string SpWatchId(Component component)
    {
        string id = SpEffectiveId(component);
        return id.Length >= 8 ? id.Substring(0, 8) : id;
    }

    private static string SpJoinOrNone(List<string> parts)
    {
        return parts.Count > 0 ? string.Join(",", parts.ToArray()) : "none";
    }

    // ---- sptelediag

    // sptelediag [radius]: why is there no portal near the player? (the first live run of story_precursor-tele: a client that was warped to the floating island found no PrecursorTeleporter within 200 m for two
    // minutes, while a client that joined at the same spot did). The portals of every loaded scene INCLUDING the inactive ones (spnear uses FindObjectsOfType, which skips them), the registered objects that are
    // called Teleporter, and how much of the world around the player is there at all (terrain, registered objects within 100 and 200 m). One block "begin / portal... / named... / end".
    private void LiveCommand_sptelediag(NotificationCenter.Notification n)
    {
        try
        {
            float radius = LiveArgFloat(n, 0, 250f);
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] sptelediag: failed reason=no-player");
                return;
            }

            Vector3 origin = player.transform.position;
            Log.Info($"[SyncHarness] sptelediag: begin player={SpVec(origin)} radius={SpF(radius)} terrainLoaded={NitroxClient.GameLogic.Terrain.IsLoadedAt(origin)} who={SpMe()}");
            int portals = 0;
            int portalsAll = 0;
            foreach (PrecursorTeleporter portal in Resources.FindObjectsOfTypeAll<PrecursorTeleporter>())
            {
                // Resources.FindObjectsOfTypeAll also returns the prefab assets in memory: only objects of a scene are in the world
                if (!portal || !portal.gameObject.scene.IsValid())
                {
                    continue;
                }

                float dist = Vector3.Distance(origin, portal.transform.position);
                if (dist > radius)
                {
                    continue;
                }

                portalsAll++;
                if (portals >= SP_MAX_ROWS)
                {
                    continue;
                }

                portals++;
                Log.Info($"[SyncHarness] sptelediag: portal {SpCommon(portal, dist)} ident={SpTok(portal.teleporterIdentifier)} activeSelf={portal.gameObject.activeSelf} activeInHierarchy={portal.gameObject.activeInHierarchy} enabled={portal.enabled} isOpen={portal.isOpen} path={SpPath(portal.transform)}");
            }

            int registered = 0;
            int near100 = 0;
            int near200 = 0;
            int named = 0;
            foreach (KeyValuePair<NitroxId, GameObject> pair in NitroxEntity.GetGameObjects())
            {
                GameObject registeredObject = pair.Value;
                if (!registeredObject)
                {
                    continue;
                }

                registered++;
                float dist = Vector3.Distance(origin, registeredObject.transform.position);
                if (dist <= 100f)
                {
                    near100++;
                }

                if (dist <= 200f)
                {
                    near200++;
                }

                if (dist <= radius && registeredObject.name.IndexOf("Teleporter", StringComparison.OrdinalIgnoreCase) >= 0 && named < SP_MAX_ROWS)
                {
                    named++;
                    Log.Info($"[SyncHarness] sptelediag: named id={pair.Key} name={SpTok(registeredObject.name)} pos={SpVec(registeredObject.transform.position)} dist={SpF(dist)} activeInHierarchy={registeredObject.activeInHierarchy} path={SpPath(registeredObject.transform)}");
                }
            }

            Log.Info($"[SyncHarness] sptelediag: end portals={portalsAll} named={named} entities100={near100} entities200={near200} registered={registered}");
        }
        catch (Exception exception)
        {
            SpWarn("sptelediag", exception);
        }
    }

    // ---- spplaces

    private const int SP_MAX_PLACES = 60;
    private const string SP_PLACE_WORDS = "aquarium|prison|emperor|incubator|contain|quarantine|gun|mountain|floating|island|teleport|precursor|lava|lost";

    // The named places of the game's own teleport table (the console command "goto", GotoConsoleCommand.main.data.locations) whose name contains one of the words (separated by |, default the Precursor list,
    // "all" lists every place): the script uses it to name the places it stands at and to find the aquarium, whose position nobody has derived
    private void LiveCommand_spplaces(NotificationCenter.Notification n)
    {
        try
        {
            GotoConsoleCommand go = GotoConsoleCommand.main;
            if (!go)
            {
                go = FindObjectOfType<GotoConsoleCommand>();
            }

            if (!go || !go.data || go.data.locations == null)
            {
                Log.Warn("[SyncHarness] spplaces: failed reason=no-locations");
                return;
            }

            string pattern = LiveArgString(n, 0, SP_PLACE_WORDS);
            bool all = pattern == "all";
            string[] words = pattern.Split('|');
            TeleportPosition[] locations = go.data.locations;
            List<string> lines = new List<string>();
            int matched = 0;
            for (int i = 0; i < locations.Length; i++)
            {
                TeleportPosition location = locations[i];
                if (location == null || string.IsNullOrEmpty(location.name))
                {
                    continue;
                }

                bool match = all;
                for (int w = 0; w < words.Length && !match; w++)
                {
                    match = words[w].Length > 0 && location.name.IndexOf(words[w], StringComparison.OrdinalIgnoreCase) >= 0;
                }

                if (!match)
                {
                    continue;
                }

                matched++;
                if (lines.Count < SP_MAX_PLACES)
                {
                    lines.Add($"[SyncHarness] spplaces: place name={SpTok(location.name)} pos={SpVec(location.position)}");
                }
            }

            Log.Info($"[SyncHarness] spplaces: begin pattern={SpTok(pattern)} total={locations.Length}");
            foreach (string line in lines)
            {
                Log.Info(line);
            }

            Log.Info($"[SyncHarness] spplaces: end pattern={SpTok(pattern)} matched={matched} listed={lines.Count} total={locations.Length}");
        }
        catch (Exception exception)
        {
            SpWarn("spplaces", exception);
        }
    }

    // ---- spgoto / spreset

    private void LiveCommand_spgoto(NotificationCenter.Notification n)
    {
        try
        {
            Vector3 target;
            if (!SpTryVec(LiveArgString(n, 0, ""), out target))
            {
                Log.Warn("[SyncHarness] spgoto: failed reason=needs-x,y,z");
                return;
            }

            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] spgoto: failed reason=no-player");
                return;
            }

            player.OnPlayerPositionCheat();
            player.SetPosition(target);
            bool settled = LargeWorldStreamer.main && LargeWorldStreamer.main.IsWorldSettled();
            Log.Info($"[SyncHarness] spgoto: to={SpVec(target)} pos={SpVec(player.transform.position)} settled={settled}");
        }
        catch (Exception exception)
        {
            SpWarn("spgoto", exception);
        }
    }

    private void LiveCommand_spreset(NotificationCenter.Notification n)
    {
        try
        {
            Player player = Player.main;
            if (!player)
            {
                Log.Warn("[SyncHarness] spreset: failed reason=no-player");
                return;
            }

            bool cinematicBefore = player.cinematicModeActive;
            bool inputBefore = player.playerController && player.playerController.inputEnabled;
            player.cinematicModeActive = false;
            if (player.playerController)
            {
                player.playerController.inputEnabled = true;
            }

            Log.Info($"[SyncHarness] spreset: cinematic {cinematicBefore} -> {player.cinematicModeActive}, input {inputBefore} -> {(player.playerController && player.playerController.inputEnabled)}");
        }
        catch (Exception exception)
        {
            SpWarn("spreset", exception);
        }
    }
}
#endif
