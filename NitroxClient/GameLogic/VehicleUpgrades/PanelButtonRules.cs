using System;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The arithmetic of the two buttons that the Cyclops lighting panel gets next to its own two (the power share switch and the tow hook, see <c>CyclopsPanelButtons</c>), as plain numbers so that it can be
/// tested: where the next buttons of the row go, and how the buttons are told apart when they are lit and when they are not.
/// </summary>
public static class PanelButtonRules
{
    /// <summary>The gap between two buttons, in widths of a button, when the panel does not show how far apart its own two are.</summary>
    public const float FALLBACK_STEP_IN_WIDTHS = 1.2f;

    /// <summary>Two buttons closer than this (in the panel's units) are taken for one: the step is not known.</summary>
    public const float MIN_STEP = 0.001f;

    /// <summary>
    /// The step from one button to the next in the row that the panel's two buttons make: the way from the first to the second. When the two stand in the same place (or the panel has only one) the step
    /// is a gap to the right.
    /// </summary>
    public static (float X, float Y) Step(float firstX, float firstY, float secondX, float secondY, float width)
    {
        float stepX = secondX - firstX;
        float stepY = secondY - firstY;
        if (float.IsNaN(stepX) || float.IsNaN(stepY) || (Math.Abs(stepX) < MIN_STEP && Math.Abs(stepY) < MIN_STEP))
        {
            return (Math.Max(width, 0f) * FALLBACK_STEP_IN_WIDTHS, 0f);
        }
        return (stepX, stepY);
    }

    /// <summary>Where the button <paramref name="index" /> places after the panel's second one goes (1 is the first new button, 2 the next): the second button's place plus that many steps.</summary>
    public static (float X, float Y) Slot(float secondX, float secondY, (float X, float Y) step, int index)
    {
        int n = Math.Max(index, 1);
        return (secondX + step.X * n, secondY + step.Y * n);
    }

    /// <summary>
    /// The slots of the buttons that are shown, in order: a button that is not wanted (its module is not installed) leaves no gap. Returns the slot of each wanted button, or 0 for one that is not wanted.
    /// </summary>
    public static int[] AssignSlots(params bool[] wanted)
    {
        int[] slots = new int[wanted.Length];
        int next = 1;
        for (int i = 0; i < wanted.Length; i++)
        {
            slots[i] = wanted[i] ? next++ : 0;
        }
        return slots;
    }

    /// <summary>The brightness (0 to 1) of a button's icon: bright when the thing it controls is on, dim when it is off.</summary>
    public static float IconBrightness(bool lit) => lit ? 1f : 0.55f;

    /// <summary>The opacity of a button's icon: a button that is off is also a little see-through, so that it is clear at a glance which of the buttons are on.</summary>
    public static float IconOpacity(bool lit) => lit ? 1f : 0.6f;
}
