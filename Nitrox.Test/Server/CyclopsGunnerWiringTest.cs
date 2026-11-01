using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Nitrox.Server.Subnautica.Extensions;
using Nitrox.Server.Subnautica.Models.GameLogic;
using Nitrox.Server.Subnautica.Models.Packets.Processors;

namespace Nitrox.Server.Subnautica;

/// <summary>
///     The gunner seats are kept by one arbiter and ended from several processors (feature group 3, station 2). Nothing in the tests builds the server's container, so a missing registration or a processor that
///     lost its arbiter parameter in a merge would only be found when the server starts; these guards find it earlier.
/// </summary>
[TestClass]
public class CyclopsGunnerWiringTest
{
    [TestMethod]
    public void TheArbiterIsRegisteredAsASingletonOfTheWorld()
    {
        ServiceCollection services = new();

        services.AddWorld();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(CyclopsGunnerArbiter) && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [TestMethod]
    [DataRow(typeof(CyclopsGunnerSeatProcessor))]
    [DataRow(typeof(CyclopsGunnerFireProcessor))]
    [DataRow(typeof(PlayerSyncFinishedProcessor))]
    [DataRow(typeof(EntityDestroyedPacketProcessor))]
    [DataRow(typeof(EntityMetadataUpdateProcessor))]
    [DataRow(typeof(PlayerDeathEventProcessor))]
    [DataRow(typeof(SubRootChangedPacketProcessor))]
    public void EveryProcessorThatEndsOrShowsASeatAsksForTheArbiter(Type processor)
    {
        ConstructorInfo[] constructors = processor.GetConstructors();

        constructors.Should().ContainSingle("a processor has one public constructor");
        constructors[0].GetParameters().Select(parameter => parameter.ParameterType).Should().Contain(typeof(CyclopsGunnerArbiter), $"{processor.Name} ends or shows the seats");
    }
}
