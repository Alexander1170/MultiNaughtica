using System;
using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: when the inventory alone cannot pay for the craft, the craft waits for the server to hand over the exact items of the lockers
/// (<see cref="LockerCrafting.OnCraft" />), and starts when the answer is granted. While the vanilla method runs without a claim the lockers are not counted (the finalizer ends that).
/// </summary>
public sealed partial class GhostCrafter_Craft_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((GhostCrafter t) => t.Craft(default(TechType), default(float)));

    public static bool Prefix(GhostCrafter __instance, TechType techType, float duration)
    {
        return LockerCrafting.OnCraft(__instance, techType, duration);
    }

    public static Exception Finalizer(Exception __exception)
    {
        LockerCrafting.OnCraftFinished();
        return __exception;
    }
}
