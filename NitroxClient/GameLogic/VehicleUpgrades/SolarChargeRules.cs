namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The charge of the Seamoth and Cyclops solar chargers (docs/coop/VEHICLE_UPGRADES.md), free of Unity types so that it can be tested. It is the falloff of a base's solar panel: vanilla
/// <c>SolarPanel.GetRechargeScalar()</c> is the depth scalar <c>(200 - depth) / 200</c> (clamped to 0..1) times the local light scalar of the day-night cycle times the biome's sunlight scale,
/// and a solar panel adds <c>scalar * deltaTime * 1.25</c> energy. The chargers add <c>scalar * deltaTime * perSecond</c> where <c>perSecond</c> is the server's setting
/// (<see cref="VehicleUpgradeSettings.SolarChargeSeamoth" /> or <see cref="VehicleUpgradeSettings.SolarChargeCyclops" />), the energy per second at the surface in full daylight.
/// <para>
/// The depth curve of the vanilla panel is a curve asset of its prefab; the chargers use the straight line it is built around, which gives the same zero at 200 m and the same one at the surface.
/// </para>
/// </summary>
public static class SolarChargeRules
{
    /// <summary>The depth at which a solar charger no longer charges (a base's solar panel: <c>SolarPanel.maxDepth</c>).</summary>
    public const float MAX_DEPTH = 200f;

    /// <summary>Several installed Cyclops solar chargers do not add up: only one counts. (The Seamoth modules add up per module, like the game's own module does.)</summary>
    public const int MAX_COUNTING_MODULES = 1;

    /// <summary>
    /// The longest time step one charge is calculated for. A frame that took longer (a hitch, a pause, a jump of the game's clock) is charged as if it took this long, so that a time jump
    /// cannot put a whole cell's worth of energy into the vehicle at once.
    /// </summary>
    public const float MAX_DELTA_SECONDS = 1f;

    /// <summary>The share of the charge the depth leaves: 1 at the surface (and above), 0 at <see cref="MAX_DEPTH" /> and deeper. <paramref name="depth" /> is metres below the surface.</summary>
    public static float DepthScalar(float depth)
    {
        if (float.IsNaN(depth))
        {
            return 0f;
        }
        float scalar = (MAX_DEPTH - depth) / MAX_DEPTH;
        return scalar < 0f ? 0f : scalar > 1f ? 1f : scalar;
    }

    /// <summary>The share of the charge the sun and the depth leave. <paramref name="lightScalar" /> is the game's local light scalar (0 at night, 1 at noon) times the biome's sunlight scale.</summary>
    public static float RechargeScalar(float depth, float lightScalar)
    {
        if (float.IsNaN(lightScalar) || lightScalar <= 0f)
        {
            return 0f;
        }
        return DepthScalar(depth) * (lightScalar > 1f ? 1f : lightScalar);
    }

    /// <summary>
    /// The energy of one step: for a Seamoth module, one second; for the Cyclops, one frame. <paramref name="deltaSeconds" /> is the day-night cycle's delta time, like the base's solar panel
    /// uses (the game's own clock, not the frame time), and is capped at <see cref="MAX_DELTA_SECONDS" />.
    /// </summary>
    public static float EnergyFor(float perSecond, float depth, float lightScalar, float deltaSeconds)
    {
        if (perSecond <= 0f || deltaSeconds <= 0f)
        {
            return 0f;
        }
        return RechargeScalar(depth, lightScalar) * perSecond * (deltaSeconds > MAX_DELTA_SECONDS ? MAX_DELTA_SECONDS : deltaSeconds);
    }

    /// <summary>How many of <paramref name="installed" /> Cyclops solar chargers count: only one, however many are installed.</summary>
    public static int CountingModules(int installed)
    {
        return installed <= 0 ? 0 : MAX_COUNTING_MODULES;
    }

    /// <summary>
    /// The energy a charger may put in so that the cells do not take more than they hold: the smaller of <paramref name="energy" /> and the room that is left.
    /// </summary>
    public static float FitIntoRoom(float energy, float capacity, float stored)
    {
        float room = capacity - stored;
        if (room <= 0f || energy <= 0f)
        {
            return 0f;
        }
        return energy < room ? energy : room;
    }
}
