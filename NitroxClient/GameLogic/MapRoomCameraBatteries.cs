using System;
using System.Collections;
using UnityEngine;
using UWE;

namespace NitroxClient.GameLogic;

/// <summary>
/// The battery of the cameras that the docks of a scanner room make for themselves (the design notes (not part of this repository) section 6b). Those cameras are not Nitrox entities (every client makes its own two per
/// room, without ids), so the entity system never gives them a battery; the vanilla default battery (<c>EnergyMixin.OnCraftEnd</c> -> <c>SpawnDefaultAsync</c>) is blanked by
/// <c>EnergyMixin_SpawnDefaultAsync_Patch</c>. Without a battery <c>EnergyMixin.capacity</c> is 0, the dock never charges the camera and <c>MapRoomCamera.CanBeControlled</c> is false in every
/// game mode that needs power. The battery installed here is local to this client on purpose: it is not an entity, it gets no id and nothing about it is sent.
/// </summary>
public static class MapRoomCameraBatteries
{
    /// <summary>
    /// Called after <c>MapRoomCameraDocking.Start</c>. Installs the camera's default battery as a local, id-less battery: the steps of the game's <c>EnergyMixin.SetBatteryAsync</c> (what the
    /// vanilla <c>SpawnDefaultAsync</c> ends in), done here step by step (see <see cref="SpawnAsync"/> for why they are not simply called).
    /// </summary>
    public static void InstallLocalDefault(MapRoomCamera camera, bool dockWasDeserialized)
    {
        try
        {
            EnergyMixin mixin = camera ? camera.energyMixin : null;
            if (!mixin)
            {
                return;
            }

            mixin.Initialize(); // creates the battery slot when Awake has not run; does nothing when it exists
            bool hasId = camera.TryGetNitroxId(out _);
            if (!MapRoomCameraBatteryRules.ShouldInstallLocalDefault(dockWasDeserialized, hasId, mixin.HasItem(), mixin.defaultBattery != TechType.None))
            {
                return;
            }

            CoroutineHost.StartCoroutine(SpawnAsync(camera, mixin));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not give the dock camera of a scanner room its battery");
        }
    }

    /// <summary>
    /// True when the <see cref="EnergyMixin"/> belongs to a <see cref="MapRoomCamera"/> that is not a Nitrox entity (the dock's own camera): its battery is local.
    /// </summary>
    public static bool IsLocalOnlyBattery(EnergyMixin energyMixin)
    {
        if (!energyMixin)
        {
            return false;
        }

        MapRoomCamera camera = energyMixin.GetComponentInParent<MapRoomCamera>();
        bool isCamera = camera;
        return MapRoomCameraBatteryRules.IsLocalOnlyBattery(isCamera, isCamera && camera.TryGetNitroxId(out _));
    }

    /// <summary>
    /// What <c>EnergyMixin.SetBatteryAsync(defaultBattery, defaultBatteryCharge)</c> does, step by step, instead of calling it. The call cannot do three things this needs: (1) it ends in
    /// <c>RestoreBattery</c>, which sets <c>allowedToPlaySounds</c> back to true before the battery is added, so the sounds could not be held off from outside; (2) it does not look at the camera
    /// again after the prefab load (at least one frame), so a dock destroyed meanwhile got a battery added to a destroyed slot, which then sat loose at the world origin; (3) it reports nothing
    /// when it bails out (incompatible battery, no prefab, slot refuses), so the success could not be told from the failure. The game's own error messages are kept (the same text) so that
    /// the live check can look for them.
    /// </summary>
    private static IEnumerator SpawnAsync(MapRoomCamera camera, EnergyMixin mixin)
    {
        // read now: after the awaits the dock can be gone, and a destroyed object has no name
        string cameraName = mixin.gameObject.name;
        TechType batteryTech = mixin.defaultBattery;
        float normalizedCharge = mixin.defaultBatteryCharge;
        if (normalizedCharge < 0f) // as the game: no charge given means full
        {
            normalizedCharge = 1f;
        }

        mixin.Initialize();
        mixin.RestoreBattery();
        if (mixin.HasItem())
        {
            Log.Debug($"[MapRoomCamera] dock camera {cameraName}: already has a battery, no default battery installed");
            yield break;
        }

        if (mixin.compatibleBatteries == null || !mixin.compatibleBatteries.Contains(batteryTech))
        {
            Log.Error($"[MapRoomCamera] dock camera {cameraName}: Incompatible battery TechType.{batteryTech}, no battery was installed");
            yield break;
        }

        TaskResult<GameObject> spawned = new();
        yield return mixin.SpawnBatteryAsync(batteryTech, Mathf.Clamp01(normalizedCharge), spawned); // the game logs its own error when the prefab is missing
        GameObject battery = spawned.Get();
        if (!battery)
        {
            Log.Error($"[MapRoomCamera] dock camera {cameraName}: Failed to spawn battery GameObject for TechType.{batteryTech}, no battery was installed (see the game error before this line)");
            yield break;
        }

        // the prefab load took at least one frame: the dock, the slot and the camera's id are looked at again before anything is added
        bool cameraAlive = camera && mixin;
        bool slotExists = cameraAlive && mixin.batterySlot != null;
        bool slotHasBattery = slotExists && mixin.HasItem();
        bool hasId = cameraAlive && camera.TryGetNitroxId(out _);
        if (!MapRoomCameraBatteryRules.CanAddSpawnedBattery(cameraAlive, slotExists, slotHasBattery, hasId))
        {
            UnityEngine.Object.Destroy(battery);
            Log.Debug($"[MapRoomCamera] dock camera {cameraName}: no default battery installed, the camera is gone={!cameraAlive}, the slot already has a battery={slotHasBattery}, the camera became an entity={hasId}");
            yield break;
        }

        Pickupable pickupable = battery.GetComponent<Pickupable>();
        if (!pickupable)
        {
            UnityEngine.Object.Destroy(battery);
            Log.Error($"[MapRoomCamera] dock camera {cameraName}: No Pickupable component found on spawned Battery for TechType.{batteryTech}, no battery was installed");
            yield break;
        }

        // as the vanilla EnergyMixin.SpawnDefaultAsync: the sounds are off while a battery is put in that nobody put in. The flag has to be held across the AddItem itself, because
        // EnergyMixin.OnAddItem plays the battery and power-up one-shots from inside it (and RestoreBattery above left the flag at true).
        InventoryItem installed;
        mixin.allowedToPlaySounds = false;
        try
        {
            installed = mixin.batterySlot.AddItem(pickupable);
        }
        finally
        {
            mixin.allowedToPlaySounds = true;
        }

        if (installed == null)
        {
            UnityEngine.Object.Destroy(battery);
            Log.Error($"[MapRoomCamera] dock camera {cameraName}: Failed to add battery to EnergyMixin batterySlot, no battery was installed");
            yield break;
        }

        Log.Debug($"[MapRoomCamera] dock camera {cameraName}: local default battery {batteryTech} installed, charge {mixin.charge:F1}/{mixin.capacity:F1}");
    }
}
