using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Server.Subnautica.Models.Serialization;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Players;

/// <summary>
/// The world "Test" was saved by coop.3 and must keep loading with every later build (CoopBuild 4 and on): this is a PlayerData.json player of that save (name changed, the list of
/// goals cut to three entries), read through the same serializer the server uses. New runtime-only state of <see cref="Player" /> (the intro flag of the join rules) is not part of it.
/// </summary>
[TestClass]
public class PlayerDataCoop3Test
{
    private const string COOP3_PLAYER_DATA = """{"Players":[{"Name":"Alice","UsedItems":["FireExtinguisher","Flare","GarryFish","Bladderfish","Scanner","Knife","SmallStorage","Seaglide","Boomerang","Builder","Beacon","Constructor","Welder","LaserCutter","PosterKitty","PosterExoSuit2","HeatBlade","Floater"],"QuickSlotsBindingIds":[{"value":"1b185b05-22f0-4d68-8851-911d8fbf34ad"},{"value":"eaf42efd-4559-4ed6-a93b-8e00454b6be0"},{"value":"60f03798-2bc1-41ce-8515-040439170025"},{"value":"2774d48b-ba40-45b7-9c00-56499812b11e"},{"value":"62c2c317-3a62-6483-2253-bc402a872140"}],"EquippedItems":[{"Key":"Head","Value":"de417450-1f1c-48c0-95e9-692c1f4602e7"},{"Key":"Body","Value":"8e48188f-9e2f-4b15-b819-e9ab6bece633"},{"Key":"Gloves","Value":"7290511c-9cd5-43cb-bd84-42ec446d7e41"},{"Key":"Foots","Value":"dfc87ebd-9d57-4437-910b-1d39ec488507"},{"Key":"Chip1","Value":"e449e799-f552-4e27-9206-707a136c020d"},{"Key":"Tank","Value":"c1a84e54-8ee2-4720-a50f-301ea165e816"}],"Id":1,"SpawnPosition":{"X":-189.61961,"Y":3.6745653,"Z":-325.23257},"SpawnRotation":{"X":-0.0,"Y":0.9999183,"Z":-0.0,"W":-0.01278513},"CurrentStats":{"Oxygen":135.0,"MaxOxygen":135.0,"Health":93.21895,"Food":96.92631,"Water":70.6969,"InfectionAmount":0.75},"GameMode":"SURVIVAL","SubRootId":"49b17f49-05af-4794-a20d-c366de86ba9d","Permissions":"ADMIN","NitroxId":"6640027c-a4f3-4231-aec1-d6f0bc90bd98","IsPermaDeath":false,"PersonalCompletedGoalsWithTimestamp":[{"Key":"Pickup_FireExtinguisher","Value":564.94366},{"Key":"FireExtinguisher","Value":564.94366},{"Key":"Equip_FireExtinguisher","Value":580.546}],"PlayerPreferences":{"PingPreferences":[{"Key":"Signal_Lifepod19a","Value":{"Color":0,"Visible":false}},{"Key":"a223b872-452e-4a4f-b318-a69b4b7315ca","Value":{"Color":2,"Visible":true}},{"Key":"Signal_Lifepod13","Value":{"Color":0,"Visible":false}},{"Key":"21e7aa3b-cd09-4a83-84b7-47c51ab4535c","Value":{"Color":0,"Visible":true}},{"Key":"6cae29ff-c4d5-4ee2-9319-ae12d93f4bde","Value":{"Color":0,"Visible":false}},{"Key":"Signal_Lifepod17a","Value":{"Color":0,"Visible":false}},{"Key":"Signal_Lifepod19","Value":{"Color":0,"Visible":false}},{"Key":"Signal_AuroraRendezvous","Value":{"Color":0,"Visible":false}}],"PinnedTechTypes":[518]},"InPrecursor":false,"DisplaySurfaceWater":true}]}""";

    private static PlayerData Load()
    {
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        return serializer.Deserialize<PlayerData>(new MemoryStream(Encoding.UTF8.GetBytes(COOP3_PLAYER_DATA)));
    }

    [TestMethod]
    public void APlayerDataFileOfCoop3Loads()
    {
        PlayerData loaded = Load();

        loaded.Players.Should().ContainSingle();
        PersistedPlayerData player = loaded.Players[0];
        player.Name.Should().Be("Alice");
        player.SubRootId.Should().NotBeNull("the player left inside a base");
        player.PlayerPreferences.Should().NotBeNull();
        player.PlayerPreferences.PingPreferences.Should().HaveCount(8);
        player.PersonalCompletedGoalsWithTimestamp.Should().HaveCount(3);
    }

    [TestMethod]
    public void ThePlayerOfCoop3BecomesARuntimePlayerWithoutTheNewFlags()
    {
        Player player = Load().GetPlayers().Single();

        player.Name.Should().Be("Alice");
        player.IntroUnfinished.Should().BeFalse("it is runtime-only and a save never has it");
        player.PingInstancePreferences.TryGetValue("Signal_Lifepod13", out PingInstancePreference preference).Should().BeTrue();
        preference.Visible.Should().BeFalse();
    }

    [TestMethod]
    public void TheSaveFormatOfAPlayerIsUnchanged()
    {
        // Written again, the player has exactly the keys coop.3 wrote: nothing new, nothing gone (a new key would be fine for a loader, but the file would then not load with an older server)
        ServerJsonSerializer serializer = new(NullLogger<ServerJsonSerializer>.Instance);
        using MemoryStream stream = new();
        serializer.Serialize(stream, Load());
        stream.Flush();
        string written = Encoding.UTF8.GetString(stream.ToArray());

        foreach (string key in new[] { "Name", "UsedItems", "QuickSlotsBindingIds", "EquippedItems", "Id", "SpawnPosition", "SpawnRotation", "CurrentStats", "GameMode", "SubRootId", "Permissions", "NitroxId", "IsPermaDeath", "PersonalCompletedGoalsWithTimestamp", "PlayerPreferences", "InPrecursor", "DisplaySurfaceWater" })
        {
            written.Should().Contain("\"" + key + "\"", "coop.3 wrote the key " + key);
        }
        written.Should().NotContain("IntroUnfinished");
    }
}
