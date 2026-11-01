using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Packets;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Model.Subnautica;

/// <summary>
///     The metadata of a Cyclops lighting panel carries the on/off switch of the Cyclops Power Share next to the two lights: it travels to the other players, is saved with the world, and a world saved before the
///     switch existed loads with the switch off.
/// </summary>
[TestClass]
public class CyclopsLightingMetadataTest
{
    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, true, false)]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    public void TheSwitchTravelsInAPacketWithTheLights(bool floodLights, bool internalLights, bool powerShare)
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new CyclopsLightingMetadata(floodLights, internalLights, powerShare));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        CyclopsLightingMetadata metadata = received.NewValue.Should().BeOfType<CyclopsLightingMetadata>().Subject;
        metadata.FloodLightsOn.Should().Be(floodLights);
        metadata.InternalLightsOn.Should().Be(internalLights);
        metadata.PowerShareOn.Should().Be(powerShare);
    }

    [TestMethod]
    public void TheSwitchIsSavedWithTheWorldAndLoadsAgain()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();

        serializer.Serialize(stream, new Holder { Metadata = new CyclopsLightingMetadata(false, true, true) });
        stream.Flush();
        string text = Encoding.UTF8.GetString(stream.ToArray());
        Holder loaded = serializer.Deserialize<Holder>(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        text.Should().Contain("\"PowerShareOn\"");
        CyclopsLightingMetadata metadata = loaded.Metadata.Should().BeOfType<CyclopsLightingMetadata>().Subject;
        metadata.PowerShareOn.Should().BeTrue();
        metadata.InternalLightsOn.Should().BeTrue();
        metadata.FloodLightsOn.Should().BeFalse();
    }

    [TestMethod]
    public void AWorldSavedBeforeTheSwitchExistedLoadsWithTheSwitchOff()
    {
        // the text of a lighting panel in EntityData.json as the build before this one wrote it: the world of the first play sessions must keep loading, with the Power Share off
        const string saved = """{"Metadata":{"$type":"Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CyclopsLightingMetadata, Nitrox.Model.Subnautica","FloodLightsOn":true,"InternalLightsOn":true}}""";
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);

        Holder loaded = serializer.Deserialize<Holder>(new MemoryStream(Encoding.UTF8.GetBytes(saved)));

        CyclopsLightingMetadata metadata = loaded.Metadata.Should().BeOfType<CyclopsLightingMetadata>().Subject;
        metadata.PowerShareOn.Should().BeFalse();
        metadata.Changed.Should().Be(CyclopsLightingMetadata.CHANGED_NOTHING);
        metadata.FloodLightsOn.Should().BeTrue();
        metadata.InternalLightsOn.Should().BeTrue();
    }

    [TestMethod]
    [DataRow(CyclopsLightingMetadata.CHANGED_NOTHING)]
    [DataRow(CyclopsLightingMetadata.CHANGED_FLOOD)]
    [DataRow(CyclopsLightingMetadata.CHANGED_INTERNAL)]
    [DataRow(CyclopsLightingMetadata.CHANGED_POWER_SHARE)]
    public void WhatThePressChangedIsNamedInThePacket(byte changed)
    {
        Packet.InitSerializer();
        EntityMetadataUpdate packet = new(new NitroxId(), new CyclopsLightingMetadata(true, false, true, changed));

        EntityMetadataUpdate received = (EntityMetadataUpdate)Packet.Deserialize(packet.Serialize());

        CyclopsLightingMetadata metadata = received.NewValue.Should().BeOfType<CyclopsLightingMetadata>().Subject;
        metadata.Changed.Should().Be(changed);
        metadata.PowerShareOn.Should().BeTrue();
        metadata.FloodLightsOn.Should().BeTrue();
        metadata.InternalLightsOn.Should().BeFalse();
    }

    [TestMethod]
    public void AReportOfALightKeepsTheSwitchTheServerHas()
    {
        // Bob flipped the share on; Alice's client has not got that yet (or never does) and reports the whole panel when she presses a light, with her old switch
        CyclopsLightingMetadata stored = new(false, true, true);
        CyclopsLightingMetadata reported = new(true, true, false);

        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(stored, reported);

        merged.PowerShareOn.Should().BeTrue();
        merged.FloodLightsOn.Should().BeTrue();
        merged.InternalLightsOn.Should().BeTrue();
    }

    [TestMethod]
    public void AReportOfALightKeepsASwitchThatIsOffToo()
    {
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(new CyclopsLightingMetadata(true, true, false), new CyclopsLightingMetadata(true, false, true));

        merged.PowerShareOn.Should().BeFalse();
        merged.InternalLightsOn.Should().BeFalse();
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void ThePressOfTheSwitchSetsTheSwitchAndKeepsTheLightsTheServerHas(bool storedOn, bool pressedTo)
    {
        // the presser's client has not got news of the floodlights yet: it reports them off, the server has them on
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(new CyclopsLightingMetadata(true, false, storedOn), new CyclopsLightingMetadata(false, true, pressedTo, CyclopsLightingMetadata.CHANGED_POWER_SHARE));

        merged.PowerShareOn.Should().Be(pressedTo);
        merged.FloodLightsOn.Should().BeTrue();
        merged.InternalLightsOn.Should().BeFalse();
    }

    [TestMethod]
    public void APressOnTheFloodlightsChangesOnlyThem()
    {
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(new CyclopsLightingMetadata(false, true, true), new CyclopsLightingMetadata(true, false, false, CyclopsLightingMetadata.CHANGED_FLOOD));

        merged.FloodLightsOn.Should().BeTrue();
        merged.InternalLightsOn.Should().BeTrue("the interior lights are the server's");
        merged.PowerShareOn.Should().BeTrue("so is the switch");
    }

    [TestMethod]
    public void APressOnTheInteriorLightsChangesOnlyThem()
    {
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(new CyclopsLightingMetadata(true, true, false), new CyclopsLightingMetadata(false, false, true, CyclopsLightingMetadata.CHANGED_INTERNAL));

        merged.InternalLightsOn.Should().BeFalse();
        merged.FloodLightsOn.Should().BeTrue("the floodlights are the server's");
        merged.PowerShareOn.Should().BeFalse("so is the switch");
    }

    [TestMethod]
    public void TheReporterIsToldWhenWhatTheServerKeptDiffersFromWhatItReported()
    {
        CyclopsLightingMetadata stored = new(true, true, true);

        CyclopsLightingMetadata keptAfterAnOldView = CyclopsLightingMetadata.Merge(stored, new CyclopsLightingMetadata(true, false, false, CyclopsLightingMetadata.CHANGED_INTERNAL));
        CyclopsLightingMetadata keptAfterAnUpToDateView = CyclopsLightingMetadata.Merge(stored, new CyclopsLightingMetadata(true, false, true, CyclopsLightingMetadata.CHANGED_INTERNAL));

        keptAfterAnOldView.HasSameStateAs(new CyclopsLightingMetadata(true, false, false)).Should().BeFalse("the reporter thinks the switch is off");
        keptAfterAnUpToDateView.HasSameStateAs(new CyclopsLightingMetadata(true, false, true)).Should().BeTrue();
        keptAfterAnUpToDateView.HasSameStateAs(null).Should().BeFalse();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WithNothingStoredYetTheReportIsKept(bool reportedOn)
    {
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(null, new CyclopsLightingMetadata(true, false, reportedOn));

        merged.PowerShareOn.Should().Be(reportedOn);
        merged.FloodLightsOn.Should().BeTrue();
        merged.InternalLightsOn.Should().BeFalse();
    }

    [TestMethod]
    public void WhatIsKeptIsNeverAPressOfTheSwitch()
    {
        // The flag belongs to the report: the world must not save it and the others must not read a stored switch as a fresh press
        CyclopsLightingMetadata merged = CyclopsLightingMetadata.Merge(null, new CyclopsLightingMetadata(true, true, true, CyclopsLightingMetadata.CHANGED_POWER_SHARE));
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();

        serializer.Serialize(stream, new Holder { Metadata = merged });
        stream.Flush();

        merged.Changed.Should().Be(CyclopsLightingMetadata.CHANGED_NOTHING);
        Encoding.UTF8.GetString(stream.ToArray()).Should().NotContain("\"Changed\":4");
    }

    [TestMethod]
    public void TheLogLineNamesTheSwitch()
    {
        new CyclopsLightingMetadata(false, true, true).ToString().Should().Contain("PowerShareOn: True");
    }

    public class Holder
    {
        public EntityMetadata Metadata { get; set; }
    }
}
