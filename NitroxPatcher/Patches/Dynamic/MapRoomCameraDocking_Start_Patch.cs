using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// <c>MapRoomCameraDocking.Start</c> makes the dock's camera (Instantiate, <c>CrafterLogic.NotifyCraftEnd</c>, <c>DockCamera</c>). The battery would come from <c>EnergyMixin.OnCraftEnd</c> ->
/// <c>SpawnDefaultAsync</c>, which <see cref="EnergyMixin_SpawnDefaultAsync_Patch"/> blanks (the entity system spawns the batteries of entities), and the dock camera is not an entity: it stayed
/// without a battery (charge 0, capacity 0), so in survival <c>MapRoomCamera.CanBeControlled</c> was false (live probe b8cam P1, 2026-10-19). The battery is installed here instead, and it is
/// local on purpose (every client makes its own two cameras per room, see the design notes (not part of this repository) section 6b). The conditions are in <see cref="MapRoomCameraBatteryRules"/>.
/// </summary>
public sealed partial class MapRoomCameraDocking_Start_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomCameraDocking t) => t.Start());

    public static void Postfix(MapRoomCameraDocking __instance)
    {
        MapRoomCameraBatteries.InstallLocalDefault(__instance.camera, __instance.deserialized);
    }
}
