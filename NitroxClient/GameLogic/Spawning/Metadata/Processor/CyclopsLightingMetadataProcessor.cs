using NitroxClient.Communication;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using NitroxClient.GameLogic.VehicleUpgrades;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class CyclopsLightingMetadataProcessor : EntityMetadataProcessor<CyclopsLightingMetadata>
{
    private readonly IPacketSender packetSender;

    public CyclopsLightingMetadataProcessor(IPacketSender packetSender)
    {
        this.packetSender = packetSender;
    }

    public override void ProcessMetadata(GameObject gameObject, CyclopsLightingMetadata metadata)
    {
        CyclopsLightingPanel lighting = gameObject.RequireComponentInChildren<CyclopsLightingPanel>(true);

        using (PacketSuppressor<EntityMetadataUpdate>.Suppress())
        {
            SetInternalLighting(lighting, metadata.InternalLightsOn);
            SetFloodLighting(lighting, metadata.FloodLightsOn);
            SetPowerShare(lighting, metadata.PowerShareOn);
        }
    }

    /// <summary>The switch of the Power Share is state only (the share reads it where it runs); the button on the panel follows it by itself.</summary>
    private static void SetPowerShare(CyclopsLightingPanel lighting, bool isOn)
    {
        if (!lighting.cyclopsRoot || !lighting.cyclopsRoot.TryGetNitroxId(out NitroxId cyclopsId))
        {
            Log.Warn($"[VehicleUpgrades] power share switch: the lighting panel's Cyclops has no id yet, the state {(isOn ? "on" : "off")} is not applied");
            return;
        }
        if (PowerShareSwitch.Set(cyclopsId, isOn))
        {
            Log.Info($"[VehicleUpgrades] power share switch of {cyclopsId}: {(isOn ? "on" : "off")} (from the server)");
        }
    }

    private void SetInternalLighting(CyclopsLightingPanel lighting, bool isOn)
    {
        if (lighting.lightingOn == isOn)
        {
            return;
        }

        lighting.lightingOn = !lighting.lightingOn;
        lighting.cyclopsRoot.ForceLightingState(lighting.lightingOn);
        FMODAsset asset = !lighting.lightingOn ? lighting.vn_lightsOff : lighting.vn_lightsOn;
        FMODUWE.PlayOneShot(asset, lighting.transform.position, 1f);
        lighting.UpdateLightingButtons();
    }

    private void SetFloodLighting(CyclopsLightingPanel lighting, bool isOn)
    {
        if (lighting.floodlightsOn == isOn)
        {
            return;
        }

        lighting.floodlightsOn = !lighting.floodlightsOn;
        lighting.SetExternalLighting(lighting.floodlightsOn);
        FMODAsset asset = !lighting.floodlightsOn ? lighting.vn_floodlightsOff : lighting.vn_floodlightsOn;
        FMODUWE.PlayOneShot(asset, lighting.transform.position, 1f);
        lighting.UpdateLightingButtons();
    }
}
