using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Engineer;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The game's engine heat steps every five seconds (<c>SubFire.EngineOverheatSimulation</c>). While the engineer's overdrive runs, the engine gets one more point of heat at each step in which the game is heating
/// it anyway (flank speed, throttle, engine on), before the game rolls its dice, so that the roll sees it (the engineer console, station 3). Only the client that holds the lock of the Cyclops runs the engine
/// (and creates the fires: <see cref="SubFire_CreateFire_Patch" />), so only that client changes the heat.
/// </summary>
public sealed partial class SubFire_EngineOverheatSimulation_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubFire t) => t.EngineOverheatSimulation());

    public static void Prefix(SubFire __instance)
    {
        // (the game's own simulation starts with the same guard: nothing happens while the engine room is not at full detail)
        if (!__instance.LOD || !__instance.LOD.IsFull() || !__instance.subRoot || !__instance.subRoot.TryGetNitroxId(out NitroxId id) || !EngineerState.IsOverdriveActive(id) || !Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            return;
        }
        // (the module and the settings gate the route: a Cyclops that lost the module has no overdrive)
        if (EngineerState.EffectiveRoute(__instance.subRoot, out _) != EngineerRoute.Engines)
        {
            return;
        }
        EngineerHeat.Set(__instance, CyclopsEngineerRules.HeatBeforeStep(EngineerHeat.Get(__instance), true, EngineerHeat.IsHeating(__instance)));
    }
}
