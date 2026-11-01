#if DEBUG
using System;
using System.Collections;
using System.Reflection;
using NitroxClient.MonoBehaviours.Gui.MainMenu.ServerJoin;

namespace NitroxClient.MonoBehaviours;

// Live check "thankdialog" : the thank-you dialog that the main menu shows after the credits (NitroxPatcher uGUI_MainMenu_Start_Patch). Debug builds only. Registered by
// HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> thankdialog [go|arm|status]".
//
//   thankdialog [go]    arms the dialog (sets EndCreditsManager_OnLateUpdate_Patch.EndCreditsTriggered, what the end of the credits does) and then leaves to the main menu the way the end of the credits does
//                       (JoinServerBackend.StopMultiplayerClient, then the 'Cleaner' scene), so that the Start patch of the new main menu shows the dialog. The result line "[SyncHarness] thankdialog: shown via ..." /
//                       "fell back to ..." / "failed, nothing could be shown: ..." is written by that patch (this component dies with the Multiplayer object on the way)
//   thankdialog arm     only arms: the next main menu (the player leaves by hand) shows the dialog
//   thankdialog status  logs whether it is armed and what the last dialog did ("not run" before the first)
//
// NitroxClient cannot reference NitroxPatcher, so the flag and the result are reached by reflection (the NitroxPatcher assembly is found by its simple name among the loaded ones).
public partial class HarnessConsoleCommands
{
    private const string THANKS_CREDITS_PATCH = "NitroxPatcher.Patches.Dynamic.EndCreditsManager_OnLateUpdate_Patch";
    private const string THANKS_START_PATCH = "NitroxPatcher.Patches.Persistent.uGUI_MainMenu_Start_Patch";
    private const BindingFlags THANKS_STATIC_MEMBER = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private void LiveCommand_thankdialog(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "go");
        try
        {
            if (mode == "status")
            {
                Log.Info($"[SyncHarness] thankdialog: status armed={ThanksTrigger()} last={ThanksLastResult()}");
                return;
            }

            if (mode != "go" && mode != "arm")
            {
                Log.Warn($"[SyncHarness] thankdialog: command error, unknown mode '{mode}', use go, arm or status");
                return;
            }

            Type creditsPatch = ThanksPatcherType(THANKS_CREDITS_PATCH);
            FieldInfo trigger = creditsPatch != null ? creditsPatch.GetField("EndCreditsTriggered", THANKS_STATIC_MEMBER) : null;
            if (trigger == null)
            {
                Log.Warn("[SyncHarness] thankdialog: command error, EndCreditsManager_OnLateUpdate_Patch.EndCreditsTriggered not found (NitroxPatcher not loaded, or the field was renamed)");
                return;
            }

            if (mode == "go" && !Player.main)
            {
                Log.Warn("[SyncHarness] thankdialog: command error, go needs a running game, the client is not in the world");
                return;
            }

            trigger.SetValue(null, true);
            Log.Info("[SyncHarness] thankdialog: armed (EndCreditsTriggered=True)");
            if (mode == "go")
            {
                Log.Info("[SyncHarness] thankdialog: leaving to the main menu the way the end of the credits does (StopMultiplayerClient, then the Cleaner scene); the result line follows from the main menu patch");
                UWE.CoroutineHost.StartCoroutine(ThanksLeaveToMenu()); // The vanilla host: this component dies with the Multiplayer object
            }
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] thankdialog: command error, {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static IEnumerator ThanksLeaveToMenu()
    {
        yield return null; // Let the packet that carried the command finish before the session is torn down
        try
        {
            JoinServerBackend.StopMultiplayerClient(); // What EndCreditsManager_OnLateUpdate_Patch.OnCreditsEnd does ...
            SceneCleaner.Open(); // ... right where the credits load the 'Cleaner' scene (the vanilla quit to the menu ends with exactly this call)
        }
        catch (Exception exception)
        {
            Log.Warn($"[SyncHarness] thankdialog: leaving to the main menu failed {exception.GetType().Name}: {exception.Message}");
        }
    }

    // net472 and net10 safe: scans the loaded assemblies by simple name; no Type.GetTypes (ReflectionTypeLoadException on a half-loaded assembly) and no assembly-qualified Type.GetType
    private static Type ThanksPatcherType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == "NitroxPatcher")
            {
                return assembly.GetType(fullName, false);
            }
        }

        return null;
    }

    private static string ThanksTrigger()
    {
        Type creditsPatch = ThanksPatcherType(THANKS_CREDITS_PATCH);
        FieldInfo field = creditsPatch != null ? creditsPatch.GetField("EndCreditsTriggered", THANKS_STATIC_MEMBER) : null;
        return field != null ? Convert.ToString(field.GetValue(null)) : "unavailable";
    }

    private static string ThanksLastResult()
    {
        Type startPatch = ThanksPatcherType(THANKS_START_PATCH);
        PropertyInfo property = startPatch != null ? startPatch.GetProperty("LastThankDialogResult", THANKS_STATIC_MEMBER) : null;
        return property != null ? Convert.ToString(property.GetValue(null, null)) : "unavailable";
    }
}
#endif
