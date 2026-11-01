using System;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The numbers and the arithmetic of the Cyclops power share (docs/coop/VEHICLE_UPGRADES.md), free of game types so that the server, the client and the tests use the same ones. The share moves
/// energy between a Cyclops and the Seamoths and Prawn Suits near it; <see cref="PowerTransfer" /> carries it between two players when they simulate the two vehicles.
/// </summary>
public static class PowerTransferRules
{
    /// <summary>Most that one request, grant or delivery may carry. A request is a second of the share (the rate is at most 20), the cap only keeps a broken client from draining a vehicle with one packet.</summary>
    public const float MAX_AMOUNT = 100f;

    /// <summary>Smaller amounts are not worth a packet (and the battery value is sent to everybody once its whole number changes): the share waits until it adds up.</summary>
    public const float MIN_AMOUNT = 0.05f;

    /// <summary>Seconds between two requests for one pair of vehicles ("about once a second").</summary>
    public const float REQUEST_INTERVAL_SECONDS = 1f;

    /// <summary>
    /// Longest span a request may ask for. A frame hitch or a pause of a second client must not become one big request: the amount is the rate times at most this many seconds.
    /// </summary>
    public const float MAX_STEP_SECONDS = 1.5f;

    /// <summary>Seconds a client waits for the answer to a request before it asks again (a lost packet, a client that is loading or paused, a lock that moved).</summary>
    public const float ANSWER_TIMEOUT_SECONDS = 3f;

    /// <summary>Seconds the server keeps an open transfer: a grant after that is not delivered but given back to the giver.</summary>
    public const float LEDGER_EXPIRY_SECONDS = 5f;

    /// <summary>Seconds the server remembers a transfer that expired, so that a late grant is recognised and refunded once and a made-up one is ignored.</summary>
    public const float LATE_GRANT_MEMORY_SECONDS = 60f;

    /// <summary>
    /// Open transfers one player may have at a time. An honest client has one in flight per (giver, receiver) pair, so a handful; a client that asks for more is broken or hostile and is answered with nothing
    /// (it would make the giver's player take energy out over and over).
    /// </summary>
    public const int MAX_OPEN_PER_REQUESTER = 8;

    /// <summary>A Cyclops never shares with vehicles below this percent of its power (or below the emergency threshold if that is higher): a share must not leave the Cyclops without power.</summary>
    public const float FORWARD_FLOOR_PERCENT = 5f;

    /// <summary>Seconds the game's own docking charge of a vehicle stays paused after an emergency draw on it (the draw repeats about once a second).</summary>
    public const float DOCK_CHARGE_PAUSE_SECONDS = 3f;

    /// <summary>
    /// What the game's own docking charge (<c>Vehicle.UpdateEnergyRecharge</c>, once a second per docked vehicle) moves from the Cyclops into a docked vehicle in one call, as a percent of the vehicle's capacity
    /// (0.5 energy for a Seamoth's 200). Read from the game's code.
    /// </summary>
    public const float VANILLA_DOCK_CHARGE_PERCENT = 0.25f;

    /// <summary>Seconds the "Charging from Cyclops" and "Emergency power from" lines stay on after the last delivery.</summary>
    public const float DISPLAY_SECONDS = 2.5f;

    public const string CYCLOPS_TECH = "Cyclops";
    public const string SEAMOTH_TECH = "Seamoth";
    public const string PRAWN_TECH = "Exosuit";

    /// <summary>A positive amount up to the cap. NaN and infinity fail (every comparison with NaN is false).</summary>
    public static bool IsValidAmount(float amount) => amount > 0f && amount <= MAX_AMOUNT;

    /// <summary>
    /// What a grant may be: the granted amount, but never negative, never NaN, and never more than was offered (a giver cannot make energy by granting more than it was asked for).
    /// </summary>
    public static float SanitizeGrant(float granted, float offered)
    {
        if (!(granted > 0f) || !(offered > 0f))
        {
            return 0f;
        }
        return Math.Min(granted, offered);
    }

    /// <summary>
    /// The amount a receiver asks for: the rate for the time since its last request (at most <see cref="MAX_STEP_SECONDS" />), but no more than the room it has. 0 when that is less than
    /// <see cref="MIN_AMOUNT" /> (not full enough yet, or the rate is 0).
    /// </summary>
    public static float RequestAmount(float ratePerSecond, float elapsedSeconds, float room)
    {
        if (!(ratePerSecond > 0f) || !(elapsedSeconds > 0f) || !(room > 0f))
        {
            return 0f;
        }
        float amount = Math.Min(ratePerSecond * Math.Min(elapsedSeconds, MAX_STEP_SECONDS), room);
        return amount >= MIN_AMOUNT ? amount : 0f;
    }

    /// <summary>The energy a Cyclops keeps for itself when it charges vehicles: the emergency threshold, but at least <see cref="FORWARD_FLOOR_PERCENT" /> of its capacity.</summary>
    public static float ForwardFloorEnergy(float maxPower, float thresholdPercent)
    {
        return maxPower > 0f ? maxPower * Math.Max(thresholdPercent, FORWARD_FLOOR_PERCENT) / 100f : 0f;
    }

    /// <summary>What a Cyclops gives to a vehicle that asked for <paramref name="offered" />: what it has above its floor, at most the offer.</summary>
    public static float ForwardGrant(float offered, float power, float maxPower, float thresholdPercent)
    {
        float available = power - ForwardFloorEnergy(maxPower, thresholdPercent);
        return available > 0f && offered > 0f ? Math.Min(offered, available) : 0f;
    }

    /// <summary>True while the Cyclops is below its emergency threshold (strictly below; a threshold of 0 switches the emergency power off).</summary>
    public static bool EmergencyActive(float power, float maxPower, float thresholdPercent)
    {
        return thresholdPercent > 0f && maxPower > 0f && power / maxPower * 100f < thresholdPercent;
    }

    /// <summary>What one call of the game's own docking charge puts into a docked vehicle that holds <paramref name="charge" /> of <paramref name="capacity" />: a quarter of a percent of the capacity, at most what is missing.</summary>
    public static float VanillaDockCharge(float charge, float capacity)
    {
        float missing = capacity - charge;
        return capacity > 0f && missing > 0f ? Math.Min(missing, capacity * VANILLA_DOCK_CHARGE_PERCENT / 100f) : 0f;
    }

    /// <summary>
    /// Whether the game's own docking charge of a vehicle docked in a Cyclops with the Power Share waits. It waits while an emergency draw on that vehicle ran lately (<paramref name="drawLately" />, the mark that
    /// lasts <see cref="DOCK_CHARGE_PAUSE_SECONDS" />), and it waits for as long as the Cyclops is in an emergency or the charge would put it into one (<paramref name="vanillaCharge" /> out of its power would leave it
    /// below the threshold): a Cyclops that sits at its threshold must not be emptied below it by the game's charge, whose energy the emergency would then have to draw back. A draw is an event and the game's
    /// charge a tick: a mark alone leaves a gap after every three seconds through which one charge went, and the draw followed it, for ever (the second live run of the second group).
    /// </summary>
    public static bool DockChargeShouldPause(float cyclopsPower, float cyclopsMaxPower, float thresholdPercent, float vanillaCharge, bool drawLately)
    {
        if (drawLately)
        {
            return true;
        }
        return EmergencyActive(cyclopsPower - Math.Max(0f, vanillaCharge), cyclopsMaxPower, thresholdPercent);
    }

    /// <summary>The energy a docked vehicle can give without going below its reserve (a percent of its capacity).</summary>
    public static float Drawable(float charge, float capacity, float reservePercent)
    {
        float drawable = charge - capacity * reservePercent / 100f;
        return capacity > 0f && drawable > 0f ? drawable : 0f;
    }

    /// <summary>What a docked vehicle gives to a Cyclops that asked for <paramref name="offered" />.</summary>
    public static float EmergencyGrant(float offered, float charge, float capacity, float reservePercent)
    {
        float drawable = Drawable(charge, capacity, reservePercent);
        return drawable > 0f && offered > 0f ? Math.Min(offered, drawable) : 0f;
    }

    public static bool IsCyclops(string techName) => techName == CYCLOPS_TECH;

    public static bool IsVehicle(string techName) => techName == SEAMOTH_TECH || techName == PRAWN_TECH;

    /// <summary>
    /// The two ends of a transfer have to be the right kinds of vehicle for its mode: a Cyclops gives to a Seamoth or Prawn Suit (forward), a Seamoth or Prawn Suit gives to a Cyclops (emergency).
    /// </summary>
    public static bool RolesFit(PowerTransferMode mode, string giverTech, string receiverTech)
    {
        return mode switch
        {
            PowerTransferMode.Forward => IsCyclops(giverTech) && IsVehicle(receiverTech),
            PowerTransferMode.Emergency => IsVehicle(giverTech) && IsCyclops(receiverTech),
            _ => false
        };
    }
}
