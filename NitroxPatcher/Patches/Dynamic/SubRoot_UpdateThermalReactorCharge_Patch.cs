using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The thermal reactor module of a Cyclops adds energy to the Cyclops' power relay in every frame, on every client that has the Cyclops loaded: with two clients both added to the
/// same battery and each client's charge update overwrote the other's (the charge stayed at the first whole number it reached). Only the client that holds the lock of the Cyclops runs it
/// (V0, battery charge; the same rule as <see cref="CyclopsShieldButton_ShieldIteration_Patch"/>).
/// </summary>
public sealed partial class SubRoot_UpdateThermalReactorCharge_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((SubRoot t) => t.UpdateThermalReactorCharge());

    public static bool Prefix(SubRoot __instance)
    {
        // A sub that is not an entity of the world (no id) keeps the vanilla behaviour
        return !__instance.TryGetNitroxId(out NitroxId id) || Resolve<SimulationOwnership>().HasAnyLockType(id);
    }
}
