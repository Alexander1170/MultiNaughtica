using Nitrox.Model.Core;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.GameLogic.Gunner;

/// <summary>
/// The gunner's weapon on this client (docs/coop/VEHICLE_UPGRADES.md, station 2). It launches what the server has paid for: the stasis sphere, the same game object the Stasis Rifle shoots, from the point and the
/// direction that the gunner aimed at when they released the charge. The sphere is the game's and so is its flight and its freezing; the patches of the fork on <c>StasisSphere.Shoot</c> and <c>OnHit</c> send the
/// shot and the hit like the rifle's, so everybody sees the sphere and the frozen creatures with no packet of the gunner's own.
/// </summary>
/// <remarks>
/// <para>
/// There is <b>one sphere</b> for the gunner, made on first use and kept until the session ends, never destroyed with the console or the Cyclops: a sphere that is destroyed while its field still runs would
/// leave its targets frozen (<see cref="BulletManager.DestroySphere" /> sets them free first). Like the rifle's, the sphere holds one field at a time: a new shot ends the field of the last one, which is also what
/// the other players replay (they keep one sphere per shooter).
/// </para>
/// <para>
/// The ammo is the seam for a second kind (decoys): <see cref="Fire" /> takes a <see cref="CyclopsGunnerAmmo" /> and the packet carries it; nothing else exists yet.
/// </para>
/// </remarks>
public static class GunnerTurret
{
    private static StasisSphere sphere;
    private static float lastShotAt = float.NegativeInfinity;
    private static bool subscribed;

    /// <summary>How many shots this client has launched in this session.</summary>
    public static int Shots { get; private set; }

    /// <summary>The time (unscaled) of the last launch; negative infinity when there was none.</summary>
    public static float LastShotAt => lastShotAt;

    /// <summary>The sphere of the gunner, if it has been made (for the Debug state line).</summary>
    public static StasisSphere Sphere => sphere;

    public static void Subscribe()
    {
        if (subscribed)
        {
            return;
        }
        subscribed = true;
        Multiplayer.OnAfterMultiplayerEnd += Reset;
    }

    /// <summary>The session ended: the sphere goes (its field is ended first) and the count starts again.</summary>
    public static void Reset()
    {
        if (sphere)
        {
            BulletManager.DestroySphere(sphere);
        }
        sphere = null;
        Shots = 0;
        lastShotAt = float.NegativeInfinity;
    }

    /// <summary>Whether a sphere can be made (the game's data has the Stasis Rifle that the sphere comes from); <paramref name="why" /> says why not.</summary>
    public static bool CanLaunch(CyclopsGunnerAmmo ammo, out string why)
    {
        why = null;
        if (ammo != CyclopsGunnerAmmo.StasisSphere)
        {
            why = "that ammo does not exist";
            return false;
        }
        if (sphere)
        {
            return true;
        }
        StasisSphere made = NitroxServiceLocator.LocateService<BulletManager>().CreateLocalStasisSphere("gunner-stasis-sphere");
        if (!made)
        {
            why = "the stasis sphere of the game was not loaded";
            return false;
        }
        sphere = made;
        return true;
    }

    /// <summary>
    /// Launches the ammo from <paramref name="muzzle" /> along <paramref name="rotation" />; <paramref name="charge" /> is how far the shot was charged and <paramref name="range" /> how far it flies. False (nothing
    /// launched) when the sphere cannot be made. <paramref name="owner" /> is the Cyclops it is fired from.
    /// </summary>
    public static bool Fire(CyclopsGunnerAmmo ammo, SubRoot owner, Vector3 muzzle, Quaternion rotation, float charge, float range)
    {
        Subscribe();
        if (!CanLaunch(ammo, out string why))
        {
            Log.Warn($"[VehicleUpgrades] gunner: no shot: {why}");
            return false;
        }
        // One field per player: the rifle's own (a static sphere that this client keeps) is ended first, so that this client and the others, who replay one sphere per shooter, hold the same
        if (StasisRifle.sphere)
        {
            StasisRifle.sphere.CancelAll();
        }
        (float speed, float lifeTime) = GunnerRules.SphereFlight(range);
        // The sphere flies through the Cyclops it was fired from: its own hull has triggers (the moonpool's box, 9 by 14 by 53 m round the whole hull) that the game's sphere would explode on
        sphere.gameObject.EnsureComponent<GunnerBullet>().Owner = owner;
        sphere.Shoot(muzzle, rotation, speed, lifeTime, Mathf.Clamp01(charge));
        Shots++;
        lastShotAt = Time.unscaledTime;
        Log.Info($"[VehicleUpgrades] gunner: sphere launched, charge {charge:F2}, range {range:F0} m (speed {speed:F1} m/s for {lifeTime:F1} s), from {muzzle:F1}");
        return true;
    }
}
