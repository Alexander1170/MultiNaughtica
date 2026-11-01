using System.Reflection;
using NitroxClient.GameLogic.VehicleUpgrades;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A player who holds on to a Seamoth's rider bar is protected like an occupant: the creatures of the game ask the player this before they target, attack or grab (about twenty places).
/// </summary>
public sealed partial class Player_CanBeAttacked_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Player t) => t.CanBeAttacked());

    public static void Postfix(ref bool __result)
    {
        if (__result && RiderBars.IsLocalRiding)
        {
            PatchProbe.Fired("Player.CanBeAttacked");
            __result = false;
        }
    }
}
