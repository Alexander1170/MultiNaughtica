using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The arithmetic of the terrain hologram at the helm of a Cyclops with a Cyclops Terrain Sonar (docs/coop/VEHICLE_UPGRADES.md): how big the game's own terrain hologram (<c>MiniWorld</c>, the one of the
/// scanner room and of the Seaglide's map) has to be for a range in metres on the round sonar display of the helm, and when it is shown. Pure numbers, no Unity types, so that they are tested.
/// </summary>
/// <remarks>
/// The round display of the helm (<c>CyclopsSonarDisplay</c>) draws every ping at <c>subTransform.InverseTransformPoint(position) * radarDisplaySize / radarScanSize</c> in its own local space, so a real
/// metre is <see cref="UnitsPerMetre" /> local units of the display. The terrain hologram has to use the same scale, or the terrain and the pings of the creatures would disagree.
/// </remarks>
public static class TerrainSonarRules
{
    /// <summary>
    /// Metres that one unit of the hologram's terrain meshes covers. The game's <c>MiniWorld</c> lays its chunks 32 units apart and a chunk covers 128 blocks of the world (a block is a metre), so a unit is
    /// 4 metres. Read from the game's code, not from its data: the first live run logs the bounds of a loaded chunk, which proves or corrects it (a harness script that is not part of this repository).
    /// </summary>
    public const float METRES_PER_MESH_UNIT = 4f;

    /// <summary>The scale of a display's pings when the game's numbers cannot be read: 0.3 display units for the 100 metres of the vanilla sonar (a guess, logged when it is used).</summary>
    public const float FALLBACK_UNITS_PER_METRE = 0.003f;

    /// <summary>Metres loaded beyond the range, so that the rim of the hologram is never a hole.</summary>
    public const float LOAD_MARGIN_METRES = 8f;

    /// <summary>The most metres the game's hologram is asked to load (the scanner room's own largest radius is 500).</summary>
    public const int MAX_LOAD_RADIUS = 640;

    /// <summary>
    /// Display units per real metre of the sonar display: <paramref name="radarDisplaySize" /> over <paramref name="radarScanSize" />. 0 when either is missing, negative or not a number (the caller
    /// then uses <see cref="FALLBACK_UNITS_PER_METRE" />).
    /// </summary>
    public static float UnitsPerMetre(float radarDisplaySize, float radarScanSize)
    {
        if (float.IsNaN(radarDisplaySize) || float.IsInfinity(radarDisplaySize) || float.IsNaN(radarScanSize) || float.IsInfinity(radarScanSize) || radarDisplaySize <= 0f || radarScanSize <= 0.0001f)
        {
            return 0f;
        }
        return radarDisplaySize / radarScanSize;
    }

    /// <summary>
    /// The radius in metres of the terrain the hologram loads: the range around the hull's pivot, plus the distance from the pivot to the display (the game's hologram loads around its own position, which
    /// is the display, while the display shows the terrain around the pivot), plus a margin.
    /// </summary>
    public static int LoadRadius(float rangeMetres, float pivotToDisplayMetres)
    {
        float radius = rangeMetres + Math.Max(0f, pivotToDisplayMetres) + LOAD_MARGIN_METRES;
        return Math.Max(1, Math.Min(MAX_LOAD_RADIUS, (int)Math.Ceiling(radius)));
    }

    /// <summary>
    /// The game's <c>hologramRadius</c> for a <paramref name="loadRadius" />: its terrain meshes are scaled by <c>hologramRadius / mapWorldRadius</c> per mesh unit, and a metre has to take
    /// <paramref name="unitsPerMetre" /> display units, so one mesh unit takes <see cref="METRES_PER_MESH_UNIT" /> times that.
    /// </summary>
    public static float HologramRadius(float unitsPerMetre, int loadRadius)
    {
        return METRES_PER_MESH_UNIT * unitsPerMetre * loadRadius;
    }

    /// <summary>
    /// Where the hologram fades out, in world units: the range in metres, as many display units, times the scale of the display in the world. The game's shader measures from the hologram's own position
    /// (<c>_MapCenterWorldPos</c>, the display's place in the world), and the terrain around the hull's pivot is drawn around exactly that place (<c>TerrainHologramAligner</c>), so the distance from the pivot to the
    /// display does not come into it (the first version added it and drew about 24 m too much).
    /// </summary>
    public static float FadeRadius(float rangeMetres, float unitsPerMetre, float displayWorldScale)
    {
        return Math.Max(0f, rangeMetres) * unitsPerMetre * displayWorldScale;
    }

    /// <summary>
    /// The range of the hologram at the helm: the server's <paramref name="setting" />, but never more than the sonar display itself shows (<paramref name="scanRadius" />, 130 m for the Cyclops: its pings and its
    /// round pad are made for that), so that the terrain stays on the pad. A display whose scan radius is not known gives the setting. The navigator console's map is not limited: it is as big as the setting.
    /// </summary>
    public static float HelmRange(float setting, float scanRadius)
    {
        if (float.IsNaN(scanRadius) || float.IsInfinity(scanRadius) || scanRadius <= 0.0001f)
        {
            return setting;
        }
        return Math.Min(setting, scanRadius);
    }

    /// <summary>World units that one real metre takes on the display (the display's own units per metre times its scale in the world); 0 for a display that has no size.</summary>
    public static float WorldUnitsPerMetre(float unitsPerMetre, float displayWorldScale)
    {
        return unitsPerMetre * Math.Max(0f, displayWorldScale);
    }

    /// <summary>
    /// Whether the hologram is shown: the Cyclops has the module and the server allows the upgrades, and the local player is aboard that Cyclops (nobody pays for the hologram of a Cyclops that they are not in).
    /// </summary>
    public static bool ShouldShow(bool moduleAvailable, bool playerAboard)
    {
        return moduleAvailable && playerAboard;
    }

    /// <summary>What the module's behaviour does to the round display of the helm (<see cref="DisplayStep" />).</summary>
    public enum DisplayAction
    {
        None,

        /// <summary>Switch the display on: the Terrain Sonar is fitted and the game left the display off because the Cyclops has no Cyclops Sonar Module.</summary>
        SwitchOn,

        /// <summary>Give the display back to the game: set it to what the game would have set (on only with the Cyclops Sonar Module).</summary>
        Restore
    }

    /// <summary>
    /// The game switches the round display of the helm (the sonar part of <c>CyclopsHelmHUDManager</c>) on only for a Cyclops Sonar Module, and the Terrain Sonar has to work without it: a player is not asked for two
    /// modules for one display. Run on every poll, because the game sets the display again on every change of a module. We switch it on only when we are the reason it is wanted, and give it back when we no longer are.
    /// </summary>
    public static DisplayAction DisplayStep(bool terrainSonarAvailable, bool vanillaSonarFitted, bool displayActive, bool weSwitchedItOn)
    {
        if (terrainSonarAvailable && !vanillaSonarFitted)
        {
            return displayActive ? DisplayAction.None : DisplayAction.SwitchOn;
        }
        return weSwitchedItOn ? DisplayAction.Restore : DisplayAction.None;
    }

    /// <summary>Whether a hologram built for <paramref name="builtRange" /> has to be built again for <paramref name="wantedRange" /> (the game freezes its scale when it starts).</summary>
    public static bool NeedsRebuild(float builtRange, float wantedRange)
    {
        return Math.Abs(builtRange - wantedRange) > 0.5f;
    }
}
