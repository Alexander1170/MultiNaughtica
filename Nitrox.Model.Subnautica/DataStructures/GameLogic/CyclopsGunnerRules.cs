using System;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica.DataStructures.GameLogic;

/// <summary>
/// The numbers of the gunner station that the server also needs (docs/coop/VEHICLE_UPGRADES.md, station 2): how soon after a shot the server lets the next one through, how long it waits for the answer of the
/// player who pays, and which charges and ammo it believes. The rest of the arithmetic (the turret, the charge, the energy) is the client's (<c>GunnerRules</c>).
/// </summary>
public static class CyclopsGunnerRules
{
    /// <summary>The server lets a shot of one Cyclops through at most this often, however small the cooldown is set: a client that does not keep its own cooldown must not make the simulator pay and play a shot as often as it likes.</summary>
    public const long MIN_SERVER_INTERVAL_MILLISECONDS = 250;

    /// <summary>The server keeps this part of the cooldown (the client keeps all of it), so that a shot that is just in time for the client is never turned down for a little lag.</summary>
    public const float SERVER_COOLDOWN_FACTOR = 0.8f;

    /// <summary>How long the server waits for the player who pays to answer a forwarded shot; an answer after that is not a grant (nobody asked for it). Shorter than the three seconds that the gunner's client waits, so that the client has given up on nothing the server would still grant.</summary>
    public const long PENDING_FORWARD_MILLISECONDS = 2500;

    /// <summary>The least time between two shots of one Cyclops that the server lets through, for this cooldown (already clamped to the setting's limits).</summary>
    public static long ServerMinIntervalMilliseconds(float cooldownSeconds)
    {
        if (float.IsNaN(cooldownSeconds) || float.IsInfinity(cooldownSeconds) || cooldownSeconds <= 0f)
        {
            return MIN_SERVER_INTERVAL_MILLISECONDS;
        }
        return Math.Max(MIN_SERVER_INTERVAL_MILLISECONDS, (long)(cooldownSeconds * 1000f * SERVER_COOLDOWN_FACTOR));
    }

    /// <summary>Whether the server knows this ammo (the stasis sphere only; a second kind is added here).</summary>
    public static bool IsKnownAmmo(CyclopsGunnerAmmo ammo)
    {
        return ammo == CyclopsGunnerAmmo.StasisSphere;
    }

    /// <summary>Whether a charge is a number the server can use (0 to 1 after clamping); a charge that is no number is a modified client's.</summary>
    public static bool IsUsableCharge(float charge)
    {
        return !float.IsNaN(charge) && !float.IsInfinity(charge);
    }

    /// <summary>The charge kept inside 0 to 1.</summary>
    public static float ClampCharge(float charge)
    {
        return IsUsableCharge(charge) ? Math.Max(0f, Math.Min(1f, charge)) : 0f;
    }
}
