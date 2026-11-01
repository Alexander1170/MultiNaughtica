#if DEBUG
using System.Globalization;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     B8 cameras, probe P1 (the design notes (not part of this repository) section 6b): can the cameras of a scanner room's docks be flown, or do they have no battery in a Nitrox session?
///     <c>cameraprobe</c> logs one line per loaded <c>MapRoomCamera</c> and a summary line, both tagged <c>[SyncHarness] cameraprobe:</c>.
/// </summary>
public partial class HarnessConsoleCommands
{
    private static string CameraProbeNumber(float value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private void LiveCommand_cameraprobe(NotificationCenter.Notification n)
    {
        try
        {
            bool requiresPower = GameModeUtils.RequiresPower();
            int count = 0;
            foreach (MapRoomCamera camera in Resources.FindObjectsOfTypeAll<MapRoomCamera>())
            {
                if (!camera || !camera.gameObject.scene.IsValid())
                {
                    continue;
                }

                count++;
                string id = camera.TryGetNitroxId(out NitroxId nitroxId) ? nitroxId.ToString() : "none";
                bool docked = camera.dockingPoint && camera.dockingPoint.cameraDocked;
                float charge = camera.energyMixin ? camera.energyMixin.charge : -1f;
                float capacity = camera.energyMixin ? camera.energyMixin.capacity : -1f;
                bool canBeControlled = camera.CanBeControlled(null);
                Vector3 position = camera.transform.position;
                string where = $"({position.x:F1},{position.y:F1},{position.z:F1})";
                Log.Info($"[SyncHarness] cameraprobe: camera {count} id={id} docked={docked} charge={CameraProbeNumber(charge)} capacity={CameraProbeNumber(capacity)} requiresPower={requiresPower} canBeControlled={canBeControlled} active={camera.isActiveAndEnabled} pos={where}");
            }
            Log.Info($"[SyncHarness] cameraprobe: {count} cameras, requiresPower={requiresPower}");
        }
        catch (System.Exception exception)
        {
            Log.Info($"[SyncHarness] cameraprobe: failed {exception.GetType().Name}: {exception.Message}");
        }
    }
}
#endif
