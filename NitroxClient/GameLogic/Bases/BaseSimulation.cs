using System;
using Nitrox.Model.DataStructures;
using NitroxClient.Extensions;
using UnityEngine;

namespace NitroxClient.GameLogic.Bases;

/// <summary>
///     Who lets the machines of a base change the shared state. In vanilla every machine runs on every client; with several players that multiplies what it consumes and
///     produces, so in a synced base only the player who simulates the base (holds its simulation lock, which follows the players, see B12) runs it for real.
/// </summary>
public static class BaseSimulation
{
    /// <summary>
    ///     Harness only (Debug builds): <c>NITROX_HARNESS_B7_OFF=1</c> puts the bioreactor and the reactor back to the vanilla behaviour for the before runs of a live check.
    /// </summary>
    public static bool DisabledByHarness { get; } =
#if DEBUG
        Environment.GetEnvironmentVariable("NITROX_HARNESS_B7_OFF") == "1";
#else
        false;
#endif

    /// <summary>
    ///     This client simulates the base the machine is part of, or the machine is not part of a synced base (nothing to arbitrate: vanilla behaviour).
    /// </summary>
    public static bool RunsHere(Component machine, SimulationOwnership simulationOwnership)
    {
        Base parentBase = machine.GetComponentInParent<Base>();
        if (!parentBase || !parentBase.TryGetNitroxId(out NitroxId baseId))
        {
            return true;
        }

        return simulationOwnership.HasAnyLockType(baseId);
    }
}
