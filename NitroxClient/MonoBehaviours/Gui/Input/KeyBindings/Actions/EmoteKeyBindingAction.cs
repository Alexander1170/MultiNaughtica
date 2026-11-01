using UnityEngine.InputSystem;

namespace NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

/// <summary>
/// The key of the emote wheel (docs/coop/PLAYER_MODELS.md): hold it, move the mouse towards an emote, let go to play it. Like the share key it is a Nitrox key binding, so the options list it ("Emote wheel
/// (hold)") and a player can move it. G is free in the game's default controls. What the key does is polled by <see cref="PlayerEmoteDriver" /> (<c>GameInput.GetButtonDown</c> and
/// <c>GetButtonHeld</c> for this binding's button), so the press itself needs no handler.
/// </summary>
internal sealed class EmoteKeyBindingAction : KeyBinding
{
    public EmoteKeyBindingAction() : base("Nitrox_Settings_Keybind_Emote", "g") { }

    public override void Execute(InputAction.CallbackContext _)
    {
    }
}
