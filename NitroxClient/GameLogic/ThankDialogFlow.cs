using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NitroxClient.GameLogic;

/// <summary>
/// The ways the thank-you message after the credits can be put in front of the player, best first. Each one is tried only when the one before it could not be shown.
/// </summary>
public enum ThankDialogMethod
{
    /// <summary>Nothing (before the first try, or after the last way was tried).</summary>
    None = 0,

    /// <summary>The game's own <c>uGUI_Dialog</c>, moved under the main menu. Ok (or Back) quits the game. The only way that hides the buttons of the main menu.</summary>
    GameDialog = 1,

    /// <summary>The message panel of the Nitrox multiplayer menu (<c>MainMenuNotificationPanel.ShowMessage</c>). Ok quits the game.</summary>
    MenuMessage = 2,

    /// <summary>No message at all: the buttons of the main menu are shown, the player quits by hand.</summary>
    PlainMenu = 3
}

/// <summary>
/// The decisions of the thank-you dialog that follows the credits (<c>uGUI_MainMenu_Start_Patch</c>): when it is shown, that it is shown once per credits, which way is tried after which (and
/// the loop that tries them), when a dialog counts as really up, and what the single result line says. Contains no Unity types so that it can be tested without the game.
/// </summary>
/// <remarks>
/// The patch runs after every step of the main menu's <c>Start</c> iterator, so "is it time yet" and "did I show it already" have to be answered here and not by the caller's luck.
/// </remarks>
public sealed class ThankDialogFlow
{
    public const string NOT_RUN = "not run";

    /// <summary>A dialog (or a rect) smaller than this in either direction cannot be seen.</summary>
    public const float MIN_DIALOG_SIZE = 1f;

    /// <summary>A product of canvas group alphas below this is invisible.</summary>
    public const float MIN_ALPHA = 0.01f;

    private const string EXCEPTION_WORD = "Exception";
    private const string NO_DETAIL = "no detail";
    private const string NO_REASON = "no reason given";
    private const int MAX_TEXT_LENGTH = 400;

    private static readonly Regex exceptionPattern = new(EXCEPTION_WORD, RegexOptions.IgnoreCase);
    private static readonly Regex whitespacePattern = new(@"\s+");

    private readonly List<string> failures = [];
    private bool pending;

    /// <summary>
    /// One way of putting the message in front of the player. Returns true when it worked; <paramref name="detail" /> then says where/how, and otherwise why not. May throw: that counts as
    /// "did not work" (see <see cref="Run" />).
    /// </summary>
    public delegate bool TryWay(ThankDialogMethod method, out string detail);

    /// <summary>
    /// What the caller read off the dialog, as plain values (so that <see cref="IsDialogUp" /> can be tested without Unity).
    /// </summary>
    public sealed class DialogFacts
    {
        /// <param name="open">The dialog's own open flag</param>
        /// <param name="activeInHierarchy">Whether the dialog's object and all its parents are active</param>
        /// <param name="hasCanvas">Whether an enabled canvas is above the dialog</param>
        /// <param name="alpha">The product of the alphas of the canvas groups above the dialog (and on it), 1 when there are none</param>
        /// <param name="blocksRaycasts">False when a canvas group above the dialog (or on it) blocks no raycasts, i.e. its buttons cannot be clicked</param>
        /// <param name="width">The width of the dialog's rect</param>
        /// <param name="height">The height of the dialog's rect</param>
        /// <param name="centerInsideCanvas">Whether the center of the dialog's rect is inside the rect of the root canvas</param>
        public DialogFacts(bool open, bool activeInHierarchy, bool hasCanvas, float alpha, bool blocksRaycasts, float width, float height, bool centerInsideCanvas)
        {
            Open = open;
            ActiveInHierarchy = activeInHierarchy;
            HasCanvas = hasCanvas;
            Alpha = alpha;
            BlocksRaycasts = blocksRaycasts;
            Width = width;
            Height = height;
            CenterInsideCanvas = centerInsideCanvas;
        }

        public bool Open { get; }
        public bool ActiveInHierarchy { get; }
        public bool HasCanvas { get; }
        public float Alpha { get; }
        public bool BlocksRaycasts { get; }
        public float Width { get; }
        public float Height { get; }
        public bool CenterInsideCanvas { get; }
    }

    /// <summary>
    /// What the last run did, one line: "shown via game dialog (...)", "fell back to menu message (...); failed before: game dialog: ..." or "failed, nothing could be shown: ...".
    /// Never contains the word "exception" (the live runs scan the log for it, see <see cref="ReasonOf" />) and never a line break.
    /// </summary>
    public string LastResult { get; private set; } = NOT_RUN;

    /// <summary>
    /// True when the last run did not get the first way to work.
    /// </summary>
    public bool FellBack { get; private set; }

    /// <summary>
    /// True from <see cref="CreditsEnded" /> until <see cref="TryBegin" /> has said yes.
    /// </summary>
    public bool IsPending => pending;

    /// <summary>
    /// The credits are over: the next main menu is to show the dialog. Calling it twice before that menu is up is still one run.
    /// </summary>
    public void CreditsEnded()
    {
        pending = true;
        FellBack = false;
        LastResult = "waiting for the main menu";
    }

    /// <summary>
    /// Once per credits: the main menu's <c>Start</c> iterator calls the patch after EVERY step, so only the first call after the iterator has finished returns true.
    /// </summary>
    /// <param name="menuStartFinished">False while <c>uGUI_MainMenu.Start</c> still has steps to run (the patch's <c>__result</c> is true then)</param>
    public bool TryBegin(bool menuStartFinished)
    {
        if (!pending || !menuStartFinished)
        {
            return false;
        }

        pending = false;
        failures.Clear();
        return true;
    }

    /// <summary>
    /// Tries the ways in order (<see cref="Next" />) until one works: a way that returns false or throws is recorded with its reason (<see cref="Failed" />) and the next one is tried, the
    /// first that works ends the run (<see cref="Completed" />), and when none works the run gives up (<see cref="GaveUp" />). The result is in <see cref="LastResult" /> and
    /// <see cref="FellBack" />. This is the whole loop of the patch; it only supplies <paramref name="tryWay" />.
    /// </summary>
    public void Run(TryWay tryWay)
    {
        if (tryWay == null)
        {
            throw new ArgumentNullException(nameof(tryWay));
        }

        failures.Clear();
        ThankDialogMethod method = Next(ThankDialogMethod.None);
        while (method != ThankDialogMethod.None)
        {
            bool worked;
            string detail;
            try
            {
                worked = tryWay(method, out detail);
            }
            catch (Exception ex)
            {
                worked = false;
                detail = ReasonOf(ex);
            }

            if (worked)
            {
                Completed(method, detail);
                return;
            }

            Failed(method, detail);
            method = Next(method);
        }

        GaveUp();
    }

    /// <summary>
    /// The way to try after <paramref name="previous" />: None, GameDialog, MenuMessage, PlainMenu, then None again (nothing left).
    /// </summary>
    public static ThankDialogMethod Next(ThankDialogMethod previous)
    {
        switch (previous)
        {
            case ThankDialogMethod.None:
                return ThankDialogMethod.GameDialog;
            case ThankDialogMethod.GameDialog:
                return ThankDialogMethod.MenuMessage;
            case ThankDialogMethod.MenuMessage:
                return ThankDialogMethod.PlainMenu;
            default:
                return ThankDialogMethod.None;
        }
    }

    /// <summary>
    /// Only the game dialog hides the buttons of the main menu (so that nothing but the dialog is left to click), and only after the dialog is known to be up. The message panel is a page
    /// of the menu itself and the plain menu is the menu: both need the buttons.
    /// </summary>
    public static bool HidesPrimaryOptions(ThankDialogMethod method)
    {
        return method == ThankDialogMethod.GameDialog;
    }

    /// <summary>
    /// Whether the dialog is really on screen, decided from facts that the caller reads off the dialog AFTER it was asked to show (and read again after the buttons of the main menu were hidden).
    /// The checks are the ones that can be made without seeing the screen: open, active, a canvas, a size, not transparent, clickable, inside the canvas. What they cannot tell (another element
    /// drawn over it, a clipping mask, a wrong sorting order) is left to the one look at the screen that thankdialog.md asks for.
    /// </summary>
    /// <param name="facts">What the caller read off the dialog</param>
    /// <param name="reason">What is wrong (the first problem found) when the answer is false</param>
    public static bool IsDialogUp(DialogFacts facts, out string reason)
    {
        if (facts == null)
        {
            reason = "no facts about the dialog were read";
            return false;
        }

        if (!facts.Open)
        {
            reason = "the dialog did not open";
            return false;
        }

        if (!facts.ActiveInHierarchy)
        {
            reason = "the dialog is not active in the hierarchy (a parent is inactive)";
            return false;
        }

        if (!facts.HasCanvas)
        {
            reason = "the dialog has no canvas above it (or the canvas is disabled)";
            return false;
        }

        // Written as "not at least" so that NaN fails too
        if (!(facts.Width >= MIN_DIALOG_SIZE) || !(facts.Height >= MIN_DIALOG_SIZE))
        {
            reason = $"the dialog has no size ({Format(facts.Width)} x {Format(facts.Height)})";
            return false;
        }

        if (!(facts.Alpha >= MIN_ALPHA))
        {
            reason = $"the dialog is transparent (canvas group alpha {Format(facts.Alpha)})";
            return false;
        }

        if (!facts.BlocksRaycasts)
        {
            reason = "a canvas group above the dialog blocks no raycasts (its buttons cannot be clicked)";
            return false;
        }

        if (!facts.CenterInsideCanvas)
        {
            reason = "the center of the dialog is outside the canvas (off screen)";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// The short name of a way, for the result line.
    /// </summary>
    public static string Describe(ThankDialogMethod method)
    {
        switch (method)
        {
            case ThankDialogMethod.GameDialog:
                return "game dialog";
            case ThankDialogMethod.MenuMessage:
                return "menu message";
            case ThankDialogMethod.PlainMenu:
                return "plain menu";
            default:
                return "nothing";
        }
    }

    /// <summary>
    /// The one-line reason for a way that threw, without the word "exception": the live runs (rocket.ps1) count every time-stamped log line that contains it as an error, and a handled fallback
    /// is not one. The type name loses its suffix ("NullReference: Object reference not set ..."), the message loses line breaks, is cut when very long, and "exception" in it becomes "error".
    /// </summary>
    public static string ReasonOf(Exception exception)
    {
        if (exception == null)
        {
            return "unknown error";
        }

        string name = exception.GetType().Name;
        if (name.EndsWith(EXCEPTION_WORD, StringComparison.Ordinal))
        {
            name = name.Length > EXCEPTION_WORD.Length ? name.Substring(0, name.Length - EXCEPTION_WORD.Length) : "Error";
        }

        string message = Clean(exception.Message);
        return message.Length > 0 ? $"{Clean(name)}: {message}" : Clean(name);
    }

    /// <summary>
    /// A way did not work: remember why, for the result line.
    /// </summary>
    public void Failed(ThankDialogMethod method, string reason)
    {
        string text = Clean(reason);
        failures.Add($"{Describe(method)}: {(text.Length > 0 ? text : NO_REASON)}");
    }

    /// <summary>
    /// A way worked. The result says which one, and (when an earlier way failed) which ones failed and why.
    /// </summary>
    public void Completed(ThankDialogMethod method, string detail)
    {
        string text = Clean(detail);
        string place = text.Length > 0 ? text : NO_DETAIL;
        string reasons = string.Join("; ", failures);
        FellBack = failures.Count > 0;
        LastResult = FellBack ? $"fell back to {Describe(method)} ({place}); failed before: {reasons}" : $"shown via {Describe(method)} ({place})";
    }

    /// <summary>
    /// Every way failed.
    /// </summary>
    public void GaveUp()
    {
        string reasons = string.Join("; ", failures);
        FellBack = true;
        LastResult = $"failed, nothing could be shown: {reasons}";
    }

    private static string Format(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One line of text for the result: no line breaks or runs of spaces, cut at <see cref="MAX_TEXT_LENGTH" />, and the word "exception" (any case) replaced by "error" (keeping the capital).
    /// </summary>
    private static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string oneLine = whitespacePattern.Replace(text, " ").Trim();
        if (oneLine.Length > MAX_TEXT_LENGTH)
        {
            oneLine = $"{oneLine.Substring(0, MAX_TEXT_LENGTH)}...";
        }

        return exceptionPattern.Replace(oneLine, match => char.IsUpper(match.Value[0]) ? "Error" : "error");
    }
}
