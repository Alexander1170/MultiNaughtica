using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
/// The key of the eighth slot of the player's quick bar (docs/coop/VEHICLE_UPGRADES.md, "Eight quick slots for the player"), the last of the three that Nitrox adds to the game's five: see
/// <see cref="QuickSlot7KeyBindingAction" />. "8" is free in the game's default controls. It must stay at the end of <see cref="KeyBindingManager.KeyBindings" />: a binding's button is its place in the list.
/// </summary>
internal sealed class QuickSlot8KeyBindingAction : KeyBinding
{
    public QuickSlot8KeyBindingAction() : base("Nitrox_Settings_Keybind_QuickSlot8", "8") { }

    public override void Execute(InputAction.CallbackContext _)
    {
    }
}
