using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.PlayerLogic;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Vehicles;

/// <summary>
/// On every Seamoth, on every client: builds the rider bar on the hull while the Seamoth has the module (and takes it down when it does not), and puts the body of a remote rider on it
/// (<see cref="RiderBars" />). The bar is a rod across the rear of the hull with two short posts, built at run time from the hull's own material: the game's meshes cannot be read offline, and the
/// state of the ride lives in <see cref="RiderBars" />, so this component only shows it. It looks again once a second, because the module can be installed before the Seamoth is set up, and a ride
/// can be announced before the bar exists.
/// </summary>
public class SeamothRiderBar : MonoBehaviour
{
    private const float REFRESH_SECONDS = 1f;

    private GameObject barRoot;
    private float nextRefresh;
    private Vector3 userOffset;
    private NitroxId vehicleId;
    private RemotePlayer remoteRider;

    public Vehicle Vehicle { get; private set; }
    public Transform Anchor { get; private set; }
    public Transform LeftHand { get; private set; }
    public Transform RightHand { get; private set; }

    public bool IsBuilt => barRoot && Anchor;

    public NitroxId VehicleId
    {
        get
        {
            if (vehicleId == null && Vehicle)
            {
                Vehicle.TryGetNitroxId(out vehicleId);
            }
            return vehicleId;
        }
    }

    /// <summary>Moves the bar and the rider by an offset in the Seamoth's space (the live check's <c>vubar</c> nudges it until it looks right); rebuilds the bar.</summary>
    public void Nudge(Vector3 offset)
    {
        if (RiderBars.IsLocalRiding && RiderBars.LocalBar == this)
        {
            Log.Warn("[VehicleUpgrades] rider bar: let go of the bar before nudging it (the rider hangs on the anchor that a nudge takes down)");
            return;
        }
        userOffset = offset;
        Teardown();
        Refresh();
    }

    private void Awake()
    {
        Vehicle = GetComponent<Vehicle>();
        RiderBars.Subscribe();
    }

    private void Update()
    {
        if (Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + REFRESH_SECONDS;
            Refresh();
        }
    }

    private void OnDestroy()
    {
        Teardown();
    }

    /// <summary>Brings the bar and the remote rider in line with the module and with what the server said.</summary>
    public void Refresh()
    {
        bool want = RiderBars.IsBarInstalled(Vehicle);
        if (want && !IsBuilt)
        {
            Build();
        }
        else if (!want && barRoot)
        {
            Teardown();
        }
        ReconcileRemoteRider();
    }

    private void Build()
    {
        if (!HullBounds.TryGet(transform, out Bounds hull))
        {
            return;
        }

        (float rodX, float rodY, float rodZ) = RiderBarLayout.RodCenter(hull.center.x, hull.center.y, hull.center.z, hull.extents.y, hull.extents.z);
        (float riderX, float riderY, float riderZ) = RiderBarLayout.RiderRoot(hull.center.x, hull.center.y, hull.center.z, hull.extents.y, hull.extents.z);
        float halfLength = RiderBarLayout.RodHalfLength(hull.extents.x);
        Material material = FindHullMaterial();

        barRoot = new GameObject("CoopRiderBar");
        barRoot.transform.SetParent(transform, false);
        barRoot.transform.localPosition = new Vector3(rodX, rodY, rodZ) + userOffset;
        barRoot.transform.localRotation = Quaternion.identity;

        List<Renderer> renderers = [];
        renderers.Add(AddPart("Rod", PrimitiveType.Capsule, Vector3.zero, new Vector3(0f, 0f, 90f), new Vector3(0.06f, halfLength, 0.06f), material));
        renderers.Add(AddPart("PostLeft", PrimitiveType.Capsule, new Vector3(-halfLength, -0.12f, 0.05f), new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.14f, 0.05f), material));
        renderers.Add(AddPart("PostRight", PrimitiveType.Capsule, new Vector3(halfLength, -0.12f, 0.05f), new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.14f, 0.05f), material));

        // The light of the water and the sky reaches the new renderers through the game's sky applier, like it does for the copies of the other players
        SkyApplier skyApplier = barRoot.AddComponent<SkyApplier>();
        skyApplier.anchorSky = Skies.Auto;
        skyApplier.emissiveFromPower = false;
        skyApplier.dynamic = true;
        skyApplier.renderers = renderers.ToArray();

        // The trigger the player's hand finds: much bigger than the rod (the use distance is 2 m; a play-test found the thin bar fiddly to aim at), on the layer the game's hand looks at, a trigger so that
        // nothing collides with it. It grows to the outside: its middle is moved away from the hull, so that it reaches no farther into the hull than before (the Seamoth's own hand target has to work there)
        GameObject handTarget = new("HandTarget") { layer = LayerID.Useable };
        handTarget.transform.SetParent(barRoot.transform, false);
        CapsuleCollider trigger = handTarget.AddComponent<CapsuleCollider>();
        trigger.isTrigger = true;
        trigger.direction = 0;
        trigger.radius = RiderBarLayout.GRAB_RADIUS;
        trigger.height = RiderBarLayout.GrabHeight(halfLength);
        trigger.center = new Vector3(0f, 0f, -RiderBarLayout.GRAB_CENTRE_BEHIND);
        handTarget.AddComponent<RiderBarHandTarget>().Bar = this;

        // Where the rider's root hangs (behind the rod) and the two places for the hands on the rod
        Anchor = new GameObject("RiderAnchor").transform;
        Anchor.SetParent(transform, false);
        Anchor.localPosition = new Vector3(riderX, riderY, riderZ) + userOffset;
        Anchor.localRotation = Quaternion.identity;
        LeftHand = new GameObject("LeftHand").transform;
        LeftHand.SetParent(barRoot.transform, false);
        LeftHand.localPosition = new Vector3(-halfLength * 0.5f, 0f, 0f);
        RightHand = new GameObject("RightHand").transform;
        RightHand.SetParent(barRoot.transform, false);
        RightHand.localPosition = new Vector3(halfLength * 0.5f, 0f, 0f);

        Log.Info($"[VehicleUpgrades] rider bar: built on {name}, rod at {barRoot.transform.localPosition} (local), rider root at {Anchor.localPosition}, half length {halfLength:F2}, material {(material ? material.name : "none")}");
    }

    private Renderer AddPart(string partName, PrimitiveType type, Vector3 localPosition, Vector3 localEuler, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        Collider collider = part.GetComponent<Collider>();
        if (collider)
        {
            Destroy(collider);
        }
        part.transform.SetParent(barRoot.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localEulerAngles = localEuler;
        part.transform.localScale = scale;
        Renderer renderer = part.GetComponent<Renderer>();
        if (material)
        {
            renderer.sharedMaterial = material;
        }
        return renderer;
    }

    /// <summary>The hull's own material: the one of the biggest mesh under the Seamoth, so that the bar looks like part of it (the game's materials cannot be read offline).</summary>
    private Material FindHullMaterial()
    {
        Material best = null;
        float bestSize = 0f;
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
        {
            // Not the bar itself, not a remote player's body, and not a transparent one (the glass of the canopy, the cones of the lights): the rod would be nearly invisible
            if ((barRoot && renderer.transform.IsChildOf(barRoot.transform)) || !renderer.sharedMaterial || renderer.sharedMaterial.renderQueue >= 3000 || renderer.GetComponentInParent<RemotePlayerIdentifier>())
            {
                continue;
            }
            float size = renderer.bounds.size.sqrMagnitude;
            if (size > bestSize)
            {
                bestSize = size;
                best = renderer.sharedMaterial;
            }
        }
        return best;
    }

    private void Teardown()
    {
        // The local rider is a child of the anchor that is destroyed below: it lets go first, whatever took the bar down (a nudge, the module, the Seamoth itself)
        if (RiderBars.IsLocalRiding && RiderBars.LocalBar == this)
        {
            RiderBars.ReleaseLocal(RiderReleaseReason.ModuleRemoved, true);
        }
        else if (Anchor && Player.main && Player.main.transform.IsChildOf(Anchor))
        {
            Player.main.transform.parent = null;
        }
        if (remoteRider != null)
        {
            remoteRider.SetBarRider(null);
            remoteRider = null;
        }
        if (barRoot)
        {
            Destroy(barRoot);
        }
        if (Anchor)
        {
            Destroy(Anchor.gameObject);
        }
        barRoot = null;
        Anchor = null;
        LeftHand = null;
        RightHand = null;
    }

    /// <summary>The body of the remote rider the server named is on this bar; any other body is off it.</summary>
    private void ReconcileRemoteRider()
    {
        RemotePlayer wanted = null;
        if (IsBuilt && VehicleId != null && RiderBars.TryGetRider(VehicleId, out SessionId session) && NitroxServiceLocator.LocateService<PlayerManager>().TryFind(session, out RemotePlayer found) && found.Body)
        {
            wanted = found;
        }

        if (remoteRider != null && remoteRider != wanted)
        {
            remoteRider.SetBarRider(null);
            remoteRider = null;
        }
        if (wanted != null && remoteRider != wanted)
        {
            wanted.SetBarRider(this);
            remoteRider = wanted;
        }
    }
}
