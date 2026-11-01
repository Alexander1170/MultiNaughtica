using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The fuel of a hand-held fire extinguisher is part of its metadata (<see cref="Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.FireExtinguisherMetadata"/>), but the
/// item paths (drop, pick up) only extract it at their own moment: a tank that was used and then put into a locker, or taken from a wall holder with the holder's fuel written in after
/// the pick-up, kept the fuel of an earlier moment on the server. Sent while it is used (throttled) and when the player puts the tool away (<see cref="PlayerTool_OnHolster_Patch"/>).
/// </summary>
public sealed partial class FireExtinguisher_UseExtinguisher_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((FireExtinguisher t) => t.UseExtinguisher(default, default));

    public static void Postfix(FireExtinguisher __instance)
    {
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            Resolve<Entities>().EntityMetadataChangedThrottled(__instance, id, 2f);
        }
    }
}
