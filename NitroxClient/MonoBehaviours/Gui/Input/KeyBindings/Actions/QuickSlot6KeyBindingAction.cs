using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
/// The key of the sixth module slot of a Seamoth or a Prawn Suit (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots") and of the sixth slot of the player's own quick bar (docs/coop/VEHICLE_UPGRADES.md, "Eight quick
/// slots for the player"): the game has the keys 1 to 5 for the quick slots and none for a sixth. Like the emote key it is a Nitrox key binding, so the options list it and a player can move it; "6" is free in the
/// game's default controls. What it does is polled by <see cref="NitroxClient.GameLogic.VehicleUpgrades.VehicleQuickSlotKeys" /> while the player sits in a vehicle and by
/// <see cref="NitroxClient.GameLogic.PlayerQuickSlotKeys" /> on foot (<c>GameInput.GetButtonHeld</c> for this binding's button), so the press itself needs no handler. It must stay in its place in
/// <see cref="KeyBindingManager.KeyBindings" />: a binding's button is its place in the list.
/// </summary>
internal sealed class QuickSlot6KeyBindingAction : KeyBinding
{
    public QuickSlot6KeyBindingAction() : base("Nitrox_Settings_Keybind_QuickSlot6", "6") { }

    public override void Execute(InputAction.CallbackContext _)
    {
    }
}
