using System.Runtime.CompilerServices;
using UnityEngine;

namespace NitroxClient.GameLogic;

/// <summary>
/// Remembers, per sulfur plant, whether the saved state of the home reached it, and for how long its holder has been asking to act (<see cref="CrashHomeRules.MayActOnItsNumber" />).
/// </summary>
public static class CrashHomeMetadataGate
{
    private sealed class State
    {
        public bool MetadataApplied;
        public float FirstAsked = -1f;
    }

    private static readonly ConditionalWeakTable<CrashHome, State> states = new();

    /// <summary>
    /// The saved state of the home was applied (<c>CrashHomeMetadataProcessor</c>).
    /// </summary>
    public static void MarkMetadataApplied(CrashHome home)
    {
        states.GetOrCreateValue(home).MetadataApplied = true;
    }

    /// <summary>
    /// Whether the holder of the home may act on its number now. Call it only when the holder is about to act: the first call starts the wait.
    /// </summary>
    public static bool MayActOnItsNumber(CrashHome home)
    {
        State state = states.GetOrCreateValue(home);
        float now = Time.realtimeSinceStartup;
        if (state.FirstAsked < 0f)
        {
            state.FirstAsked = now;
        }

        return CrashHomeRules.MayActOnItsNumber(state.MetadataApplied, now - state.FirstAsked);
    }
}
