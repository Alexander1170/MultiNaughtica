using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace Nitrox.Server.Subnautica.Models.Packets.Processors;

[TestClass]
public class EntityMetadataUpdateProcessorTest
{
    [TestMethod]
    public void TheWaterOfABaseIsAcceptedForTheBase()
    {
        EntityMetadataUpdateProcessor.IsBaseEntity(BuildEntity.MakeEmpty()).Should().BeTrue();
    }

    private static WorldEntity LightingPanel(EntityMetadata metadata = null)
    {
        WorldEntity panel = new(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Cyclops"), 0, "classId", false, new NitroxId(), null);
        panel.Metadata = metadata;
        return panel;
    }

    [TestMethod]
    public void ALightPressFromAClientWithAnOldSwitchLeavesTheSwitchOnForEverybody()
    {
        // Bob flipped the Power Share on and the server has it; Alice presses a light before that reached her and reports the whole panel with her old switch
        WorldEntity panel = LightingPanel(new CyclopsLightingMetadata(false, true, true));

        CyclopsLightingMetadata kept = EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(true, true, false));

        kept.PowerShareOn.Should().BeTrue();
        kept.FloodLightsOn.Should().BeTrue();
        panel.Metadata.Should().BeSameAs(kept);
        panel.Metadata.Should().BeOfType<CyclopsLightingMetadata>().Which.PowerShareOn.Should().BeTrue();
    }

    [TestMethod]
    public void TheReporterWithAnOldSwitchIsTheOneWhoMustBeTold()
    {
        // The processor sends the kept state to the reporter too exactly when the kept switch is not the one reported
        WorldEntity panel = LightingPanel(new CyclopsLightingMetadata(false, true, true));
        CyclopsLightingMetadata oldView = new(true, true, false);
        CyclopsLightingMetadata upToDate = new(true, true, true);

        EntityMetadataUpdateProcessor.StoreLighting(panel, oldView).PowerShareOn.Should().NotBe(oldView.PowerShareOn);
        EntityMetadataUpdateProcessor.StoreLighting(panel, upToDate).PowerShareOn.Should().Be(upToDate.PowerShareOn);
    }

    [TestMethod]
    public void ThePressOfTheSwitchChangesTheKeptSwitchAndALightPressAfterItKeepsIt()
    {
        WorldEntity panel = LightingPanel(new CyclopsLightingMetadata(true, true, false));

        EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(true, true, true, CyclopsLightingMetadata.CHANGED_POWER_SHARE)).PowerShareOn.Should().BeTrue();
        EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(true, false, true)).PowerShareOn.Should().BeTrue();
        // an old view that presses a light afterwards cannot take it back
        EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(false, false, false)).PowerShareOn.Should().BeTrue();
        // only the next press of the switch can
        EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(false, false, false, CyclopsLightingMetadata.CHANGED_POWER_SHARE)).PowerShareOn.Should().BeFalse();
    }

    [TestMethod]
    public void APressOfTheSwitchFromAClientWithOldLightsLeavesTheLightsOnForEverybody()
    {
        // Bob put the floodlights on and the server has it; Alice presses the share before that reached her and reports the whole panel with her old lights
        WorldEntity panel = LightingPanel(new CyclopsLightingMetadata(true, true, false));
        CyclopsLightingMetadata oldView = new(false, true, true, CyclopsLightingMetadata.CHANGED_POWER_SHARE);

        CyclopsLightingMetadata kept = EntityMetadataUpdateProcessor.StoreLighting(panel, oldView);

        kept.PowerShareOn.Should().BeTrue("the press is the news");
        kept.FloodLightsOn.Should().BeTrue("the lights are not");
        kept.HasSameStateAs(oldView).Should().BeFalse("so the reporter is told");
    }

    [TestMethod]
    public void APressOnALightFromAClientWithAnOldOtherLightLeavesThatLightAlone()
    {
        WorldEntity panel = LightingPanel(new CyclopsLightingMetadata(true, true, true));

        CyclopsLightingMetadata kept = EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(false, false, true, CyclopsLightingMetadata.CHANGED_INTERNAL));

        kept.InternalLightsOn.Should().BeFalse();
        kept.FloodLightsOn.Should().BeTrue("another player's floodlights press is not undone by an interior lights press");
    }

    [TestMethod]
    public void ThePanelOfAWorldWithoutAStoredStateTakesTheFirstReport()
    {
        WorldEntity panel = LightingPanel();

        EntityMetadataUpdateProcessor.StoreLighting(panel, new CyclopsLightingMetadata(true, false, true)).PowerShareOn.Should().BeTrue();
        panel.Metadata.Should().BeOfType<CyclopsLightingMetadata>().Which.FloodLightsOn.Should().BeTrue();
    }

    [TestMethod]
    public void TheWaterOfABaseIsNotAcceptedForAnythingElse()
    {
        // After the last piece is deconstructed the base is replaced by a ghost with the same id: a flood update that is still on its way must not end up on the ghost
        WorldEntity other = new(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("Quartz"), 0, "classId", false, new NitroxId(), null);

        EntityMetadataUpdateProcessor.IsBaseEntity(other).Should().BeFalse();
    }
}
