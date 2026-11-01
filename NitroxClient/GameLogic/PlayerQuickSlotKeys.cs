using System;
using NitroxClient.GameLogic.VehicleUpgrades;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Gui.Input;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

namespace NitroxClient.GameLogic;

/// <summary>
/// The keys 6, 7 and 8 of the player's quick bar (<see cref="PlayerQuickSlots" />). The game reads the keys 1 to 5 itself (<c>uGUI_QuickSlots.HandleInput</c>, from a static list of five buttons, and hands the
/// number 0 to 4 to <c>IQuickSlots.SlotKeyDown</c>); this does for the three Nitrox key bindings what that loop does for the five: a press hands the number 5, 6 or 7 to the player's quick slots, which select
/// the slot (or, with the PDA open and an item under the cursor, bind the item to it: <c>SlotKeyDown</c> does both, so the keys work in the PDA as the first five do). The game's code decides what a press means, as
/// it does for the first five: while a chat or a menu has the input, the press is ignored there. The vehicles' own sixth key is <see cref="VehicleQuickSlotKeys" />; the same binding, "6", serves a vehicle's sixth
/// module while the player sits in it and the sixth slot of the bar on foot.
/// </summary>
public static class PlayerQuickSlotKeys
{
    private const int MAX_PRESS_LOGS = 12;

    private static readonly bool[] wasHeld = new bool[PlayerQuickSlots.EXTRA_KEYS];
    private static GameInput.Button[] buttons;
    private static bool buttonsBroken;
    private static int pressLogs;

    /// <summary>Called after the game's quick slots have read their keys in a frame, with the quick slots they belong to (the player's own, or a vehicle's).</summary>
    public static void Tick(IQuickSlots target)
    {
        if (!Multiplayer.Active || target is not QuickSlots quickSlots || quickSlots.slotCount < PlayerQuickSlots.COUNT || !Player.main || !TryGetButtons())
        {
            Array.Clear(wasHeld, 0, wasHeld.Length);
            return;
        }
        // (the conditions under which the game's own loop reads the keys 1 to 5 at all)
        if (!Player.main.GetCanItemBeUsed() || uGUI.isIntro || IntroLifepodDirector.IsActive)
        {
            Array.Clear(wasHeld, 0, wasHeld.Length);
            return;
        }

        for (int extra = 0; extra < PlayerQuickSlots.EXTRA_KEYS; extra++)
        {
            bool held = GameInput.GetButtonHeld(buttons[extra]);
            (bool down, _, _) = QuickSlotKeyRules.Transition(wasHeld[extra], held);
            wasHeld[extra] = held;
            if (!down)
            {
                continue;
            }
            int number = PlayerQuickSlots.FIRST_EXTRA_KEY_NUMBER + extra;
            LogPress(quickSlots, number);
            try
            {
                quickSlots.SlotKeyDown(number);
            }
            catch (Exception exception)
            {
                Log.Error(exception, $"[QuickSlots] the key press of quick slot {number + 1} threw");
            }
        }
    }

    private static bool TryGetButtons()
    {
        if (buttons != null)
        {
            return true;
        }
        if (buttonsBroken)
        {
            return false;
        }
        try
        {
            Type[] bindingTypes = [typeof(QuickSlot6KeyBindingAction), typeof(QuickSlot7KeyBindingAction), typeof(QuickSlot8KeyBindingAction)];
            GameInput.Button[] found = new GameInput.Button[PlayerQuickSlots.EXTRA_KEYS];
            for (int i = 0; i < found.Length; i++)
            {
                Type bindingType = bindingTypes[i];
                int index = KeyBindingManager.KeyBindings.FindIndex(binding => binding.GetType() == bindingType);
                if (index < 0)
                {
                    throw new InvalidOperationException($"the key binding {bindingType.Name} is not in the list");
                }
                found[i] = (GameInput.Button)(KeyBindingManager.NITROX_BASE_ID + index);
            }
            buttons = found;
            return true;
        }
        catch (Exception exception)
        {
            buttonsBroken = true;
            Log.Error(exception, "[QuickSlots] the keys 6, 7 and 8 of the quick bar could not be read; they are off for this session");
            return false;
        }
    }

    /// <summary>Says in the log what the first presses of a session reached (the slot, what is bound to it, which slot is selected, whether the PDA is open), because the live checks cannot press the real keys.</summary>
    private static void LogPress(QuickSlots quickSlots, int number)
    {
        if (pressLogs >= MAX_PRESS_LOGS)
        {
            return;
        }
        pressLogs++;
        try
        {
            Log.Info($"[QuickSlots] key {number + 1} pressed: slot {number} of {quickSlots.slotCount} holds {quickSlots.GetSlotBinding(number)}, slot {quickSlots.activeSlot} is selected, PDA in use: {Player.main.GetPDA().isInUse}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[QuickSlots] key {number + 1} pressed: could not be described ({exception.GetType().Name}: {exception.Message})");
        }
    }
}
