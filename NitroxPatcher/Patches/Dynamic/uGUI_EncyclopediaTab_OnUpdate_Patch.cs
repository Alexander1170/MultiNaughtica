using System.Reflection;
using NitroxClient.GameLogic.HUD;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// item b, the "Clear all" button of the databank (<see cref="PdaClearAllButton" />): every frame that the databank tab is the shown one it shows the button when there is a badge to clear
/// and hides it when there is none, and places it behind the header text the first time after the PDA opened. Costs one <c>notificationsCount</c> (the count that the PDA polls for the toolbar
/// anyway) while the databank is shown, and nothing while the PDA is closed (the PDA does not call <c>OnUpdate</c> then). Never makes the button itself.
/// </summary>
public sealed partial class uGUI_EncyclopediaTab_OnUpdate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_EncyclopediaTab t) => t.OnUpdate(default));

    public static void Postfix(uGUI_EncyclopediaTab __instance, bool isOpen)
    {
        PdaClearAllButton.OnUpdate(__instance, isOpen);
    }
}
