using Story;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// The Precursor door roots (a door, its key terminals and the global key activator) that the game restores when the root is enabled: a terminal that is marked used opens the
/// doors of its root (<c>PrecursorKeyTerminal.OnEnable</c>), and an activator whose global key is complete opens them too (<c>PrecursorGlobalKeyActivator.OnEnable</c>).
/// Nitrox spawns the children of such a root after the root is enabled and applies their state after that, so both ran before there was a door or a marked terminal and
/// nothing opened the door again: a door whose own state was lost (a key inserted just before a crash) stayed shut for good, and the partner door of a root that
/// was not loaded when the global key was used stayed shut although its key was complete.
/// </summary>
public static class PrecursorDoorRoots
{
    /// <summary>
    /// Runs the two restores again once all the children of the group exist. Opening an open door does nothing.
    /// </summary>
    public static void ReopenAfterSpawn(GameObject groupObject)
    {
        foreach (PrecursorKeyTerminal terminal in groupObject.GetComponentsInChildren<PrecursorKeyTerminal>(false))
        {
            if (terminal.slotted)
            {
                terminal.BroadcastDoorsOpen();
            }
        }

        StoryGoalManager goalManager = StoryGoalManager.main;
        if (!goalManager)
        {
            return;
        }

        foreach (PrecursorGlobalKeyActivator activator in groupObject.GetComponentsInChildren<PrecursorGlobalKeyActivator>(false))
        {
            if (!string.IsNullOrEmpty(activator.doorActivationKey) && goalManager.IsGoalComplete(activator.doorActivationKey))
            {
                activator.BroadcastMessage("ToggleDoor", true, SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
