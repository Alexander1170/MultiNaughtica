namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The arithmetic of the quick slot keys of a vehicle with extra module slots (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots"), as plain numbers. The game has five quick slot keys (1 to 5, <c>GameInput.Button.Slot1</c> to
/// <c>Slot5</c>, read by <c>uGUI_QuickSlots.HandleInput</c>, which hands the number 0 to 4 to <c>IQuickSlots.SlotKeyDown</c>) and no sixth, so the sixth module slot has a Nitrox key of its own, "6" unless the player moves it.
/// </summary>
public static class QuickSlotKeyRules
{
    /// <summary>
    /// The number that the sixth key hands to <c>IQuickSlots.SlotKeyDown</c>, <c>SlotKeyHeld</c> and <c>SlotKeyUp</c>: the keys 1 to 5 are 0 to 4. A Seamoth takes it as the index in its list of slots (its sixth), a
    /// Prawn Suit adds its two arms to it (<c>Exosuit.SlotKeyDown</c> asks for the slot <c>number + 2</c>), which is its sixth module.
    /// </summary>
    public const int SIXTH_KEY_NUMBER = 5;

    /// <summary>The names of the slots of a Prawn Suit start with its two arms, so its list has two more entries than it has modules.</summary>
    public const int EXOSUIT_ARMS = 2;

    /// <summary>The number of module slots that the sixth key needs to exist.</summary>
    public const int SIXTH_MODULE = 6;

    /// <summary>
    /// Whether a vehicle whose list holds <paramref name="slotCount" /> slots has a sixth module: a Seamoth with both extra slots (its list has six names), a Prawn Suit with both (two arms and six modules). With
    /// fewer extra slots the key does nothing, because there is no slot to press.
    /// </summary>
    public static bool HasSixthModule(bool isExosuit, int slotCount)
    {
        return slotCount >= SIXTH_MODULE + (isExosuit ? EXOSUIT_ARMS : 0);
    }

    /// <summary>
    /// What the state of a key in this frame means, from its state in the last one: it started to be held (the key press, <c>SlotKeyDown</c>), it goes on being held (<c>SlotKeyHeld</c>, which only a chargeable
    /// module uses) or it was let go (<c>SlotKeyUp</c>). It is the game's own reading of the five keys, for a key that is polled as "held" only (the buttons that Nitrox adds answer <c>GetButtonHeld</c>).
    /// </summary>
    public static (bool Down, bool Held, bool Up) Transition(bool wasHeld, bool isHeld)
    {
        return (isHeld && !wasHeld, isHeld && wasHeld, !isHeld && wasHeld);
    }

    /// <summary>
    /// The index in the vehicle's list of slots that the key <paramref name="keyNumber" /> (0 to 5) reaches: the same for a Seamoth, two more for a Prawn Suit (its arms come first). Negative when the key is
    /// not one.
    /// </summary>
    public static int SlotIndex(bool isExosuit, int keyNumber)
    {
        if (keyNumber < 0 || keyNumber > SIXTH_KEY_NUMBER)
        {
            return -1;
        }
        return keyNumber + (isExosuit ? EXOSUIT_ARMS : 0);
    }
}
