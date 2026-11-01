using System;
using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Gui.Input;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// The buddy oxygen in the game (docs/coop/BUDDY_OXYGEN.md): a diver holds the share key (B unless the options say otherwise) while looking at a teammate within the range, and air flows from their tank into the
/// teammate's, <c>BuddyOxygenRate</c> seconds of air per second, down to the diver's own floor. A player's oxygen belongs to their own game, so every step is the same few messages through the server
/// (<see cref="BuddyOxygenTransfer" />): the <b>giver</b> takes the step out of their own tank first (<see cref="Tick" />) and says so, the <b>receiver's</b> game adds as much as fits and answers
/// (<see cref="OnDeliver" />), and the giver gets what did not fit back (<see cref="OnSettle" />). What the giver lost is what the receiver got. The numbers and the arithmetic are in
/// <see cref="BuddyOxygenRules" />.
/// </summary>
public static class BuddyOxygenShare
{
    private static PlayerManager playerManager;
    private static IPacketSender packetSender;
    private static bool subscribed;

    private static SessionId? sharingWith;
    private static string sharingName = string.Empty;
    private static float sinceLastGive;
    private static int emptyStreak;
    private static bool gaveUp;
    private static float receivingUntil;
    private static GameInput.Button? keyButton;
    private static bool keyBroken;

    // the harness (a harness script that is not part of this repository) holds the key by command: until when, for whom, and without the client's own range and view checks
    private static float overrideUntil;
    private static string overrideName;
    private static bool overrideForce;

    private static Totals stats;

    /// <summary>What the buddy oxygen moved on this client since the session began: for the live check, which compares the two clients (what one gave is what the other received).</summary>
    public struct Totals
    {
        /// <summary>Steps this client gave, and the air it took out of its own tank for them.</summary>
        public int Gives;

        public float Taken;

        /// <summary>Answers of the server to those steps: what the receivers took, what came back, and the answers that were refusals of the server.</summary>
        public int Settles;

        public float Accepted;
        public float Refunded;
        public float RefundApplied;
        public int Refused;

        /// <summary>Steps that arrived for this client as receiver, and the air it added to its own tank; the steps in which nothing fitted.</summary>
        public int Deliveries;

        public float Received;
        public int DeliveriesWithNothing;

        // The air of this player's tank around the first and the last step (Time.time and seconds of air): the live check reads how far the tanks moved from them, to set beside the counters above.

        /// <summary>The giver: the tank before the first step took air out, and after the last one did.</summary>
        public float GiveFirstAt;

        public float GiveFirstBefore;
        public float GiveLastAt;
        public float GiveLastAfter;

        /// <summary>The receiver: the tank before the first step that added air, and after the last one did.</summary>
        public float DeliverFirstAt;

        public float DeliverFirstBefore;
        public float DeliverLastAt;
        public float DeliverLastAfter;
    }

    public static Totals Stats => stats;

    /// <summary>The sharing runs when this client has joined, the server's rate is not 0 and the game mode needs air at all.</summary>
    public static bool Active => Multiplayer.Main && Multiplayer.Main.InitialSyncCompleted && VehicleUpgradeSettings.Received && BuddyOxygenRules.IsEnabled(VehicleUpgradeSettings.BuddyOxygenRate) && GameModeUtils.RequiresOxygen() && Player.main;

    /// <summary>The teammate this client is giving air to right now (null when it is not giving).</summary>
    public static SessionId? SharingWith => sharingWith;

    public static void Initialize(PlayerManager players, IPacketSender sender)
    {
        playerManager = players;
        packetSender = sender;
        if (!subscribed)
        {
            subscribed = true;
            Multiplayer.OnAfterMultiplayerEnd += Reset;
        }
    }

    /// <summary>The session ended: nothing of this session may be left over for the next one.</summary>
    public static void Reset()
    {
        sharingWith = null;
        sharingName = string.Empty;
        sinceLastGive = 0f;
        emptyStreak = 0;
        gaveUp = false;
        receivingUntil = 0f;
        overrideUntil = 0f;
        overrideName = null;
        overrideForce = false;
        stats = default;
    }

    /// <summary>The harness holds the key for <paramref name="seconds" />, aimed at the teammate of that name (<paramref name="force" />: without the client's own range and view checks, so that the server's can be tested).</summary>
    internal static void HoldFor(string buddyName, float seconds, bool force)
    {
        overrideName = buddyName;
        overrideUntil = Time.time + seconds;
        overrideForce = force;
        gaveUp = false;
    }

    internal static void ReleaseOverride()
    {
        overrideUntil = 0f;
        overrideName = null;
        overrideForce = false;
    }

    internal static void ResetStats() => stats = default;

    // ---- the giver

    /// <summary>Run every frame by <c>BuddyOxygenDriver</c>.</summary>
    public static void Tick(float deltaTime)
    {
        if (!Active || playerManager == null || packetSender == null)
        {
            StopSharing("the buddy oxygen is not active", false);
            return;
        }

        bool held = KeyHeld();
        if (!held)
        {
            gaveUp = false;
        }
        float range = VehicleUpgradeSettings.BuddyOxygenRange;
        float floor = VehicleUpgradeSettings.BuddyOxygenFloor;
        bool giverReady = GiverReady();
        RemotePlayer buddy = giverReady ? FindBuddy(range, out _) : null;
        ShowHint(buddy, held, floor);
        if (!held)
        {
            StopSharing("the key was released", false);
            return;
        }
        if (!giverReady || buddy == null)
        {
            StopSharing(giverReady ? "nobody is in view" : "the giver cannot give now", false);
            return;
        }
        if (gaveUp)
        {
            return;
        }

        float available = Player.main.oxygenMgr.GetOxygenAvailable();
        if (BuddyOxygenRules.StepAmount(VehicleUpgradeSettings.BuddyOxygenRate, BuddyOxygenRules.TICK_SECONDS, available, floor) <= 0f)
        {
            StopSharing("the giver is down to their own floor", true);
            ErrorMessage.AddMessage(Language.main.Get("Nitrox_BuddyOxygenFloor"));
            return;
        }
        if (sharingWith != buddy.SessionId)
        {
            StopSharing("the giver looked at somebody else", false);
            sharingWith = buddy.SessionId;
            sharingName = buddy.PlayerName;
            sinceLastGive = BuddyOxygenRules.TICK_SECONDS;
            emptyStreak = 0;
            Log.Info($"[BuddyOxygen] sharing air with {sharingName}: the giver has {available:0.0} s of air, keeps {floor:0.#} s, rate {VehicleUpgradeSettings.BuddyOxygenRate:0.#} per second");
        }

        sinceLastGive += deltaTime;
        if (sinceLastGive >= BuddyOxygenRules.TICK_SECONDS)
        {
            Give(buddy, sinceLastGive, available, floor);
            sinceLastGive = 0f;
        }
    }

    private static void Give(RemotePlayer buddy, float elapsed, float available, float floor)
    {
        float amount = BuddyOxygenRules.StepAmount(VehicleUpgradeSettings.BuddyOxygenRate, elapsed, available, floor);
        if (amount <= 0f)
        {
            StopSharing("the giver is down to their own floor", true);
            return;
        }
        // The air leaves the giver's tank first: a lost packet loses air and never makes any
        float taken = Player.main.oxygenMgr.RemoveOxygen(amount);
        if (taken < BuddyOxygenRules.MIN_AMOUNT)
        {
            StopSharing("the giver has nothing to take", true);
            return;
        }
        if (stats.Gives == 0)
        {
            stats.GiveFirstAt = Time.time;
            stats.GiveFirstBefore = available;
        }
        stats.GiveLastAt = Time.time;
        stats.GiveLastAfter = Player.main.oxygenMgr.GetOxygenAvailable();
        stats.Gives++;
        stats.Taken += taken;
        packetSender.Send(BuddyOxygenTransfer.Give(buddy.SessionId, taken));
    }

    private static bool KeyHeld()
    {
        if (Time.time < overrideUntil)
        {
            return true;
        }
        if (keyBroken || !AvatarInputHandler.main || !AvatarInputHandler.main.IsEnabled())
        {
            return false;
        }
        try
        {
            keyButton ??= (GameInput.Button)(KeyBindingManager.NITROX_BASE_ID + KeyBindingManager.KeyBindings.FindIndex(binding => binding is BuddyOxygenKeyBindingAction));
            return GameInput.GetButtonHeld(keyButton.Value);
        }
        catch (Exception exception)
        {
            keyBroken = true;
            Log.Error(exception, "[BuddyOxygen] the share key could not be read; the key is off for this session");
            return false;
        }
    }

    /// <summary>Whether this player can give air now: alive, swimming under water (not in a vehicle, a base or a Cyclops, where the air is breathable).</summary>
    private static bool GiverReady()
    {
        Player player = Player.main;
        return player && player.liveMixin && player.liveMixin.IsAlive() && player.oxygenMgr && player.IsUnderwater() && !player.IsInSub() && !player.currentMountedVehicle;
    }

    /// <summary>
    /// The teammate that the giver means: the one in view (within <see cref="BuddyOxygenRules.CONE_DEGREES" /> of the middle of the screen) within the range, swimming under water. With the harness's override, the one
    /// of that name.
    /// </summary>
    private static RemotePlayer FindBuddy(float range, out float distance)
    {
        distance = float.MaxValue;
        Camera camera = Camera.main;
        if (!camera)
        {
            return null;
        }
        List<RemotePlayer> players = [];
        List<(float Distance, float AngleDegrees)> candidates = [];
        bool overridden = overrideName != null && Time.time < overrideUntil;
        foreach (RemotePlayer remote in playerManager.GetAll())
        {
            if (remote.Body == null || !remote.Body.activeInHierarchy)
            {
                continue;
            }
            Vector3 center = remote.Collider ? remote.Collider.bounds.center : remote.Body.transform.position;
            Vector3 toBuddy = center - camera.transform.position;
            if (overridden)
            {
                if (!string.Equals(remote.PlayerName, overrideName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                distance = toBuddy.magnitude;
                // forced: no check of this client at all, so that the checks of the server and of the receiver's game can be tested
                return overrideForce || (CanBeGivenTo(remote, center) && BuddyOxygenRules.InRange(distance, range, 0f)) ? remote : null;
            }
            if (!CanBeGivenTo(remote, center))
            {
                continue;
            }
            players.Add(remote);
            candidates.Add((toBuddy.magnitude, Vector3.Angle(camera.transform.forward, toBuddy)));
        }
        int picked = BuddyOxygenRules.PickBuddy(candidates, range);
        if (picked < 0)
        {
            return null;
        }
        distance = candidates[picked].Distance;
        return players[picked];
    }

    /// <summary>A teammate that air can flow to: swimming under water, not in a vehicle, a base or a Cyclops.</summary>
    private static bool CanBeGivenTo(RemotePlayer remote, Vector3 center) => !remote.Vehicle && !remote.SubRoot && center.y <= Ocean.GetOceanLevel() - 0.5f;

    private static void ShowHint(RemotePlayer buddy, bool held, float floor)
    {
        if (buddy == null || !HandReticle.main || !AvatarInputHandler.main || !AvatarInputHandler.main.IsEnabled() || !Player.main.oxygenMgr)
        {
            return;
        }
        // The game's own text wins: when it already put a hand text on the reticle this frame (open a locker, pick up an item), the hint does not take its place (the reticle clears its texts every frame)
        if (!string.IsNullOrEmpty(HandReticle.main.textHand))
        {
            return;
        }
        bool sharing = held && sharingWith == buddy.SessionId && !gaveUp;
        if (!sharing && Player.main.oxygenMgr.GetOxygenAvailable() <= floor + BuddyOxygenRules.MIN_AMOUNT)
        {
            return;
        }
        string text = string.Format(Language.main.Get(sharing ? "Nitrox_BuddyOxygenSharing" : "Nitrox_BuddyOxygenHint"), buddy.PlayerName);
        HandReticle.main.SetText(HandReticle.TextType.Hand, text, false, keyButton ?? GameInput.Button.None);
    }

    private static void StopSharing(string reason, bool giveUp)
    {
        if (giveUp)
        {
            gaveUp = true;
        }
        if (sharingWith == null)
        {
            return;
        }
        float left = Player.main && Player.main.oxygenMgr ? Player.main.oxygenMgr.GetOxygenAvailable() : -1f;
        Log.Info($"[BuddyOxygen] stopped sharing air with {sharingName}: {reason} (the giver has {left:0.0} s of air left; this client gave {stats.Taken:0.0}, took back {stats.RefundApplied:0.0}; {stats.Accepted:0.0} was taken by the receivers so far)");
        sharingWith = null;
        sharingName = string.Empty;
        sinceLastGive = 0f;
        emptyStreak = 0;
    }

    /// <summary>The server says how a step ended: the receiver took <see cref="BuddyOxygenTransfer.Amount" />, and <see cref="BuddyOxygenTransfer.Refund" /> goes back into this player's tank.</summary>
    private static void OnSettle(BuddyOxygenTransfer packet)
    {
        stats.Settles++;
        stats.Accepted += packet.Amount;
        stats.Refunded += packet.Refund;
        if (packet.TransferId == 0)
        {
            stats.Refused++;
        }
        if (packet.Refund > 0f && Player.main && Player.main.oxygenMgr)
        {
            stats.RefundApplied += Player.main.oxygenMgr.AddOxygen(packet.Refund);
        }

        if (packet.Amount >= BuddyOxygenRules.MIN_AMOUNT)
        {
            emptyStreak = 0;
            return;
        }
        emptyStreak++;
        if (sharingWith.HasValue && sharingWith.Value == packet.Receiver && emptyStreak >= BuddyOxygenRules.GIVE_UP_AFTER_EMPTY_STEPS)
        {
            StopSharing($"the receiver took nothing ({packet.Reason})", true);
            string key = packet.Reason switch
            {
                BuddyOxygenReason.OutOfRange => "Nitrox_BuddyOxygenTooFar",
                BuddyOxygenReason.Full => "Nitrox_BuddyOxygenFull",
                _ => "Nitrox_BuddyOxygenCannot"
            };
            string name = string.IsNullOrEmpty(packet.Name) ? "?" : packet.Name;
            ErrorMessage.AddMessage(string.Format(Language.main.Get(key), name));
        }
    }

    // ---- the receiver

    /// <summary>Whether this player's tank can take air now: alive, under water, in no vehicle or base, in a game mode that needs air.</summary>
    private static bool CanReceive() => Active && GiverReady();

    /// <summary>A giver's step arrives: add as much as fits to this player's own tank and answer with how much that was.</summary>
    private static void OnDeliver(BuddyOxygenTransfer packet)
    {
        float accepted = 0f;
        BuddyOxygenReason reason = BuddyOxygenReason.None;
        if (!BuddyOxygenRules.IsValidAmount(packet.Amount) || !CanReceive())
        {
            reason = BuddyOxygenReason.CannotBreatheMore;
        }
        else
        {
            float before = Player.main.oxygenMgr.GetOxygenAvailable();
            accepted = Player.main.oxygenMgr.AddOxygen(packet.Amount);
            if (accepted < packet.Amount - 0.001f)
            {
                reason = BuddyOxygenReason.Full;
            }
            if (accepted >= BuddyOxygenRules.MIN_AMOUNT)
            {
                if (stats.DeliverFirstAt <= 0f)
                {
                    stats.DeliverFirstAt = Time.time;
                    stats.DeliverFirstBefore = before;
                }
                stats.DeliverLastAt = Time.time;
                stats.DeliverLastAfter = Player.main.oxygenMgr.GetOxygenAvailable();
            }
        }

        packetSender.Send(BuddyOxygenTransfer.Answer(packet.TransferId, accepted, reason));
        stats.Deliveries++;
        stats.Received += accepted;
        if (accepted < BuddyOxygenRules.MIN_AMOUNT)
        {
            stats.DeliveriesWithNothing++;
            return;
        }
        if (Time.time > receivingUntil)
        {
            ErrorMessage.AddMessage(string.Format(Language.main.Get("Nitrox_BuddyOxygenReceiving"), string.IsNullOrEmpty(packet.Name) ? "?" : packet.Name));
            Log.Info($"[BuddyOxygen] {packet.Name} is giving air to this player");
        }
        receivingUntil = Time.time + BuddyOxygenRules.RECEIVING_DISPLAY_SECONDS;
    }

    public static void OnPacket(BuddyOxygenTransfer packet)
    {
        switch (packet.Kind)
        {
            case BuddyOxygenKind.Deliver:
                OnDeliver(packet);
                break;
            case BuddyOxygenKind.Settle:
                OnSettle(packet);
                break;
        }
    }
}
