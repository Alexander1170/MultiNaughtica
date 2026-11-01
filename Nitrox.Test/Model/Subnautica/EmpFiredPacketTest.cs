using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class EmpFiredPacketTest
{
    [TestMethod]
    public void TheBlastNamesTheCreature()
    {
        Packet.InitSerializer();
        NitroxId creatureId = new();

        EmpFired received = (EmpFired)Packet.Deserialize(new EmpFired(creatureId).Serialize());

        received.CreatureId.Should().Be(creatureId);
    }
}
