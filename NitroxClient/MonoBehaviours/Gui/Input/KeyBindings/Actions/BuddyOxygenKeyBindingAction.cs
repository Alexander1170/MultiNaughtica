using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
/// The key that gives air to a teammate (docs/coop/BUDDY_OXYGEN.md): hold it while looking at them. Like the ping it is a Nitrox key binding, so the options list it ("Share air with a teammate (hold)") and a
/// player can move it. B is free in the game's default controls. What the key does is polled while it is held (<c>BuddyOxygenShare.Tick</c> asks <c>GameInput.GetButtonHeld</c> for this binding's button), so
/// the press itself needs no handler.
/// </summary>
internal sealed class BuddyOxygenKeyBindingAction : KeyBinding
{
    public BuddyOxygenKeyBindingAction() : base("Nitrox_Settings_Keybind_BuddyOxygen", "b") { }

    public override void Execute(InputAction.CallbackContext _)
    {
    }
}
