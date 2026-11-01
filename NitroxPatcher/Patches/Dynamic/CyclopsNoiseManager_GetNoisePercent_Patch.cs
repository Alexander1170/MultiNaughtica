using System.Reflection;
using NitroxClient.GameLogic.Engineer;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The noise that creatures hear from a Cyclops (the engineer console, station 3): while silent running is on and the engineer sends the power to the systems, it is lower by the server's
/// <c>SilentRunningBonus</c> on top of what the game's silent running takes away. Applied where the game reads the noise (it is computed at the read, nothing is stored: the game only recalculates the stored value
/// when the engine or silent running changes), which is on whichever client's creature listens: <c>AttackCyclops</c> takes the Cyclops the local player is in, so every client needs the factor, and every client
/// has the route (the server sends it to everybody).
/// </summary>
public sealed partial class CyclopsNoiseManager_GetNoisePercent_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CyclopsNoiseManager t) => t.GetNoisePercent());

    public static void Postfix(CyclopsNoiseManager __instance, ref float __result)
    {
        float factor = EngineerState.NoiseFactor(__instance.subRoot);
        if (factor != 1f)
        {
            __result *= factor;
        }
    }
}
