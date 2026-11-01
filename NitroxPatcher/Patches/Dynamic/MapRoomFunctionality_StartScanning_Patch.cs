using System.Reflection;
using Nitrox.Model.DataStructures;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// The resource a scanner room scans for is chosen on its screen and was kept on that client only: the other player's room scanned for something else, and a restart set it
/// back to nothing. The choice is sent as metadata of the room (<see cref="NitroxClient.GameLogic.Spawning.Metadata.Processor.MapRoomMetadataProcessor"/>).
/// <para>
/// Only a change is sent. The game also calls this method with the room's own value when the room starts (<c>MapRoomFunctionality.Start</c>), which on a joining client can happen after
/// the room got its id and the stored value (<c>RestoreMapRoom</c>): sending that would put the joining client's snapshot back on the server over what another player chose meanwhile.
/// </para>
/// <para>
/// And only a choice. <c>uGUI_MapRoomScanner.OnDisable</c> cancels the scan when the screen of the room is deactivated or destroyed (the room object is destroyed, e.g. a whole base that
/// this client lost, its power is down, possibly the game quits; an intact base that is resynced keeps its room): that is not a choice of a player and must not replace the target on the
/// server and on the other clients (<see cref="uGUI_MapRoomScanner_OnDisable_Patch"/>, <see cref="MapRoomScanTargetRules"/>).
/// </para>
/// </summary>
public sealed partial class MapRoomFunctionality_StartScanning_Patch : NitroxPatch, IDynamicPatch
{
    public static readonly MethodInfo TARGET_METHOD = Reflect.Method((MapRoomFunctionality t) => t.StartScanning(default));

    public static void Prefix(MapRoomFunctionality __instance, out TechType __state)
    {
        __state = __instance.typeToScan;
    }

    public static void Postfix(MapRoomFunctionality __instance, TechType __state)
    {
        if (MapRoomScanTargetRules.ShouldBroadcast(__state != __instance.typeToScan) && __instance.TryGetNitroxId(out NitroxId id))
        {
            Resolve<Entities>().EntityMetadataChanged(__instance, id);
        }
    }
}
