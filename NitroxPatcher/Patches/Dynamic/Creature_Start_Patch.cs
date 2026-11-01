using System.Reflection;
using NitroxClient.GameLogic.Navigator;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The game keeps no list of its creatures, so every creature that starts is offered to the navigator's contact list (<see cref="NavigatorContacts" />), which keeps those that the map would ever draw (the
/// leviathans and the other dangers). All creature classes that override <c>Start</c> call the base, so this reaches them all.
/// </summary>
public sealed partial class Creature_Start_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Creature t) => t.Start());

    public static void Postfix(Creature __instance)
    {
        NavigatorContacts.RegisterCreature(__instance);
    }
}
