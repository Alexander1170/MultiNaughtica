using System.Reflection;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;

namespace NitroxPatcher.Patches.Dynamic;

public sealed partial class PDAEncyclopedia_Add_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => PDAEncyclopedia.Add(default, default, default));

    /// <summary>
    /// The text and picture of a time capsule entry travel with it: they come from the web service to the player who opened the capsule and nobody else has them
    /// </summary>
    private static TimeCapsuleContentData GetTimeCapsuleContent(string key, PDAEncyclopedia.EntryData entryData)
    {
        if (entryData.kind != PDAEncyclopedia.EntryData.Kind.TimeCapsule || !TimeCapsuleContentProvider.GetData(key, out string title, out string text, out string imageUrl))
        {
            return null;
        }

        return new TimeCapsuleContentData(key, title, text, imageUrl);
    }

    public static void Postfix(string key, bool verbose, PDAEncyclopedia.EntryData __result)
    {
        if (!Multiplayer.Main || !Multiplayer.Main.InitialSyncCompleted)
        {
            return;
        }

        // Is null when it's a duplicate call
        if (__result != null)
        {
            Resolve<IPacketSender>().Send(new PDAEncyclopediaEntryAdd(key, verbose, GetTimeCapsuleContent(key, __result)));
        }
    }
}
