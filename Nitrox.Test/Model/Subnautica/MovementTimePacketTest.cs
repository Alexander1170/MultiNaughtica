using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.Packets;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The timestamp of a movement packet is a double (bug 7): a float cannot hold the play time of an old world to the millisecond (the step is 3.9 ms from 9.1 hours of play, 15.6 ms from 36 hours).
///     The packets are serialized by their layout, so these tests also fix the layout the receiver reads.
/// </summary>
[TestClass]
public class MovementTimePacketTest
{
    // 8 hours, 9.2 hours (the float step doubles), 36.5 hours of play, with a fraction that a float rounds
    private static readonly double[] times = [28800.123456, 33100.987654321, 131400.0000123];

    [TestInitialize]
    public void Init()
    {
        Packet.InitSerializer();
    }

    [TestMethod]
    public void ThePlayerMovementKeepsItsTimeExactly()
    {
        foreach (double time in times)
        {
            PlayerMovement sent = new((SessionId)(ushort)3, new NitroxVector3(1f, 2f, 3f), new NitroxVector3(0.5f, 0f, -0.5f), NitroxQuaternion.Identity, NitroxQuaternion.Identity, time);

            PlayerMovement received = (PlayerMovement)Packet.Deserialize(sent.Serialize());

            received.Time.Should().Be(time);
            received.Position.Should().Be(sent.Position);
            received.SessionId.Should().Be(sent.SessionId);
        }
    }

    [TestMethod]
    public void ThePlayerMovementTimeIsNotRoundedToAFloat()
    {
        // 33100.987654321 as a float is 33100.98828125 (a step of 3.9 ms): the packet must not come back as that
        const double TIME = 33100.987654321;
        PlayerMovement sent = new((SessionId)(ushort)3, NitroxVector3.Zero, NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxQuaternion.Identity, TIME);

        PlayerMovement received = (PlayerMovement)Packet.Deserialize(sent.Serialize());

        received.Time.Should().NotBe((double)(float)TIME);
        received.Time.Should().Be(TIME);
    }

    [TestMethod]
    public void ThePlayerInCyclopsMovementKeepsItsTimeExactly()
    {
        foreach (double time in times)
        {
            PlayerInCyclopsMovement sent = new((SessionId)(ushort)4, new NitroxVector3(0.1f, 0.2f, 0.3f), new NitroxVector3(1f, 0f, 0f), NitroxQuaternion.Identity, time);

            PlayerInCyclopsMovement received = (PlayerInCyclopsMovement)Packet.Deserialize(sent.Serialize());

            received.Time.Should().Be(time);
            received.LocalPosition.Should().Be(sent.LocalPosition);
            received.SessionId.Should().Be(sent.SessionId);
        }
    }

    [TestMethod]
    public void TheVehicleMovementsKeepTheirRealTimeExactly()
    {
        // it was a double all along; the three kinds of movement packet now agree
        foreach (double time in times)
        {
            VehicleMovements sent = new([new SimpleMovementData(new NitroxId(), new NitroxVector3(1f, 2f, 3f), NitroxQuaternion.Identity)], time);

            VehicleMovements received = (VehicleMovements)Packet.Deserialize(sent.Serialize());

            received.RealTime.Should().Be(time);
            received.Data.Should().HaveCount(1);
        }
    }

    [TestMethod]
    public void TheTimeOfAMovementPacketIsADoubleOnEveryKind()
    {
        typeof(PlayerMovement).GetProperty(nameof(PlayerMovement.Time))!.PropertyType.Should().Be(typeof(double));
        typeof(PlayerInCyclopsMovement).GetProperty(nameof(PlayerInCyclopsMovement.Time))!.PropertyType.Should().Be(typeof(double));
        typeof(VehicleMovements).GetProperty(nameof(VehicleMovements.RealTime))!.PropertyType.Should().Be(typeof(double));
    }
}
