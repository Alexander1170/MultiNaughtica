using System;

namespace NitroxClient.GameLogic;

/// <summary>
/// What a received <c>SealedDoorMetadata</c> does to a <c>Sealed</c> object (a laser-cut door, a sealed supply crate), bug 10 of the first play sessions (the design notes (not part of this repository) 6.1 row 11).
/// Plain data, no game types.
/// <para>
/// The facts behind the rules (vanilla IL, ildump): <c>Sealed._sealed</c> starts true and only <c>Sealed.Weld</c> ever clears it, together with <c>openedAmount == maxOpenedAmount</c>
/// (cutting is one way: nothing seals a cut door again); <c>openedAmount</c> only grows. The server keeps ONE metadata per entity and sends it again to every joiner and after every
/// restart; a never-touched door has no metadata at all (the Test world of the first play sessions: 13 of its 15 sealed doors), so nothing is applied to it and it keeps the prefab's
/// sealed state. The metadata of a door that somebody cut or is cutting carries an absolute state, which can arrive late or twice (two players cutting the same door, a resend
/// after a cell woke up), so applying it must never take a door backwards.
/// </para>
/// </summary>
public static class SealedDoorRules
{
    /// <param name="Sealed">The state the object gets.</param>
    /// <param name="OpenedAmount">The cut progress the object gets (the maximum for an opened door).</param>
    /// <param name="OpenAtOnce">The object was made a moment ago and the door is cut open: do what the vanilla load of a cut door does (<c>LaserCutObject.OnEnable</c> with
    /// <c>isCutOpen</c>: open at once, drop the loose cut-out piece) instead of letting <c>LaserCutObject.Update</c> play the opening in front of a player who just arrived.</param>
    /// <param name="ShowCutFx">Somebody else is cutting right now (the progress grew while the door is in the world): show the cutting beam on this client, for the second the vanilla
    /// FX lasts. Never for a door that is only being restored.</param>
    public readonly record struct Result(bool Sealed, float OpenedAmount, bool OpenAtOnce, bool ShowCutFx);

    /// <param name="currentSealed">The state of the object on this client now (a fresh object: the prefab's, sealed).</param>
    /// <param name="currentOpened">The cut progress of the object now.</param>
    /// <param name="maxOpened">The progress at which the door opens (<c>Sealed.maxOpenedAmount</c>, 0 when unknown).</param>
    /// <param name="incomingSealed">The state in the metadata.</param>
    /// <param name="incomingOpened">The progress in the metadata.</param>
    /// <param name="freshlySpawned">The object was spawned in this frame, its <c>Start</c> methods have not run yet (a joiner, a restart, a cell that woke up).</param>
    public static Result Apply(bool currentSealed, float currentOpened, float maxOpened, bool incomingSealed, float incomingOpened, bool freshlySpawned)
    {
        float current = Normalize(currentOpened);
        float incoming = Normalize(incomingOpened);

        // Cutting only goes forward: an open door stays open (a late or repeated "still sealed" must not close it), the progress never shrinks
        bool sealedNow = currentSealed && incomingSealed;
        float opened = Math.Max(current, incoming);
        if (maxOpened > 0f)
        {
            // Math.Clamp does not exist on net472 (the game's runtime)
            opened = sealedNow ? Math.Min(opened, maxOpened) : maxOpened;
        }

        bool openAtOnce = !sealedNow && freshlySpawned;
        bool showCutFx = sealedNow && !freshlySpawned && opened > current;
        return new Result(sealedNow, opened, openAtOnce, showCutFx);
    }

    private static float Normalize(float amount)
    {
        return float.IsNaN(amount) || amount < 0f ? 0f : amount;
    }
}
