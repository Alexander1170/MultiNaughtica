#if DEBUG
using NitroxClient.MonoBehaviours.Cyclops;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

// Live check of the helm HUD of the Cyclops (the "+" of the solar charger and the power share button, coop.13). Debug builds only. Registered by HarnessConsoleCommands.LiveBatch.cs, run with the server's
// "/console <player> vuhud [state|share]". One line "[SyncHarness] vuhud: ... key=value ..." (see CyclopsHelmHud.DebugState): what the HUD of the nearest real Cyclops has built and shows, the power share switch, whether the "+"
// shows and what decides it (the Cyclops Solar Charger modules, the depth, the light of the day, the power and its capacity, survival mode). "share" clicks the HUD's power share button first.
public partial class HarnessConsoleCommands
{
    private void LiveCommand_vuhud(NotificationCenter.Notification n)
    {
        string what = LiveArgString(n, 0, "state");
        CyclopsHelmHud nearest = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        foreach (CyclopsHelmHud candidate in FindObjectsOfType<CyclopsHelmHud>())
        {
            SubRoot sub = candidate.GetComponent<SubRoot>();
            if (!sub || !VuIsRealCyclops(sub))
            {
                continue;
            }
            float distance = Vector3.Distance(from, candidate.transform.position);
            if (distance < best)
            {
                best = distance;
                nearest = candidate;
            }
        }
        if (!nearest)
        {
            Log.Warn($"{VU}vuhud: no Cyclops with the helm HUD component");
            return;
        }
        if (what == "share")
        {
            bool clicked = nearest.DebugClickShare();
            Log.Info($"{VU}vuhud: share clicked={clicked} {nearest.DebugState()}");
            return;
        }
        Log.Info($"{VU}vuhud: state {nearest.DebugState()}");
    }
}
#endif
