namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// When the hologram of the build room may be set up and may stream terrain (<see cref="NitroxClient.MonoBehaviours.Cyclops.CyclopsTerrainSonar" />): not before the join is finished and the world has settled, and not while a
/// loading screen or the intro shows. The game's hologram asks the world streamer for chunks of terrain, and a join that is still waiting for the world to be idle (<c>Terrain.WaitForWorldLoad</c> waits for
/// <c>LargeWorldStreamer.IsWorldSettled</c>) must not have something new asking for chunks, or setting up objects in the Cyclops that is being spawned: in the play-test of Build 3 the world did not settle for 60 s and the
/// screen stayed black. As far as the log shows no hologram had been built then, so the gate is a precaution (nothing of the hologram exists until it opens), and the log says when it opens.
/// </summary>
public static class HologramStartRules
{
    /// <summary>Null when the hologram may start; otherwise why it has to wait, in words for the log.</summary>
    public static string WaitingFor(bool multiplayerActive, bool initialSyncCompleted, bool introShowing, bool loadingScreenShowing, bool worldSettled)
    {
        if (!multiplayerActive)
        {
            return "no session is running";
        }
        if (!initialSyncCompleted)
        {
            return "the join is not finished";
        }
        if (introShowing)
        {
            return "the intro is showing";
        }
        if (loadingScreenShowing)
        {
            return "a loading screen is showing";
        }
        if (!worldSettled)
        {
            return "the world has not settled";
        }
        return null;
    }
}
