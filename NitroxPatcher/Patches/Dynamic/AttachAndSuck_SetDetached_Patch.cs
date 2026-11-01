using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// K18, Bleeder: <c>SetDetached</c> is the one exit of a Bleeder's attack (it let go, was punched off, was killed, the victim died or went indoors). If this client holds the
/// Bleeder exclusively (<see cref="AttachAndSuck_OnCollisionEnter_Patch" />), the lock goes back to the normal hand-over of creatures between players.
/// </summary>
public sealed partial class AttachAndSuck_SetDetached_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AttachAndSuck t) => t.SetDetached());

    public static void Postfix(AttachAndSuck __instance)
    {
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            BleederClaims.OnDetached(__instance, id);
        }
    }
}
