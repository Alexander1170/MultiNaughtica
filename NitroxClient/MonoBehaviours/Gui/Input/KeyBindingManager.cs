using System.Collections.Generic;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings;
using NitroxClient.MonoBehaviours.Gui.Input.KeyBindings.Actions;

namespace NitroxClient.MonoBehaviours.Gui.Input;

public static class KeyBindingManager
{
    public const int NITROX_BASE_ID = 1000;

    public static List<KeyBinding> KeyBindings =
    [
        new ChatKeyBindingAction(),
        new DiscordFocusBindingAction(),
        new PingKeyBindingAction(),
        new BuddyOxygenKeyBindingAction(),
        new EmoteKeyBindingAction(),
        // a binding's button is its place in this list: new ones go at the end
        new QuickSlot6KeyBindingAction(),
        new QuickSlot7KeyBindingAction(),
        new QuickSlot8KeyBindingAction()
    ];
}
