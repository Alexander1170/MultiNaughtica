using System.Reflection;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication.Abstract;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// A Sea Emperor baby that reaches the teleporter is destroyed on the client (<c>SeaEmperorBaby.Teleport</c>). The real babies are server entities (the hatching player
/// reports them, <see cref="IncubatorEgg_HatchNow_Patch" />), and the server was never told: they were spawned again at the teleporter on every later load, for a late joiner and
/// after a restart, and started the mother interaction again. Every client that has the baby reports it, the server ignores what it no longer has.
/// </summary>
public sealed partial class SeaEmperorBaby_Teleport_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((SeaEmperorBaby t) => t.Teleport());

    public static void Prefix(SeaEmperorBaby __instance)
    {
        // The temporary babies of the hatch animation have no id and nobody else knows them
        if (__instance.TryGetNitroxId(out NitroxId id))
        {
            Resolve<IPacketSender>().Send(new EntityDestroyed(id));
        }
    }
}
