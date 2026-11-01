using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sends the fuel of a fire extinguisher when the player puts it away (see <see cref="FireExtinguisher_UseExtinguisher_Patch"/>): at that moment the fuel is what the player will find
/// again, also for a tank that was taken from a wall holder (the holder's fuel is written into it after the pick-up that the item paths extract from).
/// </summary>
public sealed partial class PlayerTool_OnHolster_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((PlayerTool t) => t.OnHolster());

    public static void Postfix(PlayerTool __instance)
    {
        if (__instance is FireExtinguisher fireExtinguisher && fireExtinguisher.TryGetNitroxId(out NitroxId id))
        {
            Resolve<Entities>().EntityMetadataChanged(fireExtinguisher, id);
        }
    }
}
