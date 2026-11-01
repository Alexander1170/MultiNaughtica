using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor.Abstract;
using UnityEngine;

namespace NitroxClient.GameLogic.Spawning.Metadata.Processor;

public class KeypadMetadataProcessor : EntityMetadataProcessor<KeypadMetadata>
{
    public override void ProcessMetadata(GameObject gameObject, KeypadMetadata metadata)
    {
        Log.Debug($"Received keypad metadata change for {gameObject.name} with data of {metadata}");

        // The consoles can be on children of the object that carries the id (a keypad with two consoles, an unlock button on the inside)
        KeypadDoorConsole[] keypads = gameObject.GetComponentsInChildren<KeypadDoorConsole>(true);
        KeypadDoorConsoleUnlock[] unlockButtons = gameObject.GetComponentsInChildren<KeypadDoorConsoleUnlock>(true);
        foreach (KeypadDoorConsole keypad in keypads)
        {
            keypad.unlocked = metadata.Unlocked;
        }

        if (!metadata.Unlocked)
        {
            return;
        }

        foreach (KeypadDoorConsole keypad in keypads)
        {
            if (keypad.root)
            {
                keypad.root.BroadcastMessage("UnlockDoor");
            }
            else
            {
                keypad.BroadcastMessage("UnlockDoor");
            }

            keypad.UnlockDoor();
        }

        foreach (KeypadDoorConsoleUnlock unlockButton in unlockButtons)
        {
            if (!unlockButton.unlocked)
            {
                unlockButton.UnlockDoor();
            }
        }
    }
}
