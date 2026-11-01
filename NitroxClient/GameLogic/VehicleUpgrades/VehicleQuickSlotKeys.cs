using System;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Gui.Input;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The keys of the quick slots of a vehicle with extra module slots (<see cref="QuickSlotKeyRules" />). The game reads the keys 1 to 5 itself (<c>uGUI_QuickSlots.HandleInput</c>); this adds the sixth, a Nitrox key
/// binding, by doing for it what the game's loop does for the others: the press, the hold and the release go to the vehicle (<c>IQuickSlots.SlotKeyDown</c>, <c>SlotKeyHeld</c>, <c>SlotKeyUp</c> with the number 5).
/// It also says in the log what the keys 5 and 6 reach, for the first presses of a session, because the live checks never pressed the real keys (they called the vehicle directly).
/// </summary>
public static class VehicleQuickSlotKeys
{
    private const int MAX_PRESS_LOGS = 12;

    private static GameInput.Button? sixthButton;
    private static bool buttonBroken;
    private static bool wasHeld;
    private static int pressLogs;

    /// <summary>Called after the game's quick slots have read their keys in a frame, with the vehicle (or the player's own slots) that they belong to.</summary>
    public static void Tick(IQuickSlots target)
    {
        if (!Multiplayer.Active || !VehicleUpgradeSettings.Enabled || target is not Vehicle vehicle || !vehicle || !Player.main || !Player.main.GetCanItemBeUsed())
        {
            wasHeld = false;
            return;
        }
        bool isExosuit = vehicle is Exosuit;
        if (GameInput.GetButtonDown(GameInput.Button.Slot5))
        {
            LogPress(vehicle, isExosuit, 4, "5");
        }
        // (the same guard as the emote key: a chat or a menu that has the input must not take a typed 6 for the key)
        if (!QuickSlotKeyRules.HasSixthModule(isExosuit, vehicle.slotIDs.Length) || !AvatarInputHandler.main || !AvatarInputHandler.main.IsEnabled() || !TryGetButton(out GameInput.Button button))
        {
            wasHeld = false;
            return;
        }

        bool held = GameInput.GetButtonHeld(button);
        (bool down, bool stillHeld, bool up) = QuickSlotKeyRules.Transition(wasHeld, held);
        wasHeld = held;
        if (down)
        {
            LogPress(vehicle, isExosuit, QuickSlotKeyRules.SIXTH_KEY_NUMBER, "6");
            target.SlotKeyDown(QuickSlotKeyRules.SIXTH_KEY_NUMBER);
        }
        else if (stillHeld)
        {
            target.SlotKeyHeld(QuickSlotKeyRules.SIXTH_KEY_NUMBER);
        }
        if (up)
        {
            target.SlotKeyUp(QuickSlotKeyRules.SIXTH_KEY_NUMBER);
        }
    }

    private static bool TryGetButton(out GameInput.Button button)
    {
        button = GameInput.Button.None;
        if (buttonBroken)
        {
            return false;
        }
        try
        {
            int index = KeyBindingManager.KeyBindings.FindIndex(binding => binding is QuickSlot6KeyBindingAction);
            if (index < 0)
            {
                throw new InvalidOperationException("the key binding is not in the list");
            }
            sixthButton ??= (GameInput.Button)(KeyBindingManager.NITROX_BASE_ID + index);
            button = sixthButton.Value;
            return true;
        }
        catch (Exception exception)
        {
            buttonBroken = true;
            Log.Error(exception, "[VehicleUpgrades] the sixth quick slot key could not be read; the key is off for this session");
            return false;
        }
    }

    /// <summary>
    /// Says what a key press of the fifth or sixth slot reaches: the slot, its module, the kind of quick slot and the cooldown the game will read. If the vehicle's arrays are too short or the module is not
    /// one that a key can use, this is where it shows. Only the first presses of a session.
    /// </summary>
    private static void LogPress(Vehicle vehicle, bool isExosuit, int keyNumber, string key)
    {
        if (pressLogs >= MAX_PRESS_LOGS)
        {
            return;
        }
        pressLogs++;
        int index = QuickSlotKeyRules.SlotIndex(isExosuit, keyNumber);
        try
        {
            string[] slots = vehicle.slotIDs;
            string slot = index >= 0 && index < slots.Length ? slots[index] : "none";
            TechType tech = index >= 0 && index < slots.Length && vehicle.modules != null ? vehicle.modules.GetTechTypeInSlot(slot) : TechType.None;
            QuickSlotType type = index >= 0 && index < slots.Length ? vehicle.GetQuickSlotType(index, out _) : QuickSlotType.None;
            string cooldown;
            try
            {
                cooldown = index >= 0 && index < slots.Length ? vehicle.GetQuickSlotCooldown(index).ToString("F2") : "-";
            }
            catch (Exception exception)
            {
                cooldown = $"threw {exception.GetType().Name} (arrays {vehicle.quickSlotCooldown?.Length}/{vehicle.quickSlotTimeUsed?.Length}/{vehicle.quickSlotCharge?.Length}/{vehicle.quickSlotToggled?.Length})";
            }
            Log.Info($"[VehicleUpgrades] quick slot key {key} pressed in {vehicle.name}: slot {index} of {slots.Length} ({slot}) holds {tech}, quick slot type {type}, cooldown {cooldown}");
        }
        catch (Exception exception)
        {
            Log.Warn($"[VehicleUpgrades] quick slot key {key} pressed in {vehicle.name}: could not be described ({exception.GetType().Name}: {exception.Message})");
        }
    }
}
