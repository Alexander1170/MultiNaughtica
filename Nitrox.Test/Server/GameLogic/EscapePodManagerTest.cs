using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nitrox.Model.Configuration;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Factories;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Packets.Core;
using Nitrox.Server.Subnautica.Services.Core;
using NSubstitute;

namespace Nitrox.Server.Subnautica.Models.GameLogic;

[TestClass]
public class EscapePodManagerTest
{
    private readonly EntityRegistry entityRegistry = new(NullLogger<EntityRegistry>.Instance);
    private readonly EscapePodManager escapePodManager;
    private readonly WorldEntityManager worldEntityManager;

    public EscapePodManagerTest()
    {
        IOptions<SubnauticaServerOptions> options = Options.Create(new SubnauticaServerOptions());
        // The batch spawner and the player manager are not used to register a global root entity
        worldEntityManager = new WorldEntityManager(Substitute.For<IPacketSender>(), entityRegistry, null, null, Substitute.For<IProgressReporter>(), NullLogger<WorldEntityManager>.Instance);
        escapePodManager = new EscapePodManager(new RandomFactory(options), entityRegistry, worldEntityManager, null, options, Substitute.For<IPacketSender>());
    }

    [TestMethod]
    public void CreatedPod_IsAmongTheGlobalRootEntitiesThatTheInitialSyncSends()
    {
        // #2898: a pod that is only in the registry is missing from the initial sync of the player assigned to it, and that player's escape pod lookup threw
        EscapePodEntity pod = escapePodManager.CreateEscapePod(new NitroxVector3(10f, 0f, -20f));

        worldEntityManager.GetGlobalRootEntities(true).Should().Contain(pod);
    }

    [TestMethod]
    public void CreatedPod_IsInTheRegistryToo()
    {
        EscapePodEntity pod = escapePodManager.CreateEscapePod(new NitroxVector3(10f, 0f, -20f));

        entityRegistry.TryGetEntityById(pod.Id, out EscapePodEntity registered).Should().BeTrue();
        registered.Should().BeSameAs(pod);
    }

    [TestMethod]
    public void CreatedPod_HasItsSuppliesAsChildren()
    {
        EscapePodEntity pod = escapePodManager.CreateEscapePod(new NitroxVector3(10f, 0f, -20f));

        pod.ChildEntities.Should().HaveCount(4);
        pod.ChildEntities.Should().OnlyContain(child => child.ParentId == pod.Id);
        pod.Transform.Position.Should().Be(new NitroxVector3(10f, 0f, -20f));
    }
}
