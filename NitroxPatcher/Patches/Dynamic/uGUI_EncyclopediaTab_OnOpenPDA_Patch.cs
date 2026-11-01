using System.Reflection;
using NitroxClient.GameLogic.HUD;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// item b, the "Clear all" button of the databank (<see cref="PdaClearAllButton" />). The PDA calls <c>OnOpenPDA</c> of every tab each time it opens; the first call makes the button, which
/// is a child of the tab's content. Not made in <c>Awake</c> or <c>OnUpdate</c>: <c>OnUpdate(true)</c> also runs from the PDA's warm-up in the middle of <c>Player.Awake</c> (the patches are live by
/// then, but the PDA is not open and nothing may be made at that moment); <c>OnOpenPDA</c> is the one call that always comes with a PDA the player is about to see. Local only, nothing is sent.
/// </summary>
public sealed partial class uGUI_EncyclopediaTab_OnOpenPDA_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_EncyclopediaTab t) => t.OnOpenPDA(default, default));

    public static void Postfix(uGUI_EncyclopediaTab __instance)
    {
        PdaClearAllButton.OnOpenPda(__instance);
    }
}
