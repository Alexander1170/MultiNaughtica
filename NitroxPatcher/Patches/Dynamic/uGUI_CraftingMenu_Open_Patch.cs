using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from the lockers of a base or a Cyclops: the crafting menu refreshes which recipes are available when the inventory changes (<c>uGUI_CraftingMenu.InventoryChanged</c>), and
/// only listens to the inventory. While the menu of a crafting station of a base or a Cyclops is open, it also listens to the lockers of that sub root (<see cref="LockerCrafting.OnMenuOpened" />),
/// so that a recipe goes dark when the other player takes its ingredients out of a locker.
/// </summary>
public sealed partial class uGUI_CraftingMenu_Open_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.Open(default, default));

    public static void Postfix(uGUI_CraftingMenu __instance)
    {
        LockerCrafting.OnMenuOpened(__instance);
    }
}
