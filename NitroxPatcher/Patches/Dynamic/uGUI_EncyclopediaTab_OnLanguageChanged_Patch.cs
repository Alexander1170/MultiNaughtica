using System.Reflection;
using NitroxClient.GameLogic.HUD;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// item b, the "Clear all" button of the databank (<see cref="PdaClearAllButton" />): the label follows a change of the game's language, and the button is placed anew (the width of the
/// label changes).
/// </summary>
public sealed partial class uGUI_EncyclopediaTab_OnLanguageChanged_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_EncyclopediaTab t) => t.OnLanguageChanged());

    public static void Postfix()
    {
        PdaClearAllButton.OnLanguageChanged();
    }
}
