using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The sonar of a Cyclops turns itself off on the client that holds the Cyclops when its player is no longer at the helm (<see cref="CyclopsSonarButton_Update_Patch"/>), or when the
/// Cyclops runs out of power (<see cref="CyclopsSonarButton_SonarPing_Patch"/>). Only a click was broadcast, so the other client kept pinging a sonar that costs nothing there: with nobody
/// at the helm it was free (V0, VEN5). The change is broadcast from the client that holds the lock; the receivers apply it with the packet suppressed, as for a click.
/// </summary>
public sealed partial class CyclopsSonarButton_TurnOffSonar_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsSonarButton t) => t.TurnOffSonar());

    public static void Postfix(CyclopsSonarButton __instance)
    {
        if (__instance.subRoot && __instance.subRoot.TryGetNitroxId(out NitroxId id) && Resolve<SimulationOwnership>().HasAnyLockType(id))
        {
            Resolve<Cyclops>().BroadcastMetadataChange(id);
        }
    }
}
