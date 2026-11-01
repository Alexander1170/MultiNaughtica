using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.Spawning.WorldEntities;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// What the game's own terrain hologram (<c>MiniWorld</c>) needs and that only a prefab of the game has: its material and the numbers of the one the Seaglide's map uses. Taken once per session from the
/// Seaglide's prefab (<c>VehicleInterface_MapController.interfacePrefab</c>), with the first <c>MiniWorld</c> that is loaded as the fallback.
/// </summary>
public sealed class TerrainHologramTemplate
{
    private static TerrainHologramTemplate cached;

    private TerrainHologramTemplate(Material material, float fadeSharpness, float fadeRadius, float hologramRadius, int mapWorldRadius, string source)
    {
        Material = material;
        FadeSharpness = fadeSharpness;
        FadeRadius = fadeRadius;
        HologramRadius = hologramRadius;
        MapWorldRadius = mapWorldRadius;
        Source = source;
    }

    public Material Material { get; }
    public float FadeSharpness { get; }
    public float FadeRadius { get; }
    public float HologramRadius { get; }
    public int MapWorldRadius { get; }

    /// <summary>Where the template came from (for the log).</summary>
    public string Source { get; }

    public static bool TryGetCached(out TerrainHologramTemplate template)
    {
        template = cached;
        return template != null && template.Material;
    }

    /// <summary>
    /// Finds the template: the Seaglide's prefab is requested the way the game spawns it, its map interface holds a <c>MiniWorld</c> with the material. Calls <paramref name="done" /> with null when none was found
    /// (the caller tries again later).
    /// </summary>
    public static IEnumerator Request(Action<TerrainHologramTemplate> done)
    {
        if (TryGetCached(out TerrainHologramTemplate known))
        {
            done(known);
            yield break;
        }
        TaskResult<GameObject> prefab = new();
        yield return DefaultWorldEntitySpawner.RequestPrefab(TechType.Seaglide, prefab);
        TerrainHologramTemplate template = null;
        try
        {
            template = FromSeaglide(prefab.value);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "[VehicleUpgrades] terrain sonar: the Seaglide's map could not be read");
        }
        if (template == null)
        {
            template = FromLoadedMiniWorld();
        }
        cached = template;
        done(template);
    }

    private static TerrainHologramTemplate FromSeaglide(GameObject seaglide)
    {
        if (!seaglide)
        {
            return null;
        }
        VehicleInterface_MapController controller = seaglide.GetComponentInChildren<VehicleInterface_MapController>(true);
        if (!controller || !controller.interfacePrefab)
        {
            Log.Warn("[VehicleUpgrades] terrain sonar: the Seaglide's prefab has no map interface");
            return null;
        }
        MiniWorld mini = controller.interfacePrefab.GetComponentInChildren<MiniWorld>(true);
        return mini && mini.hologramMaterial ? From(mini, "the Seaglide's map") : null;
    }

    private static TerrainHologramTemplate FromLoadedMiniWorld()
    {
        foreach (MiniWorld mini in Resources.FindObjectsOfTypeAll<MiniWorld>())
        {
            if (mini && mini.hologramMaterial)
            {
                return From(mini, $"a loaded MiniWorld ({mini.name})");
            }
        }
        Log.Warn("[VehicleUpgrades] terrain sonar: no terrain hologram of the game could be found to copy its material from");
        return null;
    }

    private static TerrainHologramTemplate From(MiniWorld mini, string source)
    {
        Log.Info(string.Concat($"[VehicleUpgrades] terrain sonar: template from {source}: hologramRadius={mini.hologramRadius:F3} mapWorldRadius={mini.mapWorldRadius} fadeRadius={mini.fadeRadius:F3} fadeSharpness={mini.fadeSharpness:F2} ",
                 $"shader={(mini.hologramMaterial.shader ? mini.hologramMaterial.shader.name : "none")} holderScale={(mini.hologramHolder ? mini.hologramHolder.lossyScale.x : 0f):F4}"));
        return new TerrainHologramTemplate(mini.hologramMaterial, mini.fadeSharpness, mini.fadeRadius, mini.hologramRadius, mini.mapWorldRadius, source);
    }
}

/// <summary>
/// The game's terrain hologram (<c>MiniWorld</c>) on the round sonar display at the helm of a Cyclops, sized by <see cref="TerrainSonarRules" /> so that a real metre takes as much of the display as it does for
/// the pings of the sonar. It is built once for a range and shown while the local player is aboard.
/// <para>
/// The game's hologram loads the terrain around its own position and the game's shader fades it by the distance from that position, so it sits at the middle of the display; the display shows the terrain around
/// the pivot of the hull (the pings do), which is some metres behind the helm, so <see cref="TerrainHologramAligner" /> moves and turns the terrain under the display's middle to match the pings.
/// </para>
/// </summary>
public sealed class TerrainHologram
{
    private const string HOST_NAME = "NitroxTerrainHologram";
    private const string HOLDER_NAME = "NitroxTerrainHologramHolder";

    private readonly GameObject host;
    private readonly Transform holder;
    private readonly MiniWorld mini;
    private readonly TerrainHologramAligner aligner;
    private readonly float fadeRadiusAtBuild;
    private float appliedSizeFactor = 1f;

    private TerrainHologram(GameObject host, Transform holder, MiniWorld mini, TerrainHologramAligner aligner, float range, float requestedRange, float unitsPerMetre, int loadRadius)
    {
        this.host = host;
        this.holder = holder;
        this.mini = mini;
        this.aligner = aligner;
        Range = range;
        RequestedRange = requestedRange;
        UnitsPerMetre = unitsPerMetre;
        LoadRadius = loadRadius;
        fadeRadiusAtBuild = mini.fadeRadius;
    }

    /// <summary>The range in metres the hologram was built for (at the helm: the setting, but not more than the display shows, <see cref="TerrainSonarRules.HelmRange" />).</summary>
    public float Range { get; }

    /// <summary>The range that was asked for, before the display's own limit: what a rebuild is decided against.</summary>
    public float RequestedRange { get; }

    public float UnitsPerMetre { get; }
    public int LoadRadius { get; }
    public bool Exists => host;
    public bool Shown => host && host.activeSelf;
    public MiniWorld MiniWorld => mini;
    public Transform Holder => holder;
    public TerrainHologramAligner Aligner => aligner;

    /// <summary>
    /// The radius in metres, in the world, of the disc of terrain as it is drawn now: the radius it was faded at when it was built, times how much the anchor has been scaled since (the player's size control of
    /// the navigator's hologram, <see cref="TerrainHologramAligner.SizeFactor" />). 0 when the hologram is gone.
    /// </summary>
    public float DiscRadiusWorld => host && aligner ? fadeRadiusAtBuild * aligner.SizeFactor : 0f;

    /// <summary>
    /// The game's shader fades the terrain at a distance in world units that the hologram was built with (<c>_FadeRadius</c>, set once). When the anchor is scaled afterwards (the size control) the terrain
    /// and the icons scale with it by themselves, and this moves the fade with them. Cheap when nothing changed.
    /// </summary>
    public void ApplyAnchorScale()
    {
        if (!host || !aligner || !mini || !mini.materialInstance)
        {
            return;
        }
        float factor = aligner.SizeFactor;
        if (Mathf.Approximately(factor, appliedSizeFactor))
        {
            return;
        }
        appliedSizeFactor = factor;
        mini.materialInstance.SetFloat(Shader.PropertyToID("_FadeRadius"), fadeRadiusAtBuild * factor);
    }

    /// <summary>
    /// Builds the hologram on the sonar display of <paramref name="cyclops" />, or returns null (after saying why in the log) when the display is not there. The game's hologram freezes its scale when it
    /// starts, so everything is set while the host is still switched off.
    /// </summary>
    public static TerrainHologram TryCreate(SubRoot cyclops, float range, TerrainHologramTemplate template, bool clampToPad = true)
    {
        try
        {
            CyclopsSonarDisplay display = cyclops.GetComponentInChildren<CyclopsSonarDisplay>(true);
            if (!display)
            {
                Log.Warn($"[VehicleUpgrades] terrain sonar: {cyclops.name} has no sonar display at its helm; the cockpit is: {DescribeHelm(cyclops)}");
                return null;
            }

            float unitsPerMetre = ReadUnitsPerMetre(display, out string how, out float scanRadius);
            Transform pivot = display.subTransform ? display.subTransform : cyclops.transform;
            float effective = clampToPad ? TerrainSonarRules.HelmRange(range, scanRadius) : range;
            return Build(cyclops, display.transform, pivot, HOST_NAME, unitsPerMetre, how, effective, range, template,
                         $"display={display.GetFullHierarchyPath()} underSonarUpgrade={IsUnderSonarUpgrade(cyclops, display)} requested={range:F0} m scanRadius={scanRadius:F0} m");
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] terrain sonar: the hologram of {cyclops.name} could not be built");
            return null;
        }
    }

    /// <summary>
    /// Builds a hologram of the same kind at <paramref name="anchor" />, which has to be a child of the Cyclops, drawing <paramref name="unitsPerMetre" /> world units per real metre: the map of the navigator
    /// console. The terrain around the Cyclops' pivot is drawn around the anchor's middle (the aligner turns it with <see cref="TerrainHologramAligner.View" /> and scales it with its zoom).
    /// </summary>
    public static TerrainHologram TryCreateOn(SubRoot cyclops, Transform anchor, string hostName, float unitsPerMetre, float range, TerrainHologramTemplate template)
    {
        try
        {
            return Build(cyclops, anchor, cyclops.transform, hostName, unitsPerMetre, "the console map", range, range, template, $"anchor={anchor.GetFullHierarchyPath()}");
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"[VehicleUpgrades] terrain sonar: the map of {cyclops.name} could not be built");
            return null;
        }
    }

    private static TerrainHologram Build(SubRoot cyclops, Transform anchor, Transform pivot, string hostName, float unitsPerMetre, string how, float range, float requestedRange, TerrainHologramTemplate template, string where)
    {
        float anchorScale = anchor.lossyScale.x;
        float worldUnitsPerMetre = TerrainSonarRules.WorldUnitsPerMetre(unitsPerMetre, anchorScale);
        float pivotToAnchor = Vector3.Distance(anchor.position, pivot.position);
        int loadRadius = TerrainSonarRules.LoadRadius(range, pivotToAnchor);

        GameObject host = new(hostName);
        host.SetActive(false);
        host.transform.SetParent(anchor, false);
        GameObject holderObject = new(HOLDER_NAME);
        holderObject.transform.SetParent(host.transform, false);

        MiniWorld mini = host.AddComponent<MiniWorld>();
        mini.hologramHolder = holderObject.transform;
        mini.hologramMaterial = template.Material;
        mini.mapWorldRadius = loadRadius;
        mini.hologramRadius = TerrainSonarRules.HologramRadius(unitsPerMetre, loadRadius);
        mini.fadeRadius = TerrainSonarRules.FadeRadius(range, unitsPerMetre, anchorScale);
        mini.fadeSharpness = template.FadeSharpness;
        mini.active = true;
        mini.updatePosition = true;

        TerrainHologramAligner aligner = host.AddComponent<TerrainHologramAligner>();
        aligner.Setup(anchor, pivot, holderObject.transform, worldUnitsPerMetre);

        host.SetActive(true);
        Log.Info(string.Concat($"[VehicleUpgrades] terrain sonar: hologram {hostName} built on {cyclops.name}: {where} unitsPerMetre={unitsPerMetre:F5} ({how}) anchorScale={anchorScale:F4} ",
                 $"pivotToAnchor={pivotToAnchor:F1} m range={range:F0} m loadRadius={loadRadius} hologramRadius={mini.hologramRadius:F4} fadeRadius={mini.fadeRadius:F4} sharpness={mini.fadeSharpness:F2} template={template.Source}"));
        return new TerrainHologram(host, holderObject.transform, mini, aligner, range, requestedRange, unitsPerMetre, loadRadius);
    }

    /// <summary>Shows or hides the hologram. Hiding switches the host off, which lets the game free the terrain chunks it loaded within a second or two.</summary>
    public void SetShown(bool shown)
    {
        if (host && host.activeSelf != shown)
        {
            host.SetActive(shown);
        }
    }

    public void Destroy()
    {
        meshCache.Clear();
        if (host)
        {
            UnityEngine.Object.Destroy(host);
        }
    }

    private sealed class MeshData
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public Bounds Bounds;
    }

    private readonly Dictionary<Mesh, MeshData> meshCache = new();

    /// <summary>How much of the fade radius a pick counts as drawn (the shader fades the terrain out towards its edge).</summary>
    private const float VISIBLE_FRACTION = 0.98f;

    /// <summary>The name of the chunk mesh that the last <see cref="TryRaycastRelief" /> hit ("Chunk-14-23-20"), or null when it hit nothing.</summary>
    public string LastPickChunk { get; private set; }

    /// <summary>
    /// Whether a real place agrees with the chunk that a pick hit: the chunk's name is the cell of the world's terrain it covers (128 m cells of the Voxeland's own frame, <see cref="HologramMath.ChunkOf" />), so a pick whose world
    /// place lies in another cell was taken back to the world wrongly. A hit within 3 m of a cell's edge is not judged (the chunk meshes reach a little over their edges). A line for the log of every pick.
    /// </summary>
    public string CheckPickedChunk(Vector3 world)
    {
        LargeWorldStreamer streamer = LargeWorldStreamer.main;
        if (string.IsNullOrEmpty(LastPickChunk) || !streamer || !streamer.land)
        {
            return "chunk check: not possible";
        }
        if (!HologramMath.TryParseChunkName(LastPickChunk, out (int X, int Y, int Z) named))
        {
            return $"chunk check: cannot read the name {LastPickChunk}";
        }
        Vector3 landOrigin = streamer.land.transform.position;
        (int X, int Y, int Z) found = HologramMath.ChunkOf((world.x, world.y, world.z), (landOrigin.x, landOrigin.y, landOrigin.z), out float edgeDistance);
        if (found == named)
        {
            return "chunk check: ok";
        }
        return edgeDistance < 3f ? $"chunk check: next to the edge of {found} ({edgeDistance:F1} m)" : $"chunk check: MISMATCH, the world place is in chunk {found}, the mesh was {named}";
    }

    /// <summary>What the last <see cref="TryRaycastRelief" /> looked at and found (chunks, triangles, milliseconds, the chunk it hit): one line of every pick's log entry.</summary>
    public string LastPickReport { get; private set; } = string.Empty;

    /// <summary>
    /// Where a ray (from the camera, through the cursor) meets the terrain that the hologram draws, as a point of the world's space as it is drawn: the first of the triangles of the terrain chunks that the ray
    /// crosses (the chunks are meshes that the game loads readable, so their triangles can be tested on the CPU; the ray is taken into each chunk's own space and the nearest hit wins). Bring the point to the real world,
    /// depth included, with <see cref="TerrainHologramAligner.MapToWorld" />. False, with the reason, when the ray crosses no terrain or the meshes cannot be read.
    /// </summary>
    public bool TryRaycastRelief(Ray ray, out Vector3 hit, out string why)
    {
        hit = default;
        why = null;
        if (!host || !holder)
        {
            why = "the hologram is gone";
            return false;
        }
        Stopwatch watch = Stopwatch.StartNew();
        // the terrain as it is drawn this frame, not as the game's Update left it (turned to the identity)
        if (aligner)
        {
            aligner.SyncHolder();
        }
        // The shader draws terrain inside a sphere only (the fade radius, round the middle of the map), but the chunks are meshes that reach well beyond it, and their triangles out there are not drawn and are
        // still there for a ray to meet, in front of what the player sees: a pick that takes the first triangle of the chunks puts a mark in empty water. Only the part of the ray inside the sphere counts.
        Vector3 middle = aligner && aligner.Anchor ? aligner.Anchor.position : holder.position;
        if (!HologramMath.RaySphere((ray.origin.x, ray.origin.y, ray.origin.z), (ray.direction.x, ray.direction.y, ray.direction.z), (middle.x, middle.y, middle.z), DiscRadiusWorld * VISIBLE_FRACTION, out float sphereIn, out float sphereOut))
        {
            LastPickReport = "the ray passes beside the volume of the hologram";
            why = "the ray passes beside the volume of the hologram";
            return false;
        }
        float best = float.MaxValue;
        string bestChunk = null;
        int chunks = 0;
        int unreadable = 0;
        long triangles = 0;
        foreach (MeshFilter filter in holder.GetComponentsInChildren<MeshFilter>(false))
        {
            Mesh mesh = filter.sharedMesh;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!mesh || !renderer || !renderer.enabled)
            {
                continue;
            }
            if (!mesh.isReadable)
            {
                unreadable++;
                continue;
            }
            chunks++;
            if (!meshCache.TryGetValue(mesh, out MeshData data))
            {
                if (meshCache.Count > 96)
                {
                    meshCache.Clear();
                }
                data = new MeshData { Vertices = mesh.vertices, Triangles = mesh.triangles, Bounds = mesh.bounds };
                meshCache[mesh] = data;
            }
            // the ray in the chunk's own space (the direction keeps the scale, so the distance along it is the same number in every chunk and in the world)
            Matrix4x4 worldToLocal = filter.transform.worldToLocalMatrix;
            Vector3 origin = worldToLocal.MultiplyPoint3x4(ray.origin);
            Vector3 direction = worldToLocal.MultiplyVector(ray.direction);
            (float, float, float) o = (origin.x, origin.y, origin.z);
            (float, float, float) d = (direction.x, direction.y, direction.z);
            Vector3 min = data.Bounds.min;
            Vector3 max = data.Bounds.max;
            if (!HologramMath.RayBox(o, d, (min.x, min.y, min.z), (max.x, max.y, max.z), out float entered) || entered > best || entered > sphereOut)
            {
                continue;
            }
            int[] indices = data.Triangles;
            Vector3[] vertices = data.Vertices;
            triangles += indices.Length / 3;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];
                if (HologramMath.RayTriangle(o, d, (a.x, a.y, a.z), (b.x, b.y, b.z), (c.x, c.y, c.z), out float t) && t < best && t >= sphereIn && t <= sphereOut)
                {
                    best = t;
                    bestChunk = mesh.name;
                }
            }
        }
        LastPickChunk = best < float.MaxValue ? bestChunk : null;
        LastPickReport = $"{chunks} chunks ({unreadable} not readable), {triangles} triangles, {watch.Elapsed.TotalMilliseconds:F1} ms, {(best < float.MaxValue ? $"hit in {bestChunk}" : "no hit")}";
        if (best >= float.MaxValue)
        {
            why = chunks == 0 ? (unreadable > 0 ? "the terrain meshes of the hologram cannot be read" : "the hologram has no terrain loaded yet") : "the ray crosses no terrain";
            return false;
        }
        hit = ray.origin + ray.direction * best;
        return true;
    }

    /// <summary>How many terrain chunks the game's hologram has loaded and is drawing now (the live check waits for it to settle before it measures the frame time). Allocates: not for every frame.</summary>
    public int ChunkCount
    {
        get
        {
            if (!host || !holder)
            {
                return 0;
            }
            int count = 0;
            foreach (MeshRenderer renderer in holder.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (renderer && renderer.enabled)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>A line for the log and the live check: what the game's hologram has loaded, and how big it came out.</summary>
    public string Measure()
    {
        if (!host || !holder)
        {
            return "gone";
        }
        MeshRenderer[] renderers = holder.GetComponentsInChildren<MeshRenderer>(false);
        Bounds? all = null;
        float largestMesh = 0f;
        foreach (MeshRenderer renderer in renderers)
        {
            if (!renderer || !renderer.enabled)
            {
                continue;
            }
            all = all.HasValue ? Encapsulate(all.Value, renderer.bounds) : renderer.bounds;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh)
            {
                Vector3 size = filter.sharedMesh.bounds.size;
                largestMesh = Mathf.Max(largestMesh, Mathf.Max(size.x, size.z));
            }
        }
        string extent = all.HasValue ? $"{all.Value.size.x:F3}x{all.Value.size.y:F3}x{all.Value.size.z:F3}" : "none";
        return string.Concat($"chunks={renderers.Length} shown={host.activeSelf} activeInHierarchy={host.activeInHierarchy} worldBounds={extent} (display units per metre {UnitsPerMetre:F5}, so {Range:F0} m should be a radius of {Range * UnitsPerMetre * host.transform.lossyScale.x:F3}) ",
               $"largestMeshExtent={largestMesh:F1} mesh units (about {TerrainSonarRules.METRES_PER_MESH_UNIT * 32f:F0} expected per 128 m chunk when a mesh unit is {TerrainSonarRules.METRES_PER_MESH_UNIT:F0} m) holderScale={holder.lossyScale.x:F4} scaleFactor={aligner.UserScale:F3}");
    }

    private static Bounds Encapsulate(Bounds bounds, Bounds other)
    {
        bounds.Encapsulate(other);
        return bounds;
    }

    private static float ReadUnitsPerMetre(CyclopsSonarDisplay display, out string how, out float scanRadius)
    {
        CyclopsHUDSonarPing ping = display.sonarPingObject ? display.sonarPingObject.GetComponent<CyclopsHUDSonarPing>() : null;
        scanRadius = ping ? ping.radarScanSize : 0f;
        float units = ping ? TerrainSonarRules.UnitsPerMetre(ping.radarDisplaySize, ping.radarScanSize) : 0f;
        if (units > 0f)
        {
            how = $"radarDisplaySize {ping.radarDisplaySize:F4} over radarScanSize {ping.radarScanSize:F2}";
            return units;
        }
        how = "FALLBACK: the sonar ping's sizes could not be read";
        return TerrainSonarRules.FALLBACK_UNITS_PER_METRE;
    }

    private static bool IsUnderSonarUpgrade(SubRoot cyclops, CyclopsSonarDisplay display)
    {
        CyclopsHelmHUDManager helm = cyclops.GetComponentInChildren<CyclopsHelmHUDManager>(true);
        return helm && helm.sonarUpgrade && display.transform.IsChildOf(helm.sonarUpgrade.transform);
    }

    /// <summary>The helm of a Cyclops as a list of the components that matter, for the log when the display could not be found.</summary>
    public static string DescribeHelm(SubRoot cyclops)
    {
        StringBuilder text = new();
        foreach (Type type in new[] { typeof(CyclopsHelmHUDManager), typeof(CyclopsHolographicHUD), typeof(CyclopsSonarDisplay), typeof(PilotingChair), typeof(CyclopsLightingPanel) })
        {
            foreach (Component component in cyclops.GetComponentsInChildren(type, true))
            {
                text.Append(type.Name).Append('@').Append(component.GetFullHierarchyPath()).Append("; ");
            }
        }
        return text.ToString();
    }
}


/// <summary>
/// Puts the terrain of <see cref="TerrainHologram" /> where the pings of the sonar display are: the game keeps its hologram level to the world, which is right for a map that stands still but not for a display
/// that turns with the hull, so after the game has updated it the holder is turned to the anchor's turn relative to the hull and moved by the anchor's distance from the pivot, scaled. The same math gives
/// <see cref="WorldToMap" />, the place of any real position on the hologram, which the icons of the navigator console use.
/// </summary>
public sealed class TerrainHologramAligner : MonoBehaviour
{
    private Transform anchor;
    private Transform pivot;
    private Transform holder;
    private float worldUnitsPerMetre;
    private float builtAnchorScale = 1f;

    /// <summary>
    /// A factor on the size of the terrain only (the icons do not get it), for the first live check to correct the game's mesh unit by (the Debug command <c>vuhelm scale</c>); 1 in play.
    /// </summary>
    public float UserScale { get; private set; } = 1f;

    /// <summary>The zoom of the console map: a factor on the size of terrain and icons together; 1 at the helm.</summary>
    public float Zoom { get; private set; } = 1f;

    /// <summary>A turn of the map in the anchor's frame before the hull's own turn (the yaw and tilt that the navigator gives the map with the mouse); identity at the helm.</summary>
    public Quaternion View { get; set; } = Quaternion.identity;

    public Transform Anchor => anchor;

    /// <summary>World units that one real metre takes on the hologram at this zoom.</summary>
    public float WorldUnitsPerMetreNow => worldUnitsPerMetre * Zoom * SizeFactor;

    /// <summary>
    /// How much the anchor has been scaled since the hologram was built (the navigator's size control scales the frame that the hologram hangs on: the terrain and the icons, which are its children, follow
    /// by themselves, and this is for the math that is done in the world). 1 until it is scaled.
    /// </summary>
    public float SizeFactor => anchor ? anchor.lossyScale.x / builtAnchorScale : 1f;

    public void Setup(Transform anchor, Transform pivot, Transform holder, float worldUnitsPerMetre)
    {
        this.anchor = anchor;
        this.pivot = pivot;
        this.holder = holder;
        this.worldUnitsPerMetre = worldUnitsPerMetre;
        builtAnchorScale = anchor ? Mathf.Max(0.0001f, anchor.lossyScale.x) : 1f;
    }

    public void SetUserScale(float factor)
    {
        UserScale = Mathf.Clamp(factor, 0.05f, 20f);
        ApplyScale();
    }

    public void SetZoom(float zoom)
    {
        Zoom = Mathf.Clamp(zoom, 0.05f, 50f);
        ApplyScale();
    }

    /// <summary>The hull's turn, the map's view and the anchor's own turn as one: the rotation that takes a direction in the world to the same direction on the hologram.</summary>
    public Quaternion Turn => anchor && pivot ? anchor.rotation * View * Quaternion.Inverse(pivot.rotation) : Quaternion.identity;

    /// <summary>Where a real position is on the hologram (the pivot is at the anchor's middle). Valid after this component's last LateUpdate.</summary>
    public Vector3 WorldToMap(Vector3 world)
    {
        if (!anchor || !pivot)
        {
            return world;
        }
        (float x, float y, float z) = HologramMath.ToMap(Pack(world), Pack(pivot.position), Pack(anchor.position), TurnMatrix(), WorldUnitsPerMetreNow);
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Where a point of the hologram is in the real world, with its real depth: the inverse of <see cref="WorldToMap" /> for every point, not only for the ones on the flat reference plane that
    /// <see cref="TryRayToWorld" /> gives (the plane through the hull's own depth). A point on the terrain of the hologram comes out at the depth of that terrain. The same numbers as <see cref="HologramMath" />.
    /// </summary>
    public Vector3 MapToWorld(Vector3 map)
    {
        if (!anchor || !pivot || WorldUnitsPerMetreNow <= 0.000001f)
        {
            return map;
        }
        (float x, float y, float z) = HologramMath.ToWorld(Pack(map), Pack(pivot.position), Pack(anchor.position), TurnMatrix(), WorldUnitsPerMetreNow);
        return new Vector3(x, y, z);
    }

    private Rot3 TurnMatrix()
    {
        Matrix4x4 m = Matrix4x4.Rotate(Turn);
        return new Rot3(m.m00, m.m01, m.m02, m.m10, m.m11, m.m12, m.m20, m.m21, m.m22);
    }

    private static (float X, float Y, float Z) Pack(Vector3 v) => (v.x, v.y, v.z);

    /// <summary>
    /// Where a ray from the camera meets the map's plane, as a place in the world: the inverse of <see cref="WorldToMap" /> for the plane of the terrain (the hull's horizontal plane, at the pivot's depth). False
    /// when the ray does not meet the plane in front of it.
    /// </summary>
    public bool TryRayToWorld(Ray ray, out Vector3 world)
    {
        world = default;
        float scale = WorldUnitsPerMetreNow;
        if (!anchor || !pivot || scale <= 0.000001f)
        {
            return false;
        }
        Plane plane = new(anchor.up, anchor.position);
        if (!plane.Raycast(ray, out float enter) || enter <= 0f)
        {
            return false;
        }
        Vector3 offset = ray.GetPoint(enter) - anchor.position;
        world = pivot.position + Quaternion.Inverse(Turn) * offset / scale;
        return true;
    }

    private void ApplyScale()
    {
        if (holder)
        {
            holder.localScale = Vector3.one * (UserScale * Zoom);
        }
    }

    private void LateUpdate()
    {
        SyncHolder();
    }

    /// <summary>
    /// Puts the holder of the terrain where this frame's <c>LateUpdate</c> puts it: turned by <see cref="Turn" /> and at the place of the anchor's world position. The game's <c>MiniWorld</c> sets the holder's turn to
    /// the identity in every <c>Update</c> and this component sets it right again in <c>LateUpdate</c>, before the picture is drawn; a pick that is made in an <c>Update</c> after the game's meets the terrain unturned
    /// and is then taken back to the world with the real turn, which put the marks of the play-test of Build 3c in the wrong place, more wrongly the farther from the middle of the map. A pick calls this first.
    /// </summary>
    public void SyncHolder()
    {
        if (!anchor || !pivot || !holder)
        {
            return;
        }
        holder.rotation = Turn;
        holder.position = WorldToMap(anchor.position);
    }
}
