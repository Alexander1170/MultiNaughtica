using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

[TestClass]
public class ReaperVehicleGrabPacketTest
{
    [TestMethod]
    public void TheGrabNamesTheReaperAndTheVehicle()
    {
        Packet.InitSerializer();
        NitroxId reaperId = new();
        NitroxId vehicleId = new();

        ReaperGrabVehicle received = (ReaperGrabVehicle)Packet.Deserialize(new ReaperGrabVehicle(reaperId, vehicleId).Serialize());

        received.ReaperId.Should().Be(reaperId);
        received.VehicleId.Should().Be(vehicleId);
    }

    [TestMethod]
    public void TheReleaseNamesTheReaper()
    {
        Packet.InitSerializer();
        NitroxId reaperId = new();

        ReaperReleaseVehicle received = (ReaperReleaseVehicle)Packet.Deserialize(new ReaperReleaseVehicle(reaperId).Serialize());

        received.ReaperId.Should().Be(reaperId);
    }
}
