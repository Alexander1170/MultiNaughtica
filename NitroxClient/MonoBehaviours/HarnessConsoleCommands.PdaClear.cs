#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.HUD;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours;

// Live check "pda_clearall" (a harness script that is not part of this repository, item b): the "Clear all" button of the PDA databank. Debug builds only.
// Registered by HarnessConsoleCommands.LiveBatch.cs (LiveCommand_<name> = command <name>), run with the server's "/console <player> <command> [arg] [arg]" or "/consoleall <command> ...".
// The PDA opens only by these commands (the harness cannot type into the PDA); every command that opens it closes it again unless it is told to "stay".
//
//   pdaclearprobe [open|close|watch <seconds>|hold <seconds>|shot <seconds>]
//                            (/console takes a player, the command and TWO words after it, /consoleall the command and two: the modes carry what would need a third)
//                            one line "[SyncHarness] pdaclearprobe: n=.. ..." about the button and the badges of this client (without an argument it only reads, whatever is open). "open" opens the
//                            databank first (a short wait for the button), logs one line and closes the PDA again. "watch" opens it, logs a line every 0.25 s for the given time, then
//                            "pdaclearprobe: done"; it closes the PDA again if it opened it. "hold" is "watch" that leaves the PDA open (a client with an open PDA for the picture of the autosave
//                            indicator, or to watch the button come back). "shot" is "watch" that also takes a picture of the window (liveshot now pdaclear-button) in the first sample that shows the button
//                            ready, or 1.5 s after the PDA opened when it never does. "close" only closes the PDA (logs "pdaclearprobe: closed"). The line:
//                              pdaOpen tabOpen    the PDA is open / the databank is its shown tab
//                              exists active      the button was made / it is switched on (hidden = none to clear, or a gamepad)
//                              interactable alpha Button.IsInteractable, the inherited alpha of its canvas renderer (0 while the tab is not the shown one)
//                              state              ready (visible and clickable) | inert (switched on but alpha 0 or not interactable) | hidden | missing
//                              label x y w h      its text (spaces as _), its centre and size in screen pixels (origin bottom left), screen = the window's size, onScreen = inside the window
//                              enc others         notifications of the Encyclopedia group (the number on the tab) / of every other group
//                              treeCount keys     the count the databank keeps at the root of its list (-1 before the list was built) / the Encyclopedia keys, sorted, joined by |
//                              canvas cursor mouse hit   the canvas render mode, Cursor.lockState, Input.mousePosition, the first thing the event system finds under the centre of the button
//                              gamepad popup      GameInput.IsPrimaryDeviceGamepad() (the button is hidden then), a popup of the game is showing (an unlock's popup that is still up activates its entry when the PDA
//                                                 opens and clears that badge: the script waits until none shows) / how many wait behind it
//   pdaclearclick [pointer|invoke] [stay]
//                            opens the databank, presses the button, waits half a second, logs one line "pdaclearclick: mode=.. sent=.. reason=.. hit=.. hitIsButton=.. handled=.. state=.. before=..
//                            after=.. stateAfter=.. treeBefore=.. treeAfter=.. othersBefore=.. othersAfter=..", closes the PDA unless "stay". "pointer" (default) finds what is under the centre of the button with the
//                            event system and, when that is the button, sends pointer down / up / click to it as the game's input module does (nothing is sent to a button that is hidden or covered);
//                            "invoke" calls onClick directly, whatever the state.
//   pdaui                    one-off dump of the databank tab's geometry for the placement of the button: the header text, the list, the row prefab, the sprites, the canvas, and every object under
//                            the tab's content (path, anchors, pivot, position, size, screen rectangle, components), at most 150 lines. Needs the databank tab to have been opened once.
//
// Every command logs "[SyncHarness] <command>: ..." lines with key=value pairs, or "failed reason=<text>". Nothing throws out of a Unity callback.
public partial class HarnessConsoleCommands
{
    private bool pdaClearBusy;

    private sealed class PdaOpenResult
    {
        public PDA Pda;
        public uGUI_EncyclopediaTab Tab;
        public string Failure;
        public bool OpenedHere;
    }

    private static string PdaClearInt(float value)
    {
        return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The window rectangle of a UI element in screen pixels (origin bottom left); empty when it has no canvas.</summary>
    private static Rect PdaClearScreenRect(RectTransform rect)
    {
        if (!rect)
        {
            return Rect.zero;
        }

        Canvas canvas = rect.GetComponentInParent<Canvas>();
        canvas = canvas ? canvas.rootCanvas : null;
        if (!canvas)
        {
            return Rect.zero;
        }

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return PdaClearProject(null, corners);
        }

        // The PDA's canvas is in world space and has no world camera: its raycaster (uGUI_GraphicRaycaster.eventCamera) asks for the game's GUI camera (SNCameraRoot.guiCam, or the main camera),
        // which is the camera that draws it. Projecting through the main camera put the whole canvas on one pixel (0 x 0 px at (1284,517) in the second live run).
        Camera camera = PdaClearEventCamera(canvas);
        Rect projected = PdaClearProject(camera, corners);
        if ((projected.width < 1f || projected.height < 1f) && Camera.main && Camera.main != camera)
        {
            projected = PdaClearProject(Camera.main, corners);
        }

        return projected;
    }

    /// <summary>The camera the raycaster of this canvas casts its rays from; the canvas's world camera, then the main camera, when it has no raycaster.</summary>
    private static Camera PdaClearEventCamera(Canvas canvas)
    {
        GraphicRaycaster raycaster = canvas ? canvas.GetComponentInChildren<GraphicRaycaster>() : null;
        Camera camera = raycaster ? raycaster.eventCamera : null;
        if (!camera && canvas)
        {
            camera = canvas.worldCamera;
        }

        return camera ? camera : Camera.main;
    }

    private static Rect PdaClearProject(Camera camera, Vector3[] corners)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        foreach (Vector3 corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static string PdaClearCanvasMode(RectTransform rect)
    {
        Canvas canvas = rect ? rect.GetComponentInParent<Canvas>() : null;
        canvas = canvas ? canvas.rootCanvas : null;
        return canvas ? canvas.renderMode.ToString() : "none";
    }

    /// <summary>The first thing the event system finds at a screen position, with whether it is the button (or a child of it).</summary>
    private static string PdaClearHit(Vector2 position, GameObject button, out bool hitIsButton, out RaycastResult result)
    {
        hitIsButton = false;
        result = default;
        if (!EventSystem.current)
        {
            return "no-event-system";
        }

        // The PDA's canvas is in world space and its pointer goes through the PDA's own raycast (uGUI_PDA.Raycast, what the input module asks while the PDA is open): the first live run found
        // nothing under the button with the event system's raycasters (hit=none, then the list's viewport). The event system is the fallback for everything else.
        List<RaycastResult> results = new();
        PointerEventData data = new(EventSystem.current) { position = position };
        uGUI_PDA ui = uGUI_PDA.main;
        if (ui && Player.main && Player.main.GetPDA() && Player.main.GetPDA().isOpen)
        {
            ui.Raycast(data, results);
        }

        if (results.Count == 0)
        {
            EventSystem.current.RaycastAll(data, results);
        }

        if (results.Count == 0)
        {
            return "none";
        }

        result = results[0];
        GameObject top = results[0].gameObject;
        hitIsButton = button && top && (top == button || top.transform.IsChildOf(button.transform));
        return top ? top.name : "none";
    }

    private static int PdaClearOtherGroups(NotificationManager manager)
    {
        int others = 0;
        if (manager)
        {
            foreach (KeyValuePair<NotificationManager.NotificationId, NotificationManager.NotificationData> pair in manager.notifications)
            {
                if (pair.Key.group != NotificationManager.Group.Encyclopedia)
                {
                    others++;
                }
            }
        }

        return others;
    }

    private static string PdaClearKeys(NotificationManager manager)
    {
        List<string> keys = new();
        if (manager)
        {
            foreach (KeyValuePair<NotificationManager.NotificationId, NotificationManager.NotificationData> pair in manager.notifications)
            {
                if (pair.Key.group == NotificationManager.Group.Encyclopedia)
                {
                    keys.Add(pair.Key.key);
                }
            }
        }

        keys.Sort(StringComparer.Ordinal);
        return keys.Count == 0 ? "none" : string.Join("|", keys);
    }

    private static bool PdaClearPopupShowing()
    {
        uGUI_PopupNotification popup = uGUI_PopupNotification.main;
        return popup && popup.isShowingMessage;
    }

    private static int PdaClearPopupQueued()
    {
        uGUI_PopupNotification popup = uGUI_PopupNotification.main;
        return popup && popup.queue != null ? popup.queue.Count : 0;
    }

    /// <summary>The numbers on the categories of the databank added up (each badge is counted in the category it sits under); -1 before the list was built.</summary>
    private static int PdaClearCategoryCount(uGUI_EncyclopediaTab tab)
    {
        if (!tab || tab.tree == null)
        {
            return -1;
        }

        int sum = 0;
        for (int i = 0; i < tab.tree.childCount; i++)
        {
            if (tab.tree[i] is CraftNode category)
            {
                sum += uGUI_EncyclopediaTab.GetNodeNotificationsCount(category);
            }
        }

        return sum;
    }

    private static string PdaClearState(out Rect screen)
    {
        Button button = PdaClearAllButton.ButtonComponent;
        screen = PdaClearScreenRect(PdaClearAllButton.Rect);
        if (!PdaClearAllButton.Exists)
        {
            return "missing";
        }

        if (!PdaClearAllButton.IsActive)
        {
            return "hidden";
        }

        CanvasRenderer renderer = button ? button.GetComponent<CanvasRenderer>() : null;
        float alpha = renderer ? renderer.GetInheritedAlpha() : 0f;
        return button && button.IsInteractable() && alpha > 0.01f ? "ready" : "inert";
    }

    private static string PdaClearLine(int n)
    {
        PDA pda = Player.main ? Player.main.GetPDA() : null;
        uGUI_PDA ui = uGUI_PDA.main;
        bool pdaOpen = pda && pda.isOpen;
        bool tabOpen = pdaOpen && ui && ui.currentTabType == PDATab.Encyclopedia;
        uGUI_EncyclopediaTab tab = ui ? ui.tabEncyclopedia as uGUI_EncyclopediaTab : null;
        NotificationManager manager = NotificationManager.main;
        Button button = PdaClearAllButton.ButtonComponent;
        string state = PdaClearState(out Rect screen);
        CanvasRenderer renderer = button ? button.GetComponent<CanvasRenderer>() : null;
        float alpha = renderer ? renderer.GetInheritedAlpha() : 0f;
        bool onScreen = screen.width > 0f && screen.height > 0f && screen.xMax > 0f && screen.yMax > 0f && screen.xMin < Screen.width && screen.yMin < Screen.height;
        string hit = "none";
        if (state != "missing" && state != "hidden" && screen.width > 0f)
        {
            hit = PdaClearHit(screen.center, button.gameObject, out _, out _);
        }

        int treeCount = PdaClearCategoryCount(tab);
        string label = PdaClearAllButton.Exists ? PdaClearAllButton.LabelText.Replace(' ', '_') : "none";
        return string.Concat($"[SyncHarness] pdaclearprobe: n={n} pdaOpen={pdaOpen} tabOpen={tabOpen} exists={PdaClearAllButton.Exists} active={PdaClearAllButton.IsActive} interactable={button && button.IsInteractable()} alpha={PdaNumber(alpha)} ",
               $"state={state} label={label} x={PdaClearInt(screen.center.x)} y={PdaClearInt(screen.center.y)} w={PdaClearInt(screen.width)} h={PdaClearInt(screen.height)} screen={Screen.width}x{Screen.height} onScreen={onScreen} ",
               $"enc={PdaBadges.CountEncyclopediaBadges()} others={PdaClearOtherGroups(manager)} treeCount={treeCount} keys={PdaClearKeys(manager)} canvas={PdaClearCanvasMode(PdaClearAllButton.Rect)} cursor={Cursor.lockState} ",
               $"mouse={PdaClearInt(Input.mousePosition.x)},{PdaClearInt(Input.mousePosition.y)} hit={hit}", $" gamepad={GameInput.IsPrimaryDeviceGamepad()} popup={PdaClearPopupShowing()} popupQueue={PdaClearPopupQueued()}");
    }

    /// <summary>Opens the PDA on the databank (when it is not open already) and waits for the list to be built, then for the button to settle.</summary>
    private IEnumerator PdaOpenDatabank(PdaOpenResult result)
    {
        PDA pda = null;
        try
        {
            pda = Player.main ? Player.main.GetPDA() : null;
            if (!pda)
            {
                result.Failure = "no PDA (the client is not in the world)";
            }
            else if (!pda.isInUse)
            {
                if (pda.Open(PDATab.Encyclopedia, null, null))
                {
                    result.OpenedHere = true;
                }
                else
                {
                    result.Failure = "the PDA refused to open";
                }
            }
        }
        catch (Exception exception)
        {
            result.Failure = $"{exception.GetType().Name}: {exception.Message}";
        }

        result.Pda = pda;
        if (result.Failure != null)
        {
            yield break;
        }

        float deadline = Time.unscaledTime + 8f;
        while (Time.unscaledTime < deadline)
        {
            uGUI_PDA ui = uGUI_PDA.main;
            uGUI_EncyclopediaTab candidate = ui ? ui.tabEncyclopedia as uGUI_EncyclopediaTab : null;
            if (candidate && pda.isOpen && ui.currentTabType == PDATab.Encyclopedia && candidate.tree != null)
            {
                result.Tab = candidate;
                break;
            }

            yield return null;
        }

        if (!result.Tab)
        {
            result.Failure = "the databank tab did not open within 8 s";
            yield break;
        }

        if (result.OpenedHere)
        {
            // the PDA rises and its screen is revealed over about two seconds (the first live run measured and photographed it half way: 0 x 0 px, a hand and no screen)
            yield return new WaitForSecondsRealtime(2.5f);
        }

        // the button is made by OnOpenPDA and switched on by the first OnUpdate of the shown tab: give it a moment (it stays hidden when there is nothing to clear)
        float settle = Time.unscaledTime + 3f;
        while (Time.unscaledTime < settle && (!PdaClearAllButton.Exists || (PdaBadges.CountEncyclopediaBadges() > 0 && PdaClearState(out _) != "ready")))
        {
            yield return null;
        }
    }

    private void PdaClearClose(PdaOpenResult open)
    {
        try
        {
            if (open.Pda && open.Pda.isInUse)
            {
                open.Pda.Close();
            }
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdaclear: failed reason=closing the PDA: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LiveCommand_pdaclearprobe(NotificationCenter.Notification n)
    {
        try
        {
            string mode = LiveArgString(n, 0, string.Empty);
            if (mode == "close")
            {
                PDA pda = Player.main ? Player.main.GetPDA() : null;
                if (pda && pda.isInUse)
                {
                    pda.Close();
                }

                Log.Info("[SyncHarness] pdaclearprobe: closed");
                return;
            }

            if (mode == "open" || mode == "watch" || mode == "hold" || mode == "shot")
            {
                if (pdaClearBusy)
                {
                    Log.Info("[SyncHarness] pdaclearprobe: failed reason=the previous pdaclear command is still running");
                    return;
                }

                float seconds = mode == "open" ? 0f : Mathf.Clamp(LiveArgFloat(n, 1, 10f), 1f, 300f);
                StartCoroutine(PdaClearProbeRoutine(seconds, mode == "hold", mode == "shot" ? "pdaclear-button" : null));
                return;
            }

            Log.Info(PdaClearLine(1));
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdaclearprobe: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator PdaClearProbeRoutine(float watchSeconds, bool stay, string shotTag)
    {
        pdaClearBusy = true;
        try
        {
            PdaOpenResult open = new();
            yield return StartCoroutine(PdaOpenDatabank(open));
            if (open.Failure != null)
            {
                Log.Info($"[SyncHarness] pdaclearprobe: failed reason={open.Failure}");
                yield break;
            }

            float opened = Time.realtimeSinceStartup;
            bool shotTaken = shotTag == null;
            if (watchSeconds <= 0f)
            {
                if (!shotTaken)
                {
                    StartCoroutine(LiveShotRoutine(false, 0f, shotTag));
                    PdaUiDumpAll();
                    yield return new WaitForSecondsRealtime(1f);
                }

                Log.Info(PdaClearLine(1));
            }
            else
            {
                float end = Time.realtimeSinceStartup + watchSeconds;
                int count = 0;
                while (Time.realtimeSinceStartup < end)
                {
                    Log.Info(PdaClearLine(++count));
                    if (!shotTaken && (PdaClearState(out _) == "ready" || Time.realtimeSinceStartup - opened > 1.5f))
                    {
                        shotTaken = true;
                        StartCoroutine(LiveShotRoutine(false, 0f, shotTag));
                        PdaUiDumpAll();
                    }

                    yield return new WaitForSecondsRealtime(0.25f);
                }

                Log.Info("[SyncHarness] pdaclearprobe: done");
            }

            if (open.OpenedHere && !stay)
            {
                PdaClearClose(open);
            }
        }
        finally
        {
            pdaClearBusy = false;
        }
    }

    private void LiveCommand_pdaclearclick(NotificationCenter.Notification n)
    {
        string mode = LiveArgString(n, 0, "pointer");
        bool stay = LiveArgString(n, 0, string.Empty) == "stay" || LiveArgString(n, 1, string.Empty) == "stay";
        if (mode != "pointer" && mode != "invoke")
        {
            mode = "pointer";
        }

        if (pdaClearBusy)
        {
            Log.Info("[SyncHarness] pdaclearclick: failed reason=the previous pdaclear command is still running");
            return;
        }

        StartCoroutine(PdaClearClickRoutine(mode, stay));
    }

    private IEnumerator PdaClearClickRoutine(string mode, bool stay)
    {
        pdaClearBusy = true;
        try
        {
            PdaOpenResult open = new();
            yield return StartCoroutine(PdaOpenDatabank(open));
            if (open.Failure != null)
            {
                Log.Info($"[SyncHarness] pdaclearclick: failed reason={open.Failure}");
                yield break;
            }

            NotificationManager manager = NotificationManager.main;
            int before = PdaBadges.CountEncyclopediaBadges();
            int othersBefore = PdaClearOtherGroups(manager);
            int treeBefore = PdaClearCategoryCount(open.Tab);
            string state = PdaClearState(out Rect screen);
            bool sent = false;
            bool handled = false;
            bool hitIsButton = false;
            string hit = "none";
            string reason = "-";
            try
            {
                Button button = PdaClearAllButton.ButtonComponent;
                if (mode == "invoke")
                {
                    if (button)
                    {
                        button.onClick.Invoke();
                        sent = true;
                    }
                    else
                    {
                        reason = "no button";
                    }
                }
                else if (state == "missing" || state == "hidden")
                {
                    // a blind click would land on the row under the place where the button would be
                    reason = state;
                }
                else
                {
                    hit = PdaClearHit(screen.center, button.gameObject, out hitIsButton, out RaycastResult raycast);
                    if (!hitIsButton)
                    {
                        reason = "the first thing under the centre of the button is not the button";
                    }
                    else
                    {
                        GameObject target = button.gameObject;
                        PointerEventData data = new(EventSystem.current)
                        {
                            position = screen.center,
                            pressPosition = screen.center,
                            button = PointerEventData.InputButton.Left,
                            clickCount = 1,
                            pointerEnter = target,
                            pointerPress = target,
                            rawPointerPress = target,
                            pointerCurrentRaycast = raycast,
                            pointerPressRaycast = raycast,
                            eligibleForClick = true
                        };
                        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
                        handled = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
                        sent = true;
                    }
                }
            }
            catch (Exception exception)
            {
                reason = $"{exception.GetType().Name}: {exception.Message}";
            }

            yield return new WaitForSeconds(0.5f);
            int after = PdaBadges.CountEncyclopediaBadges();
            int othersAfter = PdaClearOtherGroups(manager);
            int treeAfter = PdaClearCategoryCount(open.Tab);
            string stateAfter = PdaClearState(out _);
            Log.Info($"[SyncHarness] pdaclearclick: mode={mode} sent={sent} reason={reason.Replace(' ', '_')} hit={hit} hitIsButton={hitIsButton} handled={handled} state={state} before={before} after={after} stateAfter={stateAfter} treeBefore={treeBefore} treeAfter={treeAfter} othersBefore={othersBefore} othersAfter={othersAfter}");
            if (!stay)
            {
                PdaClearClose(open);
            }
        }
        finally
        {
            pdaClearBusy = false;
        }
    }

    private static string PdaUiComponents(GameObject go)
    {
        StringBuilder builder = new();
        foreach (Component component in go.GetComponents<Component>())
        {
            if (component && !(component is Transform))
            {
                builder.Append(builder.Length > 0 ? "," : string.Empty).Append(component.GetType().Name);
            }
        }

        return builder.Length > 0 ? builder.ToString() : "-";
    }

    private static string PdaUiRect(RectTransform rect)
    {
        Rect screen = PdaClearScreenRect(rect);
        return string.Concat($"anchors=({PdaNumber(rect.anchorMin.x)},{PdaNumber(rect.anchorMin.y)})-({PdaNumber(rect.anchorMax.x)},{PdaNumber(rect.anchorMax.y)}) pivot=({PdaNumber(rect.pivot.x)},{PdaNumber(rect.pivot.y)}) ",
               $"pos=({PdaNumber(rect.anchoredPosition.x)},{PdaNumber(rect.anchoredPosition.y)}) size=({PdaNumber(rect.rect.width)},{PdaNumber(rect.rect.height)}) screen=({PdaClearInt(screen.xMin)},{PdaClearInt(screen.yMin)})-({PdaClearInt(screen.xMax)},{PdaClearInt(screen.yMax)})");
    }

    private int pdaUiLines;

    private void PdaUiDump(Transform node, string path, int depth)
    {
        if (pdaUiLines >= 150 || depth > 4)
        {
            return;
        }

        RectTransform rect = node as RectTransform;
        if (rect)
        {
            pdaUiLines++;
            Log.Info($"[SyncHarness] pdaui: node path={path} active={node.gameObject.activeSelf} {PdaUiRect(rect)} comps={PdaUiComponents(node.gameObject)}");
        }

        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);
            PdaUiDump(child, $"{path}/{child.name}", depth + 1);
        }
    }

    private void LiveCommand_pdaui(NotificationCenter.Notification n)
    {
        PdaUiDumpAll();
    }

    private void PdaUiDumpAll()
    {
        try
        {
            uGUI_PDA ui = uGUI_PDA.main;
            uGUI_EncyclopediaTab tab = ui ? ui.tabEncyclopedia as uGUI_EncyclopediaTab : null;
            if (!tab || !tab.content)
            {
                Log.Info("[SyncHarness] pdaui: failed reason=no databank tab");
                return;
            }

            TextMeshProUGUI header = tab.encyclopediaLabel;
            uGUI_ListEntry row = tab.prefabEntry ? tab.prefabEntry.GetComponent<uGUI_ListEntry>() : null;
            Canvas canvas = tab.content.GetComponentInParent<Canvas>();
            canvas = canvas ? canvas.rootCanvas : null;
            UISpriteData sprites = tab.entryNodeSprites;
            Log.Info(string.Concat($"[SyncHarness] pdaui: tab content={tab.content.name} contentAlpha={PdaNumber(tab.content.alpha)} interactable={tab.content.interactable} canvas={(canvas ? canvas.renderMode.ToString() : "none")} ",
                     $"scale={(canvas ? PdaNumber(canvas.scaleFactor) : "n/a")} sorting={(canvas ? canvas.sortingOrder : 0)} worldCamera={(canvas && canvas.worldCamera ? canvas.worldCamera.name : "none")} eventCamera={DescribePdaCamera(PdaClearEventCamera(canvas))} ",
                     $"pixelRect={(canvas ? $"{PdaNumber(canvas.pixelRect.x)},{PdaNumber(canvas.pixelRect.y)},{PdaNumber(canvas.pixelRect.width)}x{PdaNumber(canvas.pixelRect.height)}" : "n/a")} screen={Screen.width}x{Screen.height}"));
            if (header)
            {
                header.ForceMeshUpdate();
                Bounds bounds = header.textBounds;
                Log.Info(string.Concat($"[SyncHarness] pdaui: header text={header.text.Replace(' ', '_')} fontSize={PdaNumber(header.fontSize)} alignment={header.alignment} preferredWidth={PdaNumber(header.preferredWidth)} ",
                         $"textBounds=({PdaNumber(bounds.min.x)},{PdaNumber(bounds.min.y)})-({PdaNumber(bounds.max.x)},{PdaNumber(bounds.max.y)}) path={PdaUiPath(header.transform, tab.content.transform)} {PdaUiRect(header.rectTransform)}"));
            }

            if (tab.listScrollRect)
            {
                Log.Info($"[SyncHarness] pdaui: listScrollRect path={PdaUiPath(tab.listScrollRect.transform, tab.content.transform)} {PdaUiRect((RectTransform)tab.listScrollRect.transform)}");
            }

            Log.Info(string.Concat($"[SyncHarness] pdaui: row prefab fontSize={(row && row.text ? PdaNumber(row.text.fontSize) : "n/a")} preferredHeight={(row && row.layoutElement ? PdaNumber(row.layoutElement.preferredHeight) : "n/a")} ",
                     $"sprites normal={(sprites != null && sprites.normal)} hovered={(sprites != null && sprites.hovered)} selected={(sprites != null && sprites.selected)}"));
            pdaUiLines = 0;
            PdaUiDump(tab.content.transform, tab.content.name, 0);
            RectTransform buttonRect = PdaClearAllButton.Rect;
            Log.Info($"[SyncHarness] pdaui: button exists={PdaClearAllButton.Exists} {(buttonRect ? PdaUiRect(buttonRect) : "-")}");
        }
        catch (Exception exception)
        {
            Log.Info($"[SyncHarness] pdaui: failed reason={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string DescribePdaCamera(Camera camera)
    {
        return camera ? $"{camera.name}(ortho={camera.orthographic},size={PdaNumber(camera.orthographicSize)},fov={PdaNumber(camera.fieldOfView)},rect={PdaNumber(camera.pixelRect.x)},{PdaNumber(camera.pixelRect.y)},{PdaNumber(camera.pixelRect.width)}x{PdaNumber(camera.pixelRect.height)})" : "none";
    }

    private static string PdaUiPath(Transform node, Transform root)
    {
        StringBuilder path = new(node.name);
        for (Transform parent = node.parent; parent && parent != root.parent; parent = parent.parent)
        {
            path.Insert(0, $"{parent.name}/");
        }

        return path.ToString();
    }
}
#endif
