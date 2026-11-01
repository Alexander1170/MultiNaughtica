using System.IO;
using Newtonsoft.Json;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Resources;
using Nitrox.Server.Subnautica.Models.Resources.Core;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities.Spawning;

[TestClass]
public class DataboxOfKnownTechTest
{
    private static readonly HashSet<string> knownTechs = ["Seamoth", "Beacon"];

    private static bool IsKnown(NitroxTechType techType) => knownTechs.Contains(techType.Name);

    [TestMethod]
    public void ADataboxForABlueprintThatIsKnownIsLeftOut()
    {
        PrefabPlaceholderAsset databox = new("databox-class", DataboxTechType: "Seamoth");

        BatchEntitySpawner.IsDataboxOfKnownTech(databox, IsKnown).Should().BeTrue();
    }

    [TestMethod]
    public void ADataboxForABlueprintThatIsNotKnownIsSpawned()
    {
        PrefabPlaceholderAsset databox = new("databox-class", DataboxTechType: "Cyclops");

        BatchEntitySpawner.IsDataboxOfKnownTech(databox, IsKnown).Should().BeFalse();
    }

    [TestMethod]
    public void APlaceholderThatIsNoDataboxIsNeverLeftOut()
    {
        BatchEntitySpawner.IsDataboxOfKnownTech(new PrefabPlaceholderAsset("rock-class"), IsKnown).Should().BeFalse();
        // a placeholder without a tech type name is never left out by this rule (vanilla spawns nothing for a DataboxSpawner whose spawnTechType is None, but the game data has no such spawner: all 19 name one)
        BatchEntitySpawner.IsDataboxOfKnownTech(new PrefabPlaceholderAsset("databox-class", DataboxTechType: ""), IsKnown).Should().BeFalse();
    }

    [TestMethod]
    public void AGroupIsNeverLeftOut()
    {
        BatchEntitySpawner.IsDataboxOfKnownTech(new PrefabPlaceholdersGroupAsset("group-class", []), IsKnown).Should().BeFalse();
    }

    [TestMethod]
    public void TheTechTypeIsKeptInTheCacheFile()
    {
        // The parser's cache is written and read with these settings: a version 5 file without the field must not be mistaken for one with it (CACHE_VERSION was raised), and the field must survive
        JsonSerializer serializer = new() { TypeNameHandling = TypeNameHandling.Auto };
        PrefabPlaceholderAsset databox = new("databox-class", null, new NitroxTransform(NitroxVector3.Zero, NitroxQuaternion.Identity, NitroxVector3.One), "Seamoth");
        PrefabPlaceholdersGroupAsset group = new("group-class", [databox]);

        using StringWriter writer = new();
        serializer.Serialize(writer, group);
        PrefabPlaceholdersGroupAsset loaded = serializer.Deserialize<PrefabPlaceholdersGroupAsset>(new JsonTextReader(new StringReader(writer.ToString())));

        loaded.PrefabAssets.Should().ContainSingle().Which.Should().BeOfType<PrefabPlaceholderAsset>().Which.DataboxTechType.Should().Be("Seamoth");
    }
}
