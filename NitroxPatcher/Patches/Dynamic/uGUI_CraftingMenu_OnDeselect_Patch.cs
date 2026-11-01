using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from lockers of a base or a Cyclops: the menu stops listening to the lockers it started to listen to in <see cref="uGUI_CraftingMenu_Open_Patch" />. <c>OnDeselect</c> is where
/// the menu takes its own subscriptions away; <c>Close(receiver)</c> is called by every crafter that has something to say (an unpowered one with a finished item does it every frame) and
/// does nothing unless the receiver is the menu's own.
/// </summary>
public sealed partial class uGUI_CraftingMenu_OnDeselect_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.OnDeselect());

    public static void Postfix()
    {
        LockerCrafting.OnMenuClosed();
    }
}
