using System;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
///     Sync test harness (Debug builds): server asks the receiving client to spawn a prefab at a world position through
///     the same client-side path the <c>spawn</c>/<c>sub</c> dev console commands use, so the spawn is then synced to
///     the server and the other clients exactly like a console spawn. Sent by <c>/spawnvehicle</c> and <c>/spawncreature</c>.
/// </summary>
[Serializable]
public class DebugSpawnEntity(NitroxTechType techType, NitroxVector3 position, NitroxQuaternion rotation) : Packet
{
    public NitroxTechType TechType { get; } = techType;
    public NitroxVector3 Position { get; } = position;
    public NitroxQuaternion Rotation { get; } = rotation;
}
