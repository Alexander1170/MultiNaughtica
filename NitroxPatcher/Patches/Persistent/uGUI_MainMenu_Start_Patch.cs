using System;
using System.Net;
using System.Reflection;
using HarmonyLib;
using Nitrox.Model.Constants;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.MultiplayerSession;
using NitroxClient.MonoBehaviours.Gui.MainMenu;
using NitroxClient.MonoBehaviours.Gui.MainMenu.ServerJoin;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.MultiplayerSession;
using Nitrox.Model.Subnautica.MultiplayerSession;
using NitroxPatcher.Patches.Dynamic;
using UnityEngine;
// Aliases and not "using NitroxClient.GameLogic;": that namespace has a Terrain (and more) that would hide the Unity ones
using ThankDialogFlow = NitroxClient.GameLogic.ThankDialogFlow;
using ThankDialogMethod = NitroxClient.GameLogic.ThankDialogMethod;

namespace NitroxPatcher.Patches.Persistent;

// TODO: Rework this to be less ad hoc and more robust with command line arguments
public sealed partial class uGUI_MainMenu_Start_Patch : NitroxPatch, IPersistentPatch
{
    private static readonly MethodInfo TARGET_METHOD = AccessTools.EnumeratorMoveNext(Reflect.Method((uGUI_MainMenu t) => t.Start()));

    /// <summary>
    ///     The decisions of the thank-you dialog after the credits (when, once per credits, which way after which). The postfix below runs after every step of the Start iterator.
    /// </summary>
    private static readonly ThankDialogFlow thankDialogFlow = new();

    /// <summary>
    ///     What the last thank-you dialog did (see <see cref="ThankDialogFlow.LastResult" />). Read by the Debug harness (command thankdialog), by reflection.
    /// </summary>
    public static string LastThankDialogResult => thankDialogFlow.LastResult;

#if DEBUG
    private static bool applied;
    private static string playerName;
#endif

    /// <param name="__result">The result of the Start iterator's MoveNext: false once the iterator is finished, i.e. every Start method of the menu scene has run</param>
    public static void Postfix(bool __result)
    {
        try
        {
            if (EndCreditsManager_OnLateUpdate_Patch.EndCreditsTriggered)
            {
                // Consumed: one end of the credits is one dialog (this postfix runs after every step of Start, and the flag is never cleared elsewhere)
                EndCreditsManager_OnLateUpdate_Patch.EndCreditsTriggered = false;
                thankDialogFlow.CreditsEnded();
            }

            if (thankDialogFlow.TryBegin(!__result))
            {
                SpawnThankDialog();
            }
        }
        catch (Exception ex)
        {
            // Never break the Start coroutine of the main menu
            Log.Error(ex, "The thank-you dialog after the credits failed unexpectedly");
        }

#if DEBUG
        if (applied)
        {
            return;
        }
        applied = true;

        string[] args = NitroxEnvironment.CommandLineArgs;
        Log.Info($"CommandLineArgs: '{string.Join(" ", args)}'");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--instantlaunch", StringComparison.OrdinalIgnoreCase) && args.Length > i + 1)
            {
                playerName = args[i + 1];
                Log.Info($"Detected instant launch, connecting to {IPAddress.Loopback}:{SubnauticaServerConstants.DEFAULT_PORT} as {playerName}");
                _ = JoinServerBackend.StartDetachedMultiplayerClientAsync(IPAddress.Loopback, SubnauticaServerConstants.DEFAULT_PORT, SessionConnectionStateChangedHandler);
            }
        }
#endif
    }

    /// <summary>
    ///     Puts the "Thanks for playing" message in front of the player after the credits (the end of the game), where Ok quits the game. Tries the ways of <see cref="ThankDialogMethod" /> one after
    ///     the other (the loop, with a thrown exception counting as a failed way, is <see cref="ThankDialogFlow.Run" />, which has the unit tests): the buttons of the main menu are hidden only once
    ///     the game's dialog passed the checks of <see cref="ThankDialogFlow.IsDialogUp" /> (open, active, canvas, size, alpha, raycasts, on screen), are shown again when it fails them after the hide, and a
    ///     way that does not work is replaced by the next one (the last is the plain main menu). What those checks cannot see (another element drawn over the dialog, a clipping mask) is left to
    ///     the look at the screen that a harness script that is not part of this repository asks for. The first try, the reason of every failure and the result are in <see cref="LastThankDialogResult" /> and in one log line.
    /// </summary>
    public static void SpawnThankDialog()
    {
        uGUI_MainMenu mainMenu = uGUI_MainMenu.main;
#if DEBUG
        LogMenuHierarchy(mainMenu);
#endif
        thankDialogFlow.Run((ThankDialogMethod method, out string detail) => TryShowWay(mainMenu, method, out detail));

        string result = thankDialogFlow.LastResult;
        if (thankDialogFlow.FellBack)
        {
            Log.Warn($"Thank-you dialog after the credits: {result}");
        }
        else
        {
            Log.Info($"Thank-you dialog after the credits: {result}");
        }
#if DEBUG
        Log.Info($"[SyncHarness] thankdialog: {result}");
#endif
    }

    private static bool TryShowWay(uGUI_MainMenu mainMenu, ThankDialogMethod method, out string detail)
    {
        switch (method)
        {
            case ThankDialogMethod.GameDialog:
                return TryShowGameDialog(mainMenu, out detail);
            case ThankDialogMethod.MenuMessage:
                return TryShowMenuMessage(mainMenu, out detail);
            default:
                return TryShowPlainMenu(mainMenu, out detail);
        }
    }

    /// <summary>
    ///     The game's own dialog (uGUI_Dialog) with an Ok button, moved under the main menu because it normally lives in the options page, which is inactive until the player opens it.
    ///     The buttons of the main menu are hidden only after the dialog is verified up, and the dialog is verified once more after the hide (the buttons are shown again when it fails that).
    ///     A dialog that did not come up is closed (without quitting the game) and put back where it was.
    /// </summary>
    private static bool TryShowGameDialog(uGUI_MainMenu mainMenu, out string detail)
    {
        if (!mainMenu)
        {
            detail = "uGUI_MainMenu.main is not set";
            return false;
        }

        uGUI_Dialog dialog = FindDialog(mainMenu, out string source);
        if (!dialog)
        {
            detail = "no uGUI_Dialog below the main menu";
            return false;
        }

        Transform dialogTransform = dialog.transform;
        Transform oldParent = dialogTransform.parent;
        int oldSiblingIndex = dialogTransform.GetSiblingIndex();
        string location = PathOf(dialogTransform);
        bool quitOnClose = true;
        bool shown = false;
        bool hidPrimaryOptions = false;
        string failure = string.Empty;
        try
        {
            dialogTransform.SetParent(mainMenu.transform, false);
            dialogTransform.SetAsLastSibling();
            dialog.Show(Localized("Nitrox_ThankForPlaying", "Thanks for using Nitrox!"), _ =>
            {
                if (quitOnClose)
                {
                    Application.Quit();
                }
            }, [Localized("Nitrox_OK", "Ok")]);

            if (CheckDialogIsUp(dialog, out failure))
            {
                if (ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.GameDialog))
                {
                    hidPrimaryOptions = true;
                    mainMenu.ShowPrimaryOptions(false); // Only now that the dialog is verified up

                    // The dialog must have survived the hide (it would not if it sat below the primary options) and still be what the checks accept: if not, the failure path below shows the buttons again
                    if (CheckDialogIsUp(dialog, out string reasonAfterHide))
                    {
                        shown = true;
                    }
                    else
                    {
                        failure = $"after the buttons of the main menu were hidden: {reasonAfterHide}";
                    }
                }
                else
                {
                    shown = true;
                }
            }
        }
        catch (Exception ex)
        {
            failure = ThankDialogFlow.ReasonOf(ex);
        }

        if (shown)
        {
            detail = $"{source} at {location}";
            return true;
        }

        quitOnClose = false; // Closing the half-shown dialog must not quit the game
        if (hidPrimaryOptions)
        {
            ShowPrimaryOptionsAgain(mainMenu); // Never leave the player on a blank main menu
        }
        CloseQuietly(dialog);
        MoveBack(dialogTransform, oldParent, oldSiblingIndex);
        detail = failure;
        return false;
    }

    /// <summary>
    ///     Reads the facts off the dialog (what <see cref="ThankDialogFlow.IsDialogUp" /> decides on): its open flag, whether it is active, an enabled canvas above it, the size and the center of
    ///     the largest rect of the dialog (the dialog's own root may be a point with sized children), the alpha and raycast flags of the canvas groups above it.
    /// </summary>
    private static bool CheckDialogIsUp(uGUI_Dialog dialog, out string reason)
    {
        Transform dialogTransform = dialog.transform;
        Canvas canvas = dialog.GetComponentInParent<Canvas>();
        ReadCanvasGroups(dialogTransform, out float alpha, out bool blocksRaycasts);
        RectTransform largestRect = LargestRect(dialogTransform);
        float width = largestRect ? largestRect.rect.width : 0f;
        float height = largestRect ? largestRect.rect.height : 0f;
        bool centerInsideCanvas = largestRect && canvas && IsCenterInsideRootCanvas(largestRect, canvas);
        ThankDialogFlow.DialogFacts facts = new(dialog.open, dialog.gameObject.activeInHierarchy, canvas && canvas.enabled, alpha, blocksRaycasts, width, height, centerInsideCanvas);
        return ThankDialogFlow.IsDialogUp(facts, out reason);
    }

    /// <summary>
    ///     The product of the alphas, and whether everything blocks raycasts, over the canvas groups on the dialog and above it (up to the first one that ignores its parent groups).
    /// </summary>
    private static void ReadCanvasGroups(Transform dialogTransform, out float alpha, out bool blocksRaycasts)
    {
        alpha = 1f;
        blocksRaycasts = true;
        for (Transform current = dialogTransform; current; current = current.parent)
        {
            CanvasGroup canvasGroup = current.GetComponent<CanvasGroup>();
            if (!canvasGroup)
            {
                continue;
            }

            alpha *= canvasGroup.alpha;
            blocksRaycasts = blocksRaycasts && canvasGroup.blocksRaycasts;
            if (canvasGroup.ignoreParentGroups)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     The active rect of the dialog or of one of its children with the biggest area (the dialog itself is the first candidate), or null when there is none.
    /// </summary>
    private static RectTransform LargestRect(Transform dialogTransform)
    {
        RectTransform largest = null;
        float largestArea = -1f;
        foreach (RectTransform candidate in dialogTransform.GetComponentsInChildren<RectTransform>())
        {
            Rect rect = candidate.rect;
            float area = rect.width * rect.height;
            if (area > largestArea)
            {
                largest = candidate;
                largestArea = area;
            }
        }
        return largest;
    }

    /// <summary>
    ///     Whether the center of the rect is inside the rect of the root canvas (both in the root canvas's space). True when there is nothing to compare with: a fact that cannot be read must not
    ///     fail the dialog.
    /// </summary>
    private static bool IsCenterInsideRootCanvas(RectTransform rectTransform, Canvas canvas)
    {
        Canvas rootCanvas = canvas.rootCanvas;
        RectTransform rootRect = rootCanvas ? rootCanvas.transform as RectTransform : null;
        if (!rootRect)
        {
            return true;
        }

        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        Vector3 localCenter = rootRect.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);
        return rootRect.rect.Contains(new Vector2(localCenter.x, localCenter.y));
    }

    private static void ShowPrimaryOptionsAgain(uGUI_MainMenu mainMenu)
    {
        try
        {
            if (mainMenu)
            {
                mainMenu.ShowPrimaryOptions(true);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Thank-you dialog: could not show the buttons of the main menu again: {ThankDialogFlow.ReasonOf(ex)}");
        }
    }

    /// <summary>
    ///     Finds the game's dialog without depending on the hierarchy of this game version: through the dialog field of the options page (which the vanilla page closes in its OnDisable, so it is
    ///     always assigned), then any dialog below the main menu or its right side, and only last through the path that older game versions had.
    /// </summary>
    private static uGUI_Dialog FindDialog(uGUI_MainMenu mainMenu, out string source)
    {
        foreach (uGUI_TabbedControlsPanel panel in mainMenu.GetComponentsInChildren<uGUI_TabbedControlsPanel>(true))
        {
            if (panel && panel.dialog)
            {
                source = $"the dialog field of {panel.GetType().Name} '{panel.name}'";
                return panel.dialog;
            }
        }

        foreach (uGUI_Dialog candidate in mainMenu.GetComponentsInChildren<uGUI_Dialog>(true))
        {
            if (candidate)
            {
                source = "a uGUI_Dialog below the main menu";
                return candidate;
            }
        }

        MainMenuRightSide rightSide = MainMenuRightSide.main;
        if (rightSide)
        {
            foreach (uGUI_Dialog sideCandidate in rightSide.GetComponentsInChildren<uGUI_Dialog>(true))
            {
                if (sideCandidate)
                {
                    source = "a uGUI_Dialog below the right side";
                    return sideCandidate;
                }
            }
        }

        Transform legacy = mainMenu.transform.Find("Panel/Options/Dialog"); // The path of older game versions, now only the last candidate
        uGUI_Dialog legacyDialog = legacy ? legacy.GetComponent<uGUI_Dialog>() : null;
        if (legacyDialog)
        {
            source = "the path Panel/Options/Dialog";
            return legacyDialog;
        }

        source = "none";
        return null;
    }

    /// <summary>
    ///     The message page of the Nitrox multiplayer menu (the one that shows join failures): needs no hierarchy path, only that the Nitrox menu modifications ran for this menu.
    /// </summary>
    private static bool TryShowMenuMessage(uGUI_MainMenu mainMenu, out string detail)
    {
        MainMenuRightSide rightSide = MainMenuRightSide.main;
        if (!mainMenu || !rightSide)
        {
            detail = "the main menu is not ready";
            return false;
        }

        MainMenuNotificationPanel panel = null;
        if (rightSide.groups != null)
        {
            foreach (MainMenuGroup menuGroup in rightSide.groups)
            {
                if (menuGroup && menuGroup.gameObject.name == MainMenuNotificationPanel.NAME)
                {
                    panel = menuGroup.GetComponent<MainMenuNotificationPanel>();
                    break;
                }
            }
        }

        if (!panel)
        {
            detail = $"the menu has no working {MainMenuNotificationPanel.NAME} group";
            return false;
        }

        mainMenu.ShowPrimaryOptions(!ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.MenuMessage));
        MainMenuNotificationPanel.ShowMessage(Localized("Nitrox_ThankForPlaying", "Thanks for using Nitrox!"), null, () => Application.Quit());
        detail = "message page of the Nitrox multiplayer menu";
        return true;
    }

    /// <summary>
    ///     The last way, which cannot fail on a menu that exists: the buttons of the main menu are visible, the player quits by hand.
    /// </summary>
    private static bool TryShowPlainMenu(uGUI_MainMenu mainMenu, out string detail)
    {
        if (!mainMenu)
        {
            detail = "uGUI_MainMenu.main is not set";
            return false;
        }

        mainMenu.ShowPrimaryOptions(!ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.PlainMenu));
        try
        {
            // An earlier way may have left another page of the right side open
            MainMenuRightSide rightSide = MainMenuRightSide.main;
            if (rightSide)
            {
                rightSide.OpenGroup("Home");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Thank-you dialog: could not open the home page again: {ThankDialogFlow.ReasonOf(ex)}");
        }

        detail = "the buttons of the main menu are shown";
        return true;
    }

    private static void CloseQuietly(uGUI_Dialog dialog)
    {
        try
        {
            if (dialog && dialog.open)
            {
                dialog.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Thank-you dialog: could not close the dialog again: {ThankDialogFlow.ReasonOf(ex)}");
        }
    }

    private static void MoveBack(Transform dialogTransform, Transform oldParent, int oldSiblingIndex)
    {
        try
        {
            if (dialogTransform && oldParent)
            {
                dialogTransform.SetParent(oldParent, false);
                dialogTransform.SetSiblingIndex(oldSiblingIndex);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Thank-you dialog: could not move the dialog back: {ThankDialogFlow.ReasonOf(ex)}");
        }
    }

    /// <summary>
    ///     The translation, or the English text when the language is not ready or has no entry (the game returns the key itself for a missing one).
    /// </summary>
    private static string Localized(string key, string english)
    {
        Language language = Language.main;
        string text = language ? language.Get(key) : null;
        return string.IsNullOrEmpty(text) || text == key ? english : text;
    }

    private static string PathOf(Transform transform)
    {
        if (!transform)
        {
            return "none";
        }

        string path = transform.name;
        for (Transform parent = transform.parent; parent; parent = parent.parent)
        {
            path = $"{parent.name}/{path}";
        }
        return path;
    }

#if DEBUG
    // So that the first live run documents the real hierarchy of this game version, also when the dialog falls back (a harness script that is not part of this repository)
    private static void LogMenuHierarchy(uGUI_MainMenu mainMenu)
    {
        try
        {
            Log.Info($"[SyncHarness] thankdialog: main menu object {(mainMenu ? PathOf(mainMenu.transform) : "none")}");
            if (mainMenu)
            {
                foreach (uGUI_TabbedControlsPanel panel in mainMenu.GetComponentsInChildren<uGUI_TabbedControlsPanel>(true))
                {
                    Log.Info($"[SyncHarness] thankdialog: tabbed panel {PathOf(panel.transform)} dialog={(panel.dialog ? PathOf(panel.dialog.transform) : "null")}");
                }
            }

            int logged = 0;
            foreach (uGUI_Dialog dialog in Resources.FindObjectsOfTypeAll<uGUI_Dialog>())
            {
                // Prefab assets and the like have no valid scene: only the dialogs of the loaded scenes
                if (!dialog || !dialog.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (++logged > 10)
                {
                    break;
                }
                Log.Info($"[SyncHarness] thankdialog: uGUI_Dialog at {PathOf(dialog.transform)} activeInHierarchy={dialog.gameObject.activeInHierarchy}");
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[SyncHarness] thankdialog: hierarchy dump failed {ThankDialogFlow.ReasonOf(ex)}");
        }
    }

    private static void SessionConnectionStateChangedHandler(IMultiplayerSessionConnectionState state)
    {
        switch (state.CurrentStage)
        {
            case MultiplayerSessionConnectionStage.AWAITING_RESERVATION_CREDENTIALS:

                if (Resolve<IMultiplayerSession>().SessionPolicy.RequiresServerPassword)
                {
                    Log.Error("Local server requires a password which is not supported with instant launch.");
                    Log.InGame("Local server requires a password which is not supported with instant launch.");
                    break;
                }

                NitroxColor playerColor = new(1,1,1);
                byte[] nameHash = playerName.ToMd5Hash();
                if (nameHash.Length >= 8)
                {
                    float hue = BitConverter.ToUInt64([nameHash[0], nameHash[1], nameHash[2], nameHash[3], nameHash[4], nameHash[5], nameHash[6], nameHash[7]], 0) / (float)ulong.MaxValue;
                    playerColor = NitroxColor.FromHsb(hue);
                }
                PlayerSettings playerSettings = new(playerColor);
                AuthenticationContext authenticationContext = new(playerName, Optional.Empty);
                Resolve<IMultiplayerSession>().RequestSessionReservation(playerSettings, authenticationContext);
                break;

            case MultiplayerSessionConnectionStage.SESSION_RESERVED:
                Resolve<IMultiplayerSession>().ConnectionStateChanged -= SessionConnectionStateChangedHandler;
                JoinServerBackend.StartGame();
                break;
        }
    }
#endif
}
