using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A teleporter pair was activated (a crystal in its terminal, or a teleporter opening): tells the other players, see <see cref="TeleporterSync" />.
/// </summary>
public sealed partial class TeleporterManager_ActivateTeleporter_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => TeleporterManager.ActivateTeleporter(default));

    public static void Postfix(string identifier)
    {
        TeleporterSync.ReportActivation(identifier);
    }
}
