using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The packet that tells every client that the server saves (item a): it carries the number of the save and nothing else, and goes through the serializer unchanged.
/// </summary>
[TestClass]
public class ServerSavingPacketTest
{
    [TestMethod]
    public void TheNumberOfTheSaveSurvivesTheSerializer()
    {
        Packet.InitSerializer();

        foreach (int number in new[] { 1, 2, 57, int.MaxValue })
        {
            ServerSaving received = (ServerSaving)Packet.Deserialize(new ServerSaving(number).Serialize());

            received.Number.Should().Be(number);
        }
    }

    [TestMethod]
    public void TheNumberIsTheOnlyThingTheClientReads()
    {
        typeof(ServerSaving).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                            .Select(property => property.Name).Should().BeEquivalentTo(["Number"]);
    }
}
