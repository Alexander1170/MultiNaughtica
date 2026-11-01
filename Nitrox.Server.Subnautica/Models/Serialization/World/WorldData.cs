using System.Collections.Generic;
using Nitrox.Model.DataStructures;
using Nitrox.Server.Subnautica.Models.GameLogic;

namespace Nitrox.Server.Subnautica.Models.Serialization.World;

internal class WorldData
{
    public List<NitroxInt3>? ParsedBatchCells { get; set; } = [];

    public GameData? GameData { get; set; }

    /// <summary>
    ///     The version of the world repair (<see cref="WorldRepair" />) this world has been through; 0 for a world that was saved before the repair existed (coop.3). The repair runs once per
    ///     world and version. A server that does not know the property ignores it when it reads the file, so a world saved by this build still loads in coop.3.
    /// </summary>
    public int RepairVersion { get; set; }

    public bool IsValid()
    {
        return ParsedBatchCells != null &&
               GameData != null;
    }
}
