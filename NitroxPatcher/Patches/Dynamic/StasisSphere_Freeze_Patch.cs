using System.Reflection;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Prevents remote players from being frozen by remote stasis fields. A stasis field also never freezes a Cyclops (the gunner station fires from one: a frozen Cyclops stops the pilot's boat, and the replay of
/// the gunner's sphere on the pilot's client would freeze exactly that) nor the vehicle that the local player flies (its body is the only copy of it that is not kinematic).
/// </summary>
public sealed partial class StasisSphere_Freeze_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((StasisSphere t) => t.Freeze(default, ref Reflect.Ref<Rigidbody>.Field));

    public static bool Prefix(Collider other, ref bool __result)
    {
        if (other.gameObject.IsRemotePlayer)
        {
            return false;
        }

        Rigidbody body = other.attachedRigidbody;
        if (body)
        {
            if (body.TryGetComponent(out SubRoot subRoot) && subRoot.isCyclops)
            {
                return false;
            }
            Vehicle flown = Player.main ? Player.main.GetVehicle() : null;
            if (flown && flown.gameObject == body.gameObject)
            {
                return false;
            }
        }
        return true;
    }
}
