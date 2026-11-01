#if DEBUG
using System;
using System.Reflection;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Sync test harness (Debug builds only), the live batch: every method of this class named <c>LiveCommand_&lt;name&gt;</c> with the signature
///     <c>void (NotificationCenter.Notification)</c> is registered as the dev console command <c>&lt;name&gt;</c> (lower case), so that a live check adds its commands in a file of its own
///     (<c>HarnessConsoleCommands.&lt;Check&gt;.cs</c>) without touching this one. They are run like the other harness commands, on a client, with the server's
///     <c>/console &lt;player&gt; &lt;command&gt; [arguments]</c>; the arguments arrive in <c>Notification.data</c> as strings. Each command logs its findings with the tag
///     <c>[SyncHarness] &lt;name&gt;: ...</c> so that the scenario scripts of docs/coop/tools/live can read them out of the client log.
/// </summary>
public partial class HarnessConsoleCommands
{
    private const string LIVE_COMMAND_PREFIX = "LiveCommand_";

    private void RegisterLiveBatchCommands()
    {
        foreach (MethodInfo method in typeof(HarnessConsoleCommands).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (!method.Name.StartsWith(LIVE_COMMAND_PREFIX, StringComparison.Ordinal) || method.ReturnType != typeof(void) || parameters.Length != 1 || parameters[0].ParameterType != typeof(NotificationCenter.Notification))
            {
                continue;
            }

            string command = method.Name.Substring(LIVE_COMMAND_PREFIX.Length).ToLowerInvariant();
            DevConsole.RegisterConsoleCommand(command, (DevConsole.Callback)Delegate.CreateDelegate(typeof(DevConsole.Callback), this, method), false, false);
            Log.Info($"[SyncHarness] live batch command registered: {command}");
        }
    }

    /// <summary>
    ///     The first string argument of a live command as a number, or the default.
    /// </summary>
    private static float LiveArgFloat(NotificationCenter.Notification n, int index, float defaultValue)
    {
        if (n?.data != null && n.data.Count > index && n.data[index] is string text &&
            float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
        {
            return value;
        }
        return defaultValue;
    }

    /// <summary>
    ///     A string argument of a live command, or the default.
    /// </summary>
    private static string LiveArgString(NotificationCenter.Notification n, int index, string defaultValue)
    {
        return n?.data != null && n.data.Count > index && n.data[index] is string text && text.Length > 0 ? text : defaultValue;
    }
}
#endif
