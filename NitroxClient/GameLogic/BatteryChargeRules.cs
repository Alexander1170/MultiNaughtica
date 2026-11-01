using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// When a change of the charge of a battery is sent to the other players (V0, battery charge). Plain data, no game types.
/// </summary>
public static class BatteryChargeRules
{
    /// <summary>
    /// A battery changes its charge many times per second; it is sent when the whole number changes and when it runs empty. The value that is sent is the one the battery has
    /// <b>after</b> the change (it used to be the one from before, which is always just above or just below the whole number: the last unit never arrived, and two clients that both
    /// changed the battery kept putting each other's stale value back, so a battery froze at a whole number).
    /// </summary>
    /// <param name="oldCharge">The charge before the change.</param>
    /// <param name="newCharge">The charge after the change, already limited to the capacity.</param>
    public static bool ShouldSend(float oldCharge, float newCharge)
    {
        bool wholeNumberChanged = Math.Floor(oldCharge) != Math.Floor(newCharge);
        // Battery.IsDepleted is "charge == 0": the step that empties the battery has to arrive even when it stays within the last whole number
        bool becameEmpty = newCharge <= 0f && oldCharge > 0f;
        return wholeNumberChanged || becameEmpty;
    }
}
