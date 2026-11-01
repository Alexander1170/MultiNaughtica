using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
/// The key of the seventh slot of the player's quick bar (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots for the player"): the game has the keys 1 to 5 for the five quick slots it has, and Nitrox adds three slots and
/// the keys 6, 7 and 8 for them. Like the emote key it is a Nitrox key binding, so the options list it and a player can move it; "7" is free in the game's default controls. What it does is polled by
/// <see cref="NitroxClient.GameLogic.PlayerQuickSlotKeys" /> (<c>GameInput.GetButtonHeld</c> for this binding's button), so the press itself needs no handler. It must stay in its place in
/// <see cref="KeyBindingManager.KeyBindings" />: a binding's button is its place in the list.
/// </summary>
internal sealed class QuickSlot7KeyBindingAction : KeyBinding
{
    public QuickSlot7KeyBindingAction() : base("Nitrox_Settings_Keybind_QuickSlot7", "7") { }

    public override void Execute(InputAction.CallbackContext _)
    {
    }
}
