#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Live check K18 (a harness script that is not part of this repository, scripts k18bleeder.ps1, k18mesmer.ps1, k18warper.ps1). Debug commands, run on a client with the server's
///     <c>/console &lt;player&gt; &lt;command&gt; [arg1] [arg2]</c> (the console lower-cases the arguments; <c>/console</c> passes at most two of them):<br/>
///     - <c>k18bleederstate</c> logs, for the (up to six) Bleeders nearest to the local player, the simulation lock this client holds on each (<c>none</c>, <c>TRANSIENT</c> or
///     <c>EXCLUSIVE</c>), the state of its <c>AttachAndSuck</c> (target, active, attached) and of its copy (kinematic, remote, collider), then one <c>done</c> line with the local player's
///     health, oxygen, game mode and the state of the player's <c>BleederAttachTarget</c> (<c>occupied</c>)<br/>
///     - <c>k18tpcreature &lt;tech type|id prefix&gt; [lift]</c> puts the local player at the position of the nearest live creature of that tech type (or whose NitroxId starts with the text),
///     <c>lift</c> metres higher: the Bleeder's contact is a physics callback (<c>OnCollisionEnter</c>) that needs the player's collider to touch the Bleeder's<br/>
///     - <c>k18bleederdetach</c> calls <c>AttachAndSuck.SetDetached</c> on the nearest Bleeder that has a target on this client: the one exit of its attack, the same call the game makes when
///     the Bleeder lets go (the victim enters a base, the victim dies), without killing it, so that the return of the lock to the normal hand-over can be watched. It logs the lock before the
///     let-go, the facts right after it (<c>after</c>) and the same facts 2 s later (<c>later</c>, inside the 4 s in which vanilla accepts no new contact, so that a Bleeder that is still on top
///     of the player cannot have attached again)<br/>
///     - <c>k18mesmerstate</c> logs every Mesmer (lock, hypnosis state, current target and whether that is a remote player's body), what <c>PullCreatures.IsValidTarget</c> answers for every
///     remote player's body (as it is, and with the body turned towards the Mesmer for that one call) together with the vanilla preconditions of that answer (tag, distance window, living
///     <c>LiveMixin</c>, <c>LooksAtMe</c>), so that a False can be told from "vanilla would say False anyway", and the strength of the local screen swirl<br/>
///     - <c>k18warperstate</c> logs every loaded <c>WarperSpawner</c> (position, distance, whether it has a Warper, seconds until it makes the next one) and every <c>Warper</c>
/// </summary>
public partial class HarnessConsoleCommands
{
    private static string K18Pos(Vector3 position)
    {
        return $"({position.x:F1},{position.y:F1},{position.z:F1})";
    }

    private static string K18IdText(Component component)
    {
        return component && component.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id) ? id.ToString() : "noid";
    }

    /// <summary>
    ///     The simulation lock this client holds on the entity of the component: <c>none</c>, <c>TRANSIENT</c> or <c>EXCLUSIVE</c> (<c>noid</c> for an object the server does not know).
    /// </summary>
    private static string K18LockText(Component component)
    {
        if (!component || !component.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId id))
        {
            return "noid";
        }

        NitroxClient.GameLogic.SimulationOwnership ownership = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>();
        return ownership.TryGetLockType(id, out SimulationLockType lockType) ? lockType.ToString() : "none";
    }

    private static BleederAttachTarget K18PlayerTarget(Player player)
    {
        BleederAttachTarget target = player.GetComponentInChildren<BleederAttachTarget>(true);
        return target ? target : player.GetComponentInParent<BleederAttachTarget>();
    }

    private void LiveCommand_k18bleederstate(NotificationCenter.Notification n)
    {
        try
        {
            K18LogBleederState();
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] k18bleederstate: probe error: {exception.Message}");
        }
    }

    private static void K18LogBleederState()
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Warn("[SyncHarness] k18bleederstate: no local player");
            return;
        }

        Vector3 here = player.transform.position;
        List<Bleeder> bleeders = new(FindObjectsOfType<Bleeder>());
        bleeders.Sort((first, second) => (first.transform.position - here).sqrMagnitude.CompareTo((second.transform.position - here).sqrMagnitude));

        int shown = 0;
        foreach (Bleeder bleeder in bleeders)
        {
            if (shown >= 6)
            {
                break;
            }
            shown++;

            AttachAndSuck attack = bleeder.attachAndSuck ? bleeder.attachAndSuck : bleeder.GetComponent<AttachAndSuck>();
            LiveMixin live = bleeder.GetComponent<LiveMixin>();
            Rigidbody body = bleeder.GetComponent<Rigidbody>();
            Collider bleederCollider = bleeder.GetComponent<Collider>();
            bool alive = !live || live.IsAlive();
            bool hasTarget = attack && attack.currentTarget;
            string attackText = attack ? $"hasTarget={hasTarget} active={attack.active} attached={attack.attached} enabled={attack.isActiveAndEnabled}" : "hasTarget=False active=False attached=False enabled=False";
            string bodyText = body ? $"kinematic={body.isKinematic}" : "kinematic=none";
            bool remote = bleeder.GetComponent<RemotelyControlled>() != null;
            Vector3 position = bleeder.transform.position;
            Log.Info($"[SyncHarness] k18bleederstate: bleeder id={K18IdText(bleeder)} dist={(position - here).magnitude:F1} pos={K18Pos(position)} alive={alive} lock={K18LockText(bleeder)} {attackText} {bodyText} remote={remote} colliderOn={(bleederCollider ? bleederCollider.enabled.ToString() : "none")} health={(live ? live.health : -1f):F1}");
        }

        BleederAttachTarget target = K18PlayerTarget(player);
        LiveMixin playerLive = player.liveMixin;
        float oxygen = player.oxygenMgr ? player.oxygenMgr.GetOxygenAvailable() : -1f;
        Log.Info($"[SyncHarness] k18bleederstate: done count={bleeders.Count} survival={GameModeUtils.RequiresSurvival()} health={(playerLive ? playerLive.health : -1f):F1} oxygen={oxygen:F1} underwater={player.IsUnderwater()} mode={player.GetMode()} occupied={(target ? target.occupied.ToString() : "none")} targetAttached={(target ? target.attached.ToString() : "none")} pos={K18Pos(here)} targetBleeder={(target && target.bleeder ? target.bleeder.name : "none")}");
    }

    private void LiveCommand_k18tpcreature(NotificationCenter.Notification n)
    {
        string wanted = LiveArgString(n, 0, "bleeder");
        float lift = LiveArgFloat(n, 1, 0f);
        Player player = Player.main;
        if (!player)
        {
            Log.Warn("[SyncHarness] k18tpcreature: no local player");
            return;
        }

        // A tech type name ("bleeder", the console lower-cases it), else the first characters of a NitroxId
        bool byTech = Enum.TryParse(wanted, true, out TechType techType) && Enum.IsDefined(typeof(TechType), techType);
        Creature nearest = null;
        float nearestSqr = float.MaxValue;
        foreach (Creature creature in FindObjectsOfType<Creature>())
        {
            if (!creature.isActiveAndEnabled)
            {
                continue;
            }

            bool matches = byTech
                ? CraftData.GetTechType(creature.gameObject) == techType
                : creature.TryGetNitroxId(out Nitrox.Model.DataStructures.NitroxId creatureId) && creatureId.ToString().StartsWith(wanted, StringComparison.Ordinal);
            LiveMixin live = creature.GetComponent<LiveMixin>();
            if (!matches || (live && !live.IsAlive()))
            {
                continue;
            }

            float sqr = (creature.transform.position - player.transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearest = creature;
                nearestSqr = sqr;
            }
        }

        if (!nearest)
        {
            Log.Warn($"[SyncHarness] k18tpcreature: no live creature matching '{wanted}' is loaded on this client");
            return;
        }

        Vector3 before = player.transform.position;
        Vector3 target = nearest.transform.position + new Vector3(0f, lift, 0f);
        player.OnPlayerPositionCheat();
        player.SetPosition(target);
        Log.Info($"[SyncHarness] k18tpcreature: player moved from {K18Pos(before)} to {K18Pos(player.transform.position)}, {Mathf.Sqrt(nearestSqr):F1} m away, onto {CraftData.GetTechType(nearest.gameObject)} {K18IdText(nearest)} at {K18Pos(nearest.transform.position)}, lift {lift:F1}");
    }

    private void LiveCommand_k18bleederdetach(NotificationCenter.Notification n)
    {
        Player player = Player.main;
        Vector3 here = player ? player.transform.position : Vector3.zero;
        AttachAndSuck nearest = null;
        float nearestSqr = float.MaxValue;
        foreach (AttachAndSuck attack in FindObjectsOfType<AttachAndSuck>())
        {
            if (!attack.currentTarget)
            {
                continue;
            }

            float sqr = (attack.transform.position - here).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearest = attack;
                nearestSqr = sqr;
            }
        }

        if (!nearest)
        {
            Log.Warn("[SyncHarness] k18bleederdetach: no Bleeder has a target on this client");
            return;
        }

        // The lock on the "letting go" line is the one before the let-go; the "after" and "later" lines are the facts the scripts judge (the Bleeder cannot take a new target for 4 s)
        string detachedId = K18IdText(nearest);
        Log.Info($"[SyncHarness] k18bleederdetach: letting go {detachedId}: target {nearest.currentTarget.name}, active {nearest.active}, attached {nearest.attached}, lock {K18LockText(nearest)}");
        nearest.SetDetached();
        Log.Info($"[SyncHarness] k18bleederdetach: done {detachedId}, hasTarget {(bool)nearest.currentTarget}");
        Log.Info($"[SyncHarness] k18bleederdetach: after id={detachedId} {K18DetachFacts(nearest, player)}");
        StartCoroutine(K18DetachLater(nearest, detachedId, 2f));
    }

    private static string K18DetachFacts(AttachAndSuck attack, Player player)
    {
        BleederAttachTarget target = player ? K18PlayerTarget(player) : null;
        bool hasTarget = attack && attack.currentTarget;
        return $"hasTarget={hasTarget} occupied={(target ? target.occupied.ToString() : "none")} lock={K18LockText(attack)}";
    }

    private static IEnumerator K18DetachLater(AttachAndSuck attack, string id, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Log.Info($"[SyncHarness] k18bleederdetach: later id={id} seconds={seconds:F1} {K18DetachFacts(attack, Player.main)}");
    }

    private void LiveCommand_k18mesmerstate(NotificationCenter.Notification n)
    {
        try
        {
            K18LogMesmerState();
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] k18mesmerstate: probe error: {exception.Message}");
        }
    }

    private static void K18LogMesmerState()
    {
        Player player = Player.main;
        if (!player)
        {
            Log.Warn("[SyncHarness] k18mesmerstate: no local player");
            return;
        }

        Vector3 here = player.transform.position;
        List<GameObject> remoteBodies = [];
        foreach (NitroxClient.GameLogic.RemotePlayer remote in Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.PlayerManager>().GetAll())
        {
            if (remote.Body)
            {
                remoteBodies.Add(remote.Body);
            }
        }

        int mesmers = 0;
        foreach (Mesmer mesmer in FindObjectsOfType<Mesmer>())
        {
            mesmers++;
            PullCreatures pull = mesmer.GetComponentInChildren<PullCreatures>(true);
            GameObject current = pull ? pull.currentTarget : null;
            bool currentIsRemote = current && current.GetComponentInParent<NitroxClient.GameLogic.PlayerLogic.RemotePlayerIdentifier>();
            bool currentIsLocal = current && current == player.gameObject;
            LiveMixin live = mesmer.GetComponent<LiveMixin>();
            bool alive = !live || live.IsAlive();
            string mesmerId = K18IdText(mesmer);
            Log.Info($"[SyncHarness] k18mesmerstate: mesmer id={mesmerId} dist={(mesmer.transform.position - here).magnitude:F1} lock={K18LockText(mesmer)} alive={alive} pullActive={(pull ? pull.isActive.ToString() : "none")} targetIsRemote={currentIsRemote} targetIsLocal={currentIsLocal} allTargets={PullCreatures.allTargets.Count} target={(current ? current.name : "none")}");

            foreach (GameObject body in remoteBodies)
            {
                K18LogMesmerRemote(mesmer, pull, mesmerId, body);
            }
        }

        MesmerizedScreenFXController fx = FindObjectOfType<MesmerizedScreenFXController>();
        Log.Info($"[SyncHarness] k18mesmerstate: done mesmers={mesmers} remotes={remoteBodies.Count} fxAmount={(fx ? fx.amount.ToString("F2") : "none")} pos={K18Pos(here)}");
    }

    /// <summary>
    ///     One line for a (Mesmer, remote player body) pair: what IsValidTarget answers (<c>valid</c>), what it answers when the body looks at the Mesmer (<c>validFacing</c>, the body is turned for
    ///     that one call and turned back at once), and the preconditions that vanilla checks before the patch could matter: tag Creature/Player, the body inside [minDistance, maxDistance] of the
    ///     PullCreatures, a living LiveMixin, and LooksAtMe (angle of the body's forward and a free line of sight). A False answer proves the patch only when all four hold.
    /// </summary>
    private static void K18LogMesmerRemote(Mesmer mesmer, PullCreatures pull, string mesmerId, GameObject body)
    {
        bool valid = pull && pull.IsValidTarget(body);
        bool validFacing = false;
        bool looks = false;
        bool inRange = false;
        float minDistance = pull ? pull.minDistance : -1f;
        float maxDistance = pull ? pull.maxDistance : -1f;
        Vector3 mesmerPosition = pull ? pull.transform.position : mesmer.transform.position;
        float distance = (body.transform.position - mesmerPosition).magnitude;
        bool tagOk = body.CompareTag("Player") || body.CompareTag("Creature");
        LiveMixin bodyLive = body.GetComponent<LiveMixin>();
        bool hasLiveMixin = bodyLive && bodyLive.IsAlive();

        if (pull)
        {
            float sqr = (body.transform.position - mesmerPosition).sqrMagnitude;
            inRange = sqr <= maxDistance * maxDistance && sqr >= minDistance * minDistance;
            Quaternion savedRotation = body.transform.rotation;
            try
            {
                Vector3 toMesmer = mesmerPosition - body.transform.position;
                if (toMesmer.sqrMagnitude > 0.0001f)
                {
                    body.transform.rotation = Quaternion.LookRotation(toMesmer);
                }

                looks = pull.LooksAtMe(body);
                validFacing = pull.IsValidTarget(body);
            }
            catch (Exception exception)
            {
                Log.Warn($"[SyncHarness] k18mesmerstate: facing probe error: {exception.Message}");
            }
            finally
            {
                body.transform.rotation = savedRotation;
            }
        }

        Log.Info($"[SyncHarness] k18mesmerstate: remote valid={valid} validFacing={validFacing} mesmer={mesmerId} tag={body.tag} dist={distance:F1} tagOk={tagOk} inRange={inRange} min={minDistance:F1} max={maxDistance:F1} liveMixin={hasLiveMixin} looks={looks} body={body.name}");
    }

    private void LiveCommand_k18warperstate(NotificationCenter.Notification n)
    {
        try
        {
            K18LogWarperState();
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] k18warperstate: probe error: {exception.Message}");
        }
    }

    private static void K18LogWarperState()
    {
        Player player = Player.main;
        Vector3 here = player ? player.transform.position : Vector3.zero;
        int spawners = 0;
        foreach (WarperSpawner spawner in FindObjectsOfType<WarperSpawner>())
        {
            spawners++;
            NitroxEntity entity = spawner.GetComponentInParent<NitroxEntity>();
            Vector3 position = spawner.transform.position;
            Log.Info($"[SyncHarness] k18warperstate: spawner pos={K18Pos(position)} dist={(position - here).magnitude:F1} enabled={spawner.isActiveAndEnabled} warper={(spawner.warper ? "yes" : "no")} nextWarpIn={(spawner.nextWarpTime - Time.time):F1} warpPause={spawner.warpPause:F1} warpInterval={spawner.warpInterval:F1} entity={(entity ? entity.Id.ToString() : "none")}");
        }

        int warpers = 0;
        foreach (Warper warper in FindObjectsOfType<Warper>())
        {
            warpers++;
            Vector3 position = warper.transform.position;
            Log.Info($"[SyncHarness] k18warperstate: warper pos={K18Pos(position)} dist={(position - here).magnitude:F1} active={warper.isActiveAndEnabled}");
        }

        Log.Info($"[SyncHarness] k18warperstate: done spawners={spawners} warpers={warpers} pos={K18Pos(here)}");
    }
}
#endif
