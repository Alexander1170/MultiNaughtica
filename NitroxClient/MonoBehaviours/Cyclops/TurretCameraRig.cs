using NitroxClient.GameLogic.Gunner;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The turret camera of the gunner station (docs/coop/VEHICLE_UPGRADES.md, station 2): while the gunner sits at the desk the player's own main camera is moved to a mount on the hull, above the conning tower, and
/// turned by the mouse inside the turret's arc. It is the real camera with all of its effects (the water, the fog), so the turret view looks like the water outside and costs nothing extra, which a second camera
/// would not; the same thing the game's own external cameras of the Cyclops do. The player's body stays at the desk in the normal mode, so everybody else keeps seeing the gunner stand there.
/// </summary>
/// <remarks>
/// The camera is <b>always put back</b>: <see cref="End" /> restores the pose that the camera root had, and runs from the console's close, from <c>OnDisable</c> and <c>OnDestroy</c> and when the camera or the
/// mount is gone. The game rewrites the head pose every frame, so the turret's pose is written after it, in <c>LateUpdate</c>, late in the order.
/// </remarks>
[DefaultExecutionOrder(32000)]
public class TurretCameraRig : MonoBehaviour
{
    private SubRoot hull;
    private Transform root;
    private Transform mount;
    private Transform pivot;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation = Quaternion.identity;
    private Vector3 cameraOffset;
    private Quaternion cameraTurn = Quaternion.identity;

    public bool Active { get; private set; }

    /// <summary>The lowest clear pitch of each sector round the mount, scanned from the hull's colliders (<see cref="GunnerRules.MinPitch(float, float[])" />); null until the console has scanned.</summary>
    public float[] Horizon { get; set; }

    /// <summary>Degrees from the bow, positive to starboard.</summary>
    public float Yaw { get; private set; }

    /// <summary>Degrees above the horizon, positive up.</summary>
    public float Pitch { get; private set; }

    public SubRoot Hull => hull;

    /// <summary>The point the turret turns about, on the mount; its forward is the aim.</summary>
    public Transform Pivot => pivot;

    /// <summary>The aim in the hull's frame.</summary>
    public Vector3 AimLocal
    {
        get
        {
            (float x, float y, float z) = GunnerRules.Direction(Yaw, Pitch);
            return new Vector3(x, y, z);
        }
    }

    /// <summary>Takes the camera to the mount. False when there is no camera to take.</summary>
    public bool Begin(SubRoot cyclops)
    {
        if (Active)
        {
            return true;
        }
        if (!cyclops || !SNCameraRoot.main || !MainCamera.camera)
        {
            return false;
        }
        hull = cyclops;
        root = SNCameraRoot.main.transform;
        Transform camera = MainCamera.camera.transform;
        savedLocalPosition = root.localPosition;
        savedLocalRotation = root.localRotation;
        // where the camera is relative to the root that is moved (identity, as far as is known): kept, so that the camera ends exactly on the pivot
        cameraOffset = root.InverseTransformPoint(camera.position);
        cameraTurn = Quaternion.Inverse(root.rotation) * camera.rotation;

        mount = new GameObject("NitroxGunnerMount").transform;
        mount.SetParent(cyclops.transform, false);
        (float x, float y, float z) = GunnerStationLayout.MountPosition;
        mount.localPosition = new Vector3(x, y, z);
        // The hull's own forward points to the stern: turned half a turn, the mount's forward is the bow and its right is starboard
        mount.localRotation = Quaternion.Euler(0f, 180f, 0f);
        pivot = new GameObject("Pivot").transform;
        pivot.SetParent(mount, false);

        Yaw = 0f;
        Pitch = 0f;
        Active = true;
        Apply();
        Log.Info($"[VehicleUpgrades] gunner: the camera is on the mount at hull-local {mount.localPosition:F2}; the camera root had local {savedLocalPosition:F2} / {savedLocalRotation.eulerAngles:F1}, the camera sits {cameraOffset:F2} from it");
        return true;
    }

    /// <summary>Turns the turret by what the mouse moved, inside the arc.</summary>
    public void Look(float dx, float dy)
    {
        (Yaw, Pitch) = GunnerRules.Step(Yaw, Pitch, dx, dy, Horizon);
    }

    public void SetAim(float yaw, float pitch)
    {
        Yaw = GunnerRules.WrapYaw(yaw);
        Pitch = GunnerRules.ClampPitch(Yaw, pitch, Horizon);
    }

    private void LateUpdate()
    {
        if (Active)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (!root || !pivot || !hull)
        {
            End();
            return;
        }
        pivot.localRotation = Quaternion.Euler(-Pitch, Yaw, 0f);
        Quaternion rotation = pivot.rotation * Quaternion.Inverse(cameraTurn);
        root.SetPositionAndRotation(pivot.position - rotation * cameraOffset, rotation);
    }

    /// <summary>Puts the camera back where it was. Safe to call at any time and more than once.</summary>
    public void End()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        // Destroy is deferred to the end of the frame: this component must not put the camera back on the tower in the LateUpdate that is still to come
        enabled = false;
        if (root)
        {
            root.localPosition = savedLocalPosition;
            root.localRotation = savedLocalRotation;
        }
        if (mount)
        {
            Destroy(mount.gameObject);
        }
        mount = null;
        pivot = null;
        Log.Info($"[VehicleUpgrades] gunner: the camera is back (root local {(root ? root.localPosition.ToString("F2") : "gone")})");
    }

    private void OnDisable()
    {
        End();
    }

    private void OnDestroy()
    {
        End();
    }
}
