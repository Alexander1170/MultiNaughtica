using System;
using System.Reflection;
using NitroxClient.GameLogic;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Bug 2 of the first play session ("the yellow new badges of the databank take several clicks to clear", the second player more than the first). Vanilla never removes a badge on a click:
/// <c>NotificationManager.LateUpdate</c> counts 3 s down while the entry is the selected one and removes it then (or at once when the entry is deselected after more than 1 s; a shorter look
/// sets the timer back to 3 s, see <see cref="PdaBadgeRules" />). A player who clicks through a list of new entries, which is what the other player's scans make for her, leaves every badge behind.
/// Now opening an entry takes its badge away at once, the way everybody expects a click to work. Local only (a badge is the personal state of a player, nothing is sent): the notification is
/// removed with the game's own <c>NotificationManager.Remove</c>, so the counters of the categories follow through the tab's listener exactly as when the badge ends by itself.
/// <c>Activate</c> returns true only for an entry that became the displayed one, so a category, an entry that is displayed already and a popup that names nothing are left alone.
/// Debug builds: the environment variable <c>NITROX_PDA_BADGES=0</c> switches this off, so that the live check can show the vanilla behaviour from the same build (a harness script that is not part of this repository).
/// </summary>
public sealed partial class uGUI_EncyclopediaTab_Activate_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((uGUI_EncyclopediaTab t) => t.Activate(default));

#if DEBUG
    private static readonly bool clearEnabled = Environment.GetEnvironmentVariable("NITROX_PDA_BADGES") != "0";
#endif

    public static void Postfix(CraftNode node, bool __result)
    {
#if DEBUG
        if (!clearEnabled)
        {
            return;
        }
#endif
        string key = node?.id;
        if (PdaBadgeRules.ShouldClearOnActivate(__result, key))
        {
            PdaBadges.ClearEncyclopediaBadge(key);
        }
    }
}
