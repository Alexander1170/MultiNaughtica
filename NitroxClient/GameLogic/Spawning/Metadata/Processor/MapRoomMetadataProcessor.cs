using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using NitroxClient.Communication;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class MapRoomMetadataProcessor : EntityMetadataProcessor<MapRoomMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, MapRoomMetadata metadata)
    {
        if (!gameObject.TryGetComponent(out MapRoomFunctionality mapRoom))
        {
            Log.Error($"[{nameof(MapRoomMetadataProcessor)}] Could not find MapRoomFunctionality on {gameObject.name}");
            return;
        }

        TechType typeToScan = metadata.TypeToScan.ToUnity();
        // StartScanning starts the scan over (nodes found, time of the last scan): only when the type really is another one
        if (mapRoom.typeToScan == typeToScan)
        {
            return;
        }

        // The patch on StartScanning would send what was just received back
        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        {
            mapRoom.StartScanning(typeToScan);
        }
    }
}
