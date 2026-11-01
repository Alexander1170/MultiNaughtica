using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// The base of the screens of the Cyclops' stations (the gunner's, the navigator's and the engineer's): the game's own input group, the way the PDA and the chat are, with the two things that the game's
/// input module (<c>FPSInputModule.ProcessMousePress</c>) needs so that a mouse button does not take the player out of the station.
/// <para>
/// <b>A press on nothing is a click outside.</b> On every mouse press the module asks what is under the pointer (with a locked cursor, as at the gunner, the middle of the screen). When nothing that belongs to the
/// open group is there, it calls <c>ChangeGroup(null)</c>, which calls <c>OnDeselect</c> of the group, and the station treated that as "the input was taken away" and closed (the log of coop.12 has eight gunner sessions
/// that all ended so, and five navigator sessions). The cure is a graphic that covers the whole screen, behind everything else, that the pointer always finds (<see cref="AddClickCatcher" />): a press on it
/// belongs to the group.
/// </para>
/// <para>
/// <b>A press inside the group re-selects it with the walking lock off.</b> The module then calls <c>OnReselect(false)</c>, which releases the lock on the walking that the station took when it opened, and the
/// player could walk away from the desk with the console open. The stations always lock (<see cref="OnReselect" />).
/// </para>
/// <para>
/// <b>The safety net.</b> If the group is taken away by a mouse press all the same (and not by Escape), the screen takes it back on the next frame instead of closing the station, at most three times in two
/// seconds, and the log says so (<see cref="TakenAwayByAPress" />, <see cref="RetakeIfAsked" />): a station that works with a flicker is better than one that closes on every click.
/// </para>
/// </summary>
public abstract class ConsoleInputGroup : uGUI_InputGroup
{
    private const string CATCHER_NAME = "NitroxClickCatcher";
    private const int MAX_PRESS_LOGS = 12;
    private const int MAX_RETAKES = 3;
    private const float RETAKE_WINDOW_SECONDS = 2f;

    private int pressLogs;
    private int retakeFrame = -1;
    private int retakes;
    private float retakeWindowStart = float.NegativeInfinity;

    /// <summary>The station's name in the log: "gunner", "navigator", "engineer".</summary>
    protected abstract string StationLogName { get; }

    /// <summary>How many times a mouse press took the screen away and it was taken back (for the log and the tests of the live checks).</summary>
    public int RetakeCount { get; private set; }

    /// <summary>
    /// Puts the invisible graphic over the whole canvas, as its first child (so that the buttons and everything else are in front of it and get their presses first). Call it first in the station's Setup,
    /// once the canvas and its raycaster are on this object.
    /// </summary>
    protected void AddClickCatcher()
    {
        GameObject catcherObject = new(CATCHER_NAME, typeof(RectTransform), typeof(Image));
        catcherObject.transform.SetParent(transform, false);
        catcherObject.transform.SetAsFirstSibling();
        RectTransform rect = (RectTransform)catcherObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = catcherObject.GetComponent<Image>();
        // one level of 255, so that no renderer or batcher takes it for "nothing to draw" and drops it from the raycast; the eye cannot tell
        image.color = new Color(0f, 0f, 0f, 1f / 255f);
        image.raycastTarget = true;
        image.canvasRenderer.cullTransparentMesh = false;
    }

    /// <summary>A press inside the group re-selects it (<c>FPSInputModule.ProcessMousePress</c>) with the walking lock off; a station keeps it on.</summary>
    public override void OnReselect(bool lockMovement)
    {
        base.OnReselect(true);
    }

    /// <summary>
    /// Whether the screen was just taken away by a mouse press (not by Escape): the station calls it from <c>OnDeselect</c> when it did not give the screen up itself; true means "do not close, the screen takes
    /// the input back in the next frame" (<see cref="RetakeIfAsked" />). Logs what happened either way.
    /// </summary>
    protected bool TakenAwayByAPress()
    {
        bool left = UnityEngine.Input.GetMouseButtonDown(0);
        bool right = UnityEngine.Input.GetMouseButtonDown(1);
        bool middle = UnityEngine.Input.GetMouseButtonDown(2);
        bool escape = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
        bool press = left || right || middle;
        float now = Time.unscaledTime;
        if (now - retakeWindowStart > RETAKE_WINDOW_SECONDS)
        {
            retakeWindowStart = now;
            retakes = 0;
        }
        bool retake = press && !escape && retakes < MAX_RETAKES;
        Log.Info($"[VehicleUpgrades] {StationLogName}: the input module took the screen away in frame {Time.frameCount} (mouse buttons pressed this frame: left {left}, right {right}, middle {middle}; Escape {escape}; cursor {Cursor.lockState}); {(retake ? "a press on nothing the screen can see: it takes the input back" : "the station closes")}");
        if (retake)
        {
            retakes++;
            retakeFrame = Time.frameCount + 1;
        }
        return retake;
    }

    /// <summary>Takes the input back when <see cref="TakenAwayByAPress" /> asked for it. True when it did (the station skips the rest of its frame).</summary>
    protected bool RetakeIfAsked()
    {
        if (retakeFrame < 0 || Time.frameCount < retakeFrame)
        {
            return false;
        }
        retakeFrame = -1;
        RetakeCount++;
        Select(true);
        Log.Warn($"[VehicleUpgrades] {StationLogName}: the screen took the input back after a mouse press had taken it away ({RetakeCount} so far); the click catcher did not catch that press");
        return true;
    }

    /// <summary>Logs the first few mouse presses of the station, with whether the screen has the input (a press that closed it logs as <see cref="TakenAwayByAPress" /> instead).</summary>
    protected void LogPresses()
    {
        if (pressLogs >= MAX_PRESS_LOGS)
        {
            return;
        }
        for (int button = 0; button < 3; button++)
        {
            if (UnityEngine.Input.GetMouseButtonDown(button))
            {
                pressLogs++;
                Log.Info($"[VehicleUpgrades] {StationLogName}: mouse button {button} pressed, the screen has the input: {focused} (cursor {Cursor.lockState})");
                return;
            }
        }
    }
}
