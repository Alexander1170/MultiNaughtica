namespace Nitrox.Model.Configuration;

[TestClass]
public class SubnauticaServerOptionsTest
{
    [TestMethod]
    public void ANewWorldOpensNoPortAndAnswersNoBroadcast()
    {
        // A launcher-created world of the co-op fork takes these defaults: UPnP asks the router of the host to open a port, LAN discovery answers broadcasts of the network
        SubnauticaServerOptions options = new();

        options.PortForward.Should().BeFalse();
        options.LanDiscovery.Should().BeFalse();
    }

    [TestMethod]
    public void ANewWorldAutosavesEveryTwoMinutes()
    {
        // The Debug server overrides this with 0; the zip is a Release build and has to keep the default
        new SubnauticaServerOptions().SaveInterval.Should().Be(120_000);
    }
}
