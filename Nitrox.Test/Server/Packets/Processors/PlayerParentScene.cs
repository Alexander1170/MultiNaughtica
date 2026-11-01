using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

/// <summary>
/// One player with a player entity, a lifepod and a sub root (a Cyclops) in an entity registry, for the processors that write the parent of a player.
/// </summary>
internal sealed class PlayerParentScene
{
    private const ushort SESSION_ID = 1;

    public EntityRegistry EntityRegistry { get; } = new(NullLogger<EntityRegistry>.Instance);
    public IPacketSender PacketSender { get; } = Substitute.For<IPacketSender>();
    public Player Player { get; }
    public AuthProcessorContext Context { get; }
    public PlayerEntity PlayerEntity { get; }
    public EscapePodEntity Pod { get; }
    public WorldEntity SubRoot { get; }

    public PlayerParentScene(PlayerStart start)
    {
        Pod = new EscapePodEntity(NitroxVector3.Zero, new NitroxId(), null);
        SubRoot = new WorldEntity(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Cyclops"), 0, "", false, new NitroxId(), null);
        EntityRegistry.AddOrUpdate(Pod);
        EntityRegistry.AddOrUpdate(SubRoot);

        NitroxId playerId = new();
        Optional<NitroxId> startParentId = start switch
        {
            PlayerStart.IN_POD => Pod.Id,
            PlayerStart.IN_SUB => SubRoot.Id,
            _ => Optional.Empty
        };
        PlayerEntity = new PlayerEntity(new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), 0, null, false, playerId, NitroxTechType.None, null, startParentId.OrNull(), []);
        EntityRegistry.AddOrUpdate(PlayerEntity);

        Player = new Player(SESSION_ID, SESSION_ID, "Player", false, null, NitroxVector3.Zero, NitroxQuaternion.Identity, playerId, startParentId, Perms.PLAYER,
                            new PlayerStatsData(100, 100, 100, 100, 100, 0), SubnauticaGameMode.SURVIVAL, [], [], new Dictionary<string, NitroxId>(), new Dictionary<string, float>(),
                            new Dictionary<string, PingInstancePreference>(), [], false, true);
        Context = new AuthProcessorContext(Player, PacketSender);
    }

    public enum PlayerStart
    {
        IN_OPEN_WATER,
        IN_POD,
        IN_SUB
    }
}
