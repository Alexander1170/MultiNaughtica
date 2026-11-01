using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Crafting from lockers of a base or a Cyclops: a craft that needs the lockers starts after the click, when the server has handed the items over, so the menu has to be put into its
/// "a craft is running" state afterwards (<see cref="LockerCrafting.OnMenuAction" /> remembers the recipe that was clicked).
/// </summary>
public sealed partial class uGUI_CraftingMenu_Action_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_CraftingMenu t) => t.Action(default));

    public static void Prefix(uGUI_CraftingMenu.Node sender)
    {
        LockerCrafting.OnMenuAction(sender);
    }
}
