using System.Collections.Generic;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The on/off switch of the Cyclops Power Share: a button on the Cyclops lighting panel, next to the lights. The forward share (a Cyclops charging the Seamoths and Prawn Suits around it) runs only while
/// the Cyclops' switch is on; it is off by default, so that a Cyclops with the module does not spend its power on its own. The emergency power from a docked vehicle is not switched and stays automatic.
/// <para>
/// The state of each Cyclops lives here, by the Cyclops' id (so that it can arrive before the Cyclops' components exist, with the metadata of the lighting panel at the initial sync), and travels like the
/// state of the lights: it is part of the <see cref="Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata.CyclopsLightingMetadata" />, which every client applies and the server saves with the world.
/// </para>
/// </summary>
public static class PowerShareSwitch
{
    private static readonly HashSet<NitroxId> switchedOn = [];

    /// <summary>The Cyclops whose switch is on.</summary>
    public static int Count => switchedOn.Count;

    public static bool IsOn(NitroxId cyclopsId) => cyclopsId != null && switchedOn.Contains(cyclopsId);

    public static bool IsOn(SubRoot cyclops) => cyclops && cyclops.TryGetNitroxId(out NitroxId id) && IsOn(id);

    /// <summary>Sets the switch of a Cyclops. Returns true when the state changed.</summary>
    public static bool Set(NitroxId cyclopsId, bool on)
    {
        if (cyclopsId == null)
        {
            return false;
        }
        return on ? switchedOn.Add(cyclopsId) : switchedOn.Remove(cyclopsId);
    }

    /// <summary>The session ended: the switches of this session are not the next session's.</summary>
    public static void Clear()
    {
        switchedOn.Clear();
    }

    /// <summary>A press of the button (or the live check's stand-in for it): turns the switch of the panel's Cyclops over and tells everybody, like a press on a light button does.</summary>
    public static void Press(CyclopsLightingPanel panel)
    {
        SubRoot cyclops = panel ? panel.cyclopsRoot : null;
        if (!cyclops || !cyclops.TryGetNitroxId(out NitroxId cyclopsId))
        {
            return;
        }
        if (!PowerShare.IsAvailable(cyclops))
        {
            Log.InGame(Language.main.Get("Nitrox_PowerShareUnavailable"));
            return;
        }

        bool on = !IsOn(cyclopsId);
        Set(cyclopsId, on);
        Log.InGame(Language.main.Get(on ? "Nitrox_PowerShareSwitchedOn" : "Nitrox_PowerShareSwitchedOff"));
        Log.Info($"[VehicleUpgrades] power share switch of {cyclopsId}: {(on ? "on" : "off")}");

        if (panel.TryGetIdOrWarn(out NitroxId panelId))
        {
            // The report of this press says that the press changed the switch; the lights in it are only this client's view (the server keeps its own, see CyclopsLightingMetadata.Merge)
            NitroxServiceLocator.LocateService<Entities>().BroadcastMetadataUpdate(panelId, new CyclopsLightingMetadata(panel.floodlightsOn, panel.lightingOn, on, CyclopsLightingMetadata.CHANGED_POWER_SHARE));
        }
    }
}
