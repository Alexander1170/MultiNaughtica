#if DEBUG
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     B8 cameras, battery fix (the design notes (not part of this repository) section 6b): <c>camerafix</c> reports, for every loaded <c>MapRoomCamera</c>, whether its <c>EnergyMixin</c> has a battery installed (the dock
///     cameras get a local one from <c>MapRoomCameraDocking_Start_Patch</c>), its charge and capacity, the prefab's default battery, whether the prefab has the battery-add and power-up sounds
///     (<c>soundBatteryAdd</c>, <c>soundPowerUp</c>: when both are False the held-off insert sound is unobservable) and whether the camera can be controlled. It is read-only:
///     it charges nothing and changes nothing. One line per camera and a summary line, both tagged <c>[SyncHarness] camerafix:</c>. Run it with the server's
///     <c>/console &lt;player&gt; camerafix</c>, in survival, after a scanner room was built (b8cam does that).
/// </summary>
public partial class HarnessConsoleCommands
{
    private static string CameraFixNumber(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private void LiveCommand_camerafix(NotificationCenter.Notification n)
    {
        try
        {
            bool requiresPower = GameModeUtils.RequiresPower();
            int count = 0;
            int withBattery = 0;
            int controllable = 0;
            foreach (MapRoomCamera camera in Resources.FindObjectsOfTypeAll<MapRoomCamera>())
            {
                if (!camera || !camera.gameObject.scene.IsValid())
                {
                    continue;
                }

                count++;
                EnergyMixin mixin = camera.energyMixin;
                InventoryItem stored = mixin && mixin.batterySlot != null ? mixin.batterySlot.storedItem : null;
                bool hasBattery = stored != null && stored.item;
                string batteryTech = hasBattery ? stored.item.GetTechType().ToString() : "none";
                string batteryId = "none";
                if (hasBattery && stored.item.TryGetNitroxId(out NitroxId batteryNitroxId))
                {
                    batteryId = batteryNitroxId.ToString();
                }

                string id = camera.TryGetNitroxId(out NitroxId cameraNitroxId) ? cameraNitroxId.ToString() : "none";
                bool docked = camera.dockingPoint && camera.dockingPoint.cameraDocked;
                bool canBeControlled = camera.CanBeControlled(null);
                string defaultBattery = mixin ? mixin.defaultBattery.ToString() : "n/a";
                bool defaultCompatible = mixin && mixin.compatibleBatteries != null && mixin.compatibleBatteries.Contains(mixin.defaultBattery);
                float charge = mixin ? mixin.charge : -1f;
                float capacity = mixin ? mixin.capacity : -1f;
                float defaultCharge = mixin ? mixin.defaultBatteryCharge : -1f;
                // does the camera prefab have the one-shots that EnergyMixin.OnAddItem would play for the battery (the local default battery is put in with the sounds held off)
                bool hasAddSound = mixin && mixin.soundBatteryAdd != null;
                bool hasPowerUpSound = mixin && mixin.soundPowerUp != null;
                if (hasBattery)
                {
                    withBattery++;
                }

                if (canBeControlled)
                {
                    controllable++;
                }

                Log.Info($"[SyncHarness] camerafix: camera {count} id={id} docked={docked} battery={hasBattery} batteryTech={batteryTech} batteryId={batteryId} charge={CameraFixNumber(charge)} capacity={CameraFixNumber(capacity)} defaultBattery={defaultBattery} defaultCharge={CameraFixNumber(defaultCharge)} defaultCompatible={defaultCompatible} canBeControlled={canBeControlled} requiresPower={requiresPower} soundBatteryAdd={hasAddSound} soundPowerUp={hasPowerUpSound}");
            }

            Log.Info($"[SyncHarness] camerafix: {count} cameras, withBattery={withBattery}, controllable={controllable}, requiresPower={requiresPower}");
        }
        catch (System.Exception exception)
        {
            Log.Info($"[SyncHarness] camerafix: failed {exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
