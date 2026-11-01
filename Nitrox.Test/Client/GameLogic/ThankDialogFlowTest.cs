namespace NitroxClient.GameLogic;

[TestClass]
public class ThankDialogFlowTest
{
    /// <summary>A flow whose credits ended and whose menu begun, i.e. what the patch holds when it calls <see cref="ThankDialogFlow.Run" />.</summary>
    private static ThankDialogFlow StartedFlow()
    {
        ThankDialogFlow flow = new();
        flow.CreditsEnded();
        flow.TryBegin(true).Should().BeTrue();
        return flow;
    }

    /// <summary>Facts of a dialog that is fine; a test changes the one it is about.</summary>
    private static ThankDialogFlow.DialogFacts Facts(bool open = true, bool activeInHierarchy = true, bool hasCanvas = true, float alpha = 1f, bool blocksRaycasts = true, float width = 800f, float height = 600f, bool centerInsideCanvas = true)
    {
        return new ThankDialogFlow.DialogFacts(open, activeInHierarchy, hasCanvas, alpha, blocksRaycasts, width, height, centerInsideCanvas);
    }

    [TestMethod]
    public void TryBegin_BeforeTheCreditsEnded_ReturnsFalse()
    {
        ThankDialogFlow flow = new();

        flow.TryBegin(true).Should().BeFalse();
        flow.TryBegin(false).Should().BeFalse();
        flow.IsPending.Should().BeFalse();
        flow.LastResult.Should().Be(ThankDialogFlow.NOT_RUN);
        flow.FellBack.Should().BeFalse();
    }

    [TestMethod]
    public void TryBegin_WhileStartIsStillRunning_WaitsAndThenBeginsExactlyOnce()
    {
        ThankDialogFlow flow = new();
        flow.CreditsEnded();

        // The patch runs after every step of the Start iterator; only the step that ends it (the iterator's MoveNext returned false) may start the dialog
        flow.TryBegin(false).Should().BeFalse();
        flow.IsPending.Should().BeTrue();
        flow.TryBegin(true).Should().BeTrue();
        flow.IsPending.Should().BeFalse();
        flow.TryBegin(true).Should().BeFalse();
        flow.TryBegin(false).Should().BeFalse();
        flow.TryBegin(true).Should().BeFalse();
    }

    [TestMethod]
    public void CreditsEnded_SaysThatItWaitsForTheMainMenu()
    {
        ThankDialogFlow flow = new();

        flow.CreditsEnded();

        flow.LastResult.Should().Be("waiting for the main menu");
        flow.FellBack.Should().BeFalse();
    }

    [TestMethod]
    public void TryBegin_AfterASecondCreditsEnd_BeginsAgain()
    {
        ThankDialogFlow flow = new();
        flow.CreditsEnded();
        flow.TryBegin(true).Should().BeTrue();
        flow.TryBegin(true).Should().BeFalse();

        flow.CreditsEnded();

        flow.TryBegin(true).Should().BeTrue();
        flow.TryBegin(true).Should().BeFalse();
    }

    [TestMethod]
    public void CreditsEnded_CalledTwiceBeforeTheMenu_BeginsOnlyOnce()
    {
        ThankDialogFlow flow = new();
        flow.CreditsEnded();
        flow.CreditsEnded();

        flow.TryBegin(true).Should().BeTrue();
        flow.TryBegin(true).Should().BeFalse();
    }

    [TestMethod]
    public void TryBegin_WhenTheMenuNeverFinishedStart_StaysPendingForTheNextMenu()
    {
        ThankDialogFlow flow = new();
        flow.CreditsEnded();

        // The first menu's Start was cut short (scene unloaded): nothing begins, nothing is lost
        flow.TryBegin(false).Should().BeFalse();
        flow.TryBegin(false).Should().BeFalse();

        // The next menu's Start finishes
        flow.TryBegin(true).Should().BeTrue();
    }

    [TestMethod]
    public void Next_WalksGameDialogMenuMessagePlainMenuThenStops()
    {
        ThankDialogFlow.Next(ThankDialogMethod.None).Should().Be(ThankDialogMethod.GameDialog);
        ThankDialogFlow.Next(ThankDialogMethod.GameDialog).Should().Be(ThankDialogMethod.MenuMessage);
        ThankDialogFlow.Next(ThankDialogMethod.MenuMessage).Should().Be(ThankDialogMethod.PlainMenu);
        ThankDialogFlow.Next(ThankDialogMethod.PlainMenu).Should().Be(ThankDialogMethod.None);
    }

    [TestMethod]
    public void HidesPrimaryOptions_IsTrueOnlyForTheGameDialog()
    {
        ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.GameDialog).Should().BeTrue();
        ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.MenuMessage).Should().BeFalse();
        ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.PlainMenu).Should().BeFalse();
        ThankDialogFlow.HidesPrimaryOptions(ThankDialogMethod.None).Should().BeFalse();
    }

    [TestMethod]
    public void IsDialogUp_EverythingRight_ReturnsTrue()
    {
        ThankDialogFlow.IsDialogUp(Facts(), out string reason).Should().BeTrue();

        reason.Should().BeEmpty();
    }

    [TestMethod]
    public void IsDialogUp_NotOpen_ReturnsFalseAndNamesTheProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(open: false), out string reason).Should().BeFalse();

        reason.Should().Contain("did not open");
    }

    [TestMethod]
    public void IsDialogUp_NotActiveInHierarchy_ReturnsFalseAndNamesTheProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(activeInHierarchy: false), out string reason).Should().BeFalse();

        reason.Should().Contain("not active in the hierarchy");
    }

    [TestMethod]
    public void IsDialogUp_NoCanvas_ReturnsFalseAndNamesTheProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(hasCanvas: false), out string reason).Should().BeFalse();

        reason.Should().Contain("no canvas");
    }

    [TestMethod]
    public void IsDialogUp_ZeroWidthOrHeight_ReturnsFalseAndSaysTheSize()
    {
        ThankDialogFlow.IsDialogUp(Facts(width: 0f), out string widthReason).Should().BeFalse();
        ThankDialogFlow.IsDialogUp(Facts(height: 0f), out string heightReason).Should().BeFalse();

        widthReason.Should().Contain("no size").And.Contain("0 x 600");
        heightReason.Should().Contain("no size").And.Contain("800 x 0");
    }

    [TestMethod]
    public void IsDialogUp_SizeNotANumber_ReturnsFalse()
    {
        ThankDialogFlow.IsDialogUp(Facts(width: float.NaN), out string reason).Should().BeFalse();

        reason.Should().Contain("no size");
    }

    [TestMethod]
    public void IsDialogUp_SizeAtTheMinimum_ReturnsTrueAndJustBelowReturnsFalse()
    {
        ThankDialogFlow.IsDialogUp(Facts(width: ThankDialogFlow.MIN_DIALOG_SIZE, height: ThankDialogFlow.MIN_DIALOG_SIZE), out _).Should().BeTrue();
        ThankDialogFlow.IsDialogUp(Facts(width: ThankDialogFlow.MIN_DIALOG_SIZE * 0.99f), out _).Should().BeFalse();
        ThankDialogFlow.IsDialogUp(Facts(height: ThankDialogFlow.MIN_DIALOG_SIZE * 0.99f), out _).Should().BeFalse();
    }

    [TestMethod]
    public void IsDialogUp_TransparentCanvasGroup_ReturnsFalseAndSaysTheAlpha()
    {
        ThankDialogFlow.IsDialogUp(Facts(alpha: 0f), out string reason).Should().BeFalse();

        reason.Should().Contain("transparent").And.Contain("alpha 0");
    }

    [TestMethod]
    public void IsDialogUp_AlphaAtTheMinimum_ReturnsTrueAndJustBelowReturnsFalse()
    {
        ThankDialogFlow.IsDialogUp(Facts(alpha: ThankDialogFlow.MIN_ALPHA), out _).Should().BeTrue();
        ThankDialogFlow.IsDialogUp(Facts(alpha: ThankDialogFlow.MIN_ALPHA / 2f), out _).Should().BeFalse();
        ThankDialogFlow.IsDialogUp(Facts(alpha: float.NaN), out _).Should().BeFalse();
    }

    [TestMethod]
    public void IsDialogUp_CanvasGroupBlocksRaycasts_ReturnsFalseAndNamesTheProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(blocksRaycasts: false), out string reason).Should().BeFalse();

        reason.Should().Contain("raycasts");
    }

    [TestMethod]
    public void IsDialogUp_CenterOutsideTheCanvas_ReturnsFalseAndNamesTheProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(centerInsideCanvas: false), out string reason).Should().BeFalse();

        reason.Should().Contain("outside the canvas");
    }

    [TestMethod]
    public void IsDialogUp_NothingRight_NamesTheFirstProblem()
    {
        ThankDialogFlow.IsDialogUp(Facts(open: false, activeInHierarchy: false, hasCanvas: false, alpha: 0f, blocksRaycasts: false, width: 0f, height: 0f, centerInsideCanvas: false), out string reason).Should().BeFalse();

        reason.Should().Contain("did not open");
    }

    [TestMethod]
    public void IsDialogUp_SeveralVisibilityProblems_NamesTheSizeBeforeTheAlpha()
    {
        ThankDialogFlow.IsDialogUp(Facts(alpha: 0f, blocksRaycasts: false, width: 0f, centerInsideCanvas: false), out string reason).Should().BeFalse();

        reason.Should().Contain("no size");
    }

    [TestMethod]
    public void IsDialogUp_NoFacts_ReturnsFalse()
    {
        ThankDialogFlow.IsDialogUp(null, out string reason).Should().BeFalse();

        reason.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Completed_FirstTry_ReportsShownViaGameDialogAndNotFellBack()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Completed(ThankDialogMethod.GameDialog, "the dialog field at Menu canvas/Panel/Options/Dialog");

        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("shown via game dialog (the dialog field at Menu canvas/Panel/Options/Dialog)");
    }

    [TestMethod]
    public void Completed_AfterFailures_ReportsFellBackWithEveryReason()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, "no uGUI_Dialog");
        flow.Failed(ThankDialogMethod.MenuMessage, "no group");
        flow.Completed(ThankDialogMethod.PlainMenu, "the buttons of the main menu are shown");

        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Contain("fell back to plain menu");
        flow.LastResult.Should().Contain("game dialog: no uGUI_Dialog");
        flow.LastResult.Should().Contain("menu message: no group");
        flow.LastResult.Should().Contain("the buttons of the main menu are shown");
    }

    [TestMethod]
    public void Completed_AfterOneFailure_ReportsTheWayThatWorkedAndTheReasonOfTheOneBefore()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, "the dialog did not open");
        flow.Completed(ThankDialogMethod.MenuMessage, "message page of the Nitrox multiplayer menu");

        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("fell back to menu message (message page of the Nitrox multiplayer menu); failed before: game dialog: the dialog did not open");
    }

    [TestMethod]
    public void GaveUp_ReportsFailedAndFellBack()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, "a");
        flow.Failed(ThankDialogMethod.MenuMessage, "b");
        flow.Failed(ThankDialogMethod.PlainMenu, "c");
        flow.GaveUp();

        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().StartWith("failed, nothing could be shown");
        flow.LastResult.Should().Contain("game dialog: a").And.Contain("menu message: b").And.Contain("plain menu: c");
    }

    [TestMethod]
    public void NewRun_ClearsTheReasonsAndResultOfTheLastRun()
    {
        ThankDialogFlow flow = StartedFlow();
        flow.Failed(ThankDialogMethod.GameDialog, "old reason");
        flow.Completed(ThankDialogMethod.MenuMessage, "old detail");
        flow.FellBack.Should().BeTrue();

        flow.CreditsEnded();
        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("waiting for the main menu");
        flow.TryBegin(true).Should().BeTrue();
        flow.Completed(ThankDialogMethod.GameDialog, "new detail");

        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("shown via game dialog (new detail)");
        flow.LastResult.Should().NotContain("old reason");
    }

    [TestMethod]
    public void Describe_NamesEveryMethodAndNothingForNone()
    {
        ThankDialogFlow.Describe(ThankDialogMethod.GameDialog).Should().Be("game dialog");
        ThankDialogFlow.Describe(ThankDialogMethod.MenuMessage).Should().Be("menu message");
        ThankDialogFlow.Describe(ThankDialogMethod.PlainMenu).Should().Be("plain menu");
        ThankDialogFlow.Describe(ThankDialogMethod.None).Should().Be("nothing");
    }

    [TestMethod]
    public void Failed_ReasonWithLineBreaksAndTheWordException_IsOneLineWithoutTheWord()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, "first\r\nsecond   third NullReferenceException in LoaderExceptions");
        flow.GaveUp();

        flow.LastResult.Should().Be("failed, nothing could be shown: game dialog: first second third NullReferenceError in LoaderErrors");
    }

    [TestMethod]
    public void Failed_VeryLongReason_IsCut()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, new string('a', 1000));
        flow.GaveUp();

        flow.LastResult.Length.Should().BeLessThan(500);
        flow.LastResult.Should().EndWith("...");
    }

    [TestMethod]
    public void Failed_NoReason_SaysSo()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Failed(ThankDialogMethod.GameDialog, null);
        flow.Failed(ThankDialogMethod.MenuMessage, "   ");
        flow.GaveUp();

        flow.LastResult.Should().Be("failed, nothing could be shown: game dialog: no reason given; menu message: no reason given");
    }

    [TestMethod]
    public void ReasonOf_DropsTheExceptionSuffixOfTheTypeName()
    {
        ThankDialogFlow.ReasonOf(new NullReferenceException("Object reference not set to an instance of an object.")).Should().Be("NullReference: Object reference not set to an instance of an object.");
        ThankDialogFlow.ReasonOf(new TimeoutException("slow")).Should().Be("Timeout: slow");
    }

    [TestMethod]
    public void ReasonOf_PlainException_IsCalledError()
    {
        ThankDialogFlow.ReasonOf(new Exception("plain")).Should().Be("Error: plain");
    }

    [TestMethod]
    public void ReasonOf_MessageWithLineBreaks_IsOneLine()
    {
        ThankDialogFlow.ReasonOf(new InvalidOperationException("line one\r\nline two\n\tline three")).Should().Be("InvalidOperation: line one line two line three");
    }

    [TestMethod]
    public void ReasonOf_MessageWithTheWordException_DoesNotContainItAnymore()
    {
        string reason = ThankDialogFlow.ReasonOf(new InvalidOperationException("see the LoaderExceptions property and the inner exception"));

        reason.Should().Be("InvalidOperation: see the LoaderErrors property and the inner error");
        reason.ToLowerInvariant().Should().NotContain("exception");
    }

    [TestMethod]
    public void ReasonOf_EmptyMessage_IsTheTypeNameAlone()
    {
        ThankDialogFlow.ReasonOf(new NullReferenceException(string.Empty)).Should().Be("NullReference");
    }

    [TestMethod]
    public void ReasonOf_Null_SaysUnknown()
    {
        ThankDialogFlow.ReasonOf(null).Should().Be("unknown error");
    }

    [TestMethod]
    public void Run_FirstWayWorks_StopsThereAndReportsShownViaGameDialog()
    {
        ThankDialogFlow flow = StartedFlow();
        List<ThankDialogMethod> tried = [];

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            tried.Add(method);
            detail = "the dialog field at Menu canvas/Panel/Options/Dialog";
            return true;
        });

        tried.Should().Equal(ThankDialogMethod.GameDialog);
        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("shown via game dialog (the dialog field at Menu canvas/Panel/Options/Dialog)");
    }

    [TestMethod]
    public void Run_FirstWayReturnsFalse_TriesTheSecondAndKeepsTheReason()
    {
        ThankDialogFlow flow = StartedFlow();
        List<ThankDialogMethod> tried = [];

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            tried.Add(method);
            if (method == ThankDialogMethod.GameDialog)
            {
                detail = "no uGUI_Dialog below the main menu";
                return false;
            }

            detail = "message page of the Nitrox multiplayer menu";
            return true;
        });

        tried.Should().Equal(ThankDialogMethod.GameDialog, ThankDialogMethod.MenuMessage);
        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("fell back to menu message (message page of the Nitrox multiplayer menu); failed before: game dialog: no uGUI_Dialog below the main menu");
    }

    [TestMethod]
    public void Run_FirstWayThrows_CountsAsFailedAndTheSecondWayIsTried()
    {
        ThankDialogFlow flow = StartedFlow();
        List<ThankDialogMethod> tried = [];

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            tried.Add(method);
            if (method == ThankDialogMethod.GameDialog)
            {
                throw new NullReferenceException("Object reference not set to an instance of an object.");
            }

            detail = "message page of the Nitrox multiplayer menu";
            return true;
        });

        tried.Should().Equal(ThankDialogMethod.GameDialog, ThankDialogMethod.MenuMessage);
        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("fell back to menu message (message page of the Nitrox multiplayer menu); failed before: game dialog: NullReference: Object reference not set to an instance of an object.");
        // rocket.ps1 counts every time-stamped log line with this word as an error entry; a handled fallback must not be one
        flow.LastResult.ToLowerInvariant().Should().NotContain("exception");
    }

    [TestMethod]
    public void Run_FirstTwoWaysFail_TheThirdIsThePlainMenu()
    {
        ThankDialogFlow flow = StartedFlow();
        List<ThankDialogMethod> tried = [];

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            tried.Add(method);
            if (method == ThankDialogMethod.PlainMenu)
            {
                detail = "the buttons of the main menu are shown";
                return true;
            }

            if (method == ThankDialogMethod.GameDialog)
            {
                throw new InvalidOperationException("no good");
            }

            detail = "no group";
            return false;
        });

        tried.Should().Equal(ThankDialogMethod.GameDialog, ThankDialogMethod.MenuMessage, ThankDialogMethod.PlainMenu);
        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("fell back to plain menu (the buttons of the main menu are shown); failed before: game dialog: InvalidOperation: no good; menu message: no group");
    }

    [TestMethod]
    public void Run_NothingWorks_GivesUpAfterTheThreeWaysAndSaysWhy()
    {
        ThankDialogFlow flow = StartedFlow();
        List<ThankDialogMethod> tried = [];

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            tried.Add(method);
            if (method == ThankDialogMethod.MenuMessage)
            {
                throw new InvalidOperationException("no panel");
            }

            detail = method == ThankDialogMethod.GameDialog ? "no dialog" : "no main menu";
            return false;
        });

        tried.Should().Equal(ThankDialogMethod.GameDialog, ThankDialogMethod.MenuMessage, ThankDialogMethod.PlainMenu);
        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("failed, nothing could be shown: game dialog: no dialog; menu message: InvalidOperation: no panel; plain menu: no main menu");
    }

    [TestMethod]
    public void Run_EveryWayThrows_GivesUpWithoutThrowing()
    {
        ThankDialogFlow flow = StartedFlow();
        int calls = 0;

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            calls++;
            throw new NullReferenceException("nope");
        });

        calls.Should().Be(3);
        flow.FellBack.Should().BeTrue();
        flow.LastResult.Should().Be("failed, nothing could be shown: game dialog: NullReference: nope; menu message: NullReference: nope; plain menu: NullReference: nope");
    }

    [TestMethod]
    public void Run_WayWorksWithoutADetail_SaysSo()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            detail = null;
            return true;
        });

        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("shown via game dialog (no detail)");
    }

    [TestMethod]
    public void Run_WayFailsWithoutAReason_SaysSo()
    {
        ThankDialogFlow flow = StartedFlow();

        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            detail = null;
            return method == ThankDialogMethod.MenuMessage;
        });

        flow.LastResult.Should().Be("fell back to menu message (no detail); failed before: game dialog: no reason given");
    }

    [TestMethod]
    public void Run_SecondRun_DoesNotKeepTheFailuresOfTheFirst()
    {
        ThankDialogFlow flow = StartedFlow();
        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            detail = "old reason";
            return false;
        });
        flow.FellBack.Should().BeTrue();

        flow.CreditsEnded();
        flow.TryBegin(true).Should().BeTrue();
        flow.Run((ThankDialogMethod method, out string detail) =>
        {
            detail = "fine";
            return true;
        });

        flow.FellBack.Should().BeFalse();
        flow.LastResult.Should().Be("shown via game dialog (fine)");
    }

    [TestMethod]
    public void Run_WithoutAWay_Throws()
    {
        ThankDialogFlow flow = StartedFlow();

        Action run = () => flow.Run(null);

        run.Should().Throw<ArgumentNullException>();
    }
}
