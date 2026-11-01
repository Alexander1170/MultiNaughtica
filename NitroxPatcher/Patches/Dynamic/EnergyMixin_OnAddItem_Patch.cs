using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class EnergyMixin_OnAddItem_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((EnergyMixin t) => t.OnAddItem(default(InventoryItem)));

    public static void Postfix(EnergyMixin __instance, InventoryItem item)
    {
        // The battery of a scanner room's dock camera is local to this client (the camera is not an entity, see MapRoomCameraBatteries): nothing to broadcast, and no "Couldn't find an id" warning.
        if (item != null && !MapRoomCameraBatteries.IsLocalOnlyBattery(__instance))
        {
            Resolve<ItemContainers>().BroadcastBatteryAdd(item.item.gameObject, __instance, item.techType);
        }
    }
}
