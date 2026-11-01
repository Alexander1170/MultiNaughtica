namespace NitroxClient.GameLogic;

[TestClass]
public class PdaBadgeRulesTest
{
    [TestMethod]
    public void AKnownEntryIsNeverAddedAgain()
    {
        // vanilla refuses it itself (PDAEncyclopedia.Add returns at ContainsEntry), the receiver does not even ask: no second popup, no timer set back under a reading player
        PdaBadgeRules.DecideRemoteAdd(entryKnown: true, verbose: true).Should().Be(PdaBadgeRules.RemoteAddAction.Ignore);
        PdaBadgeRules.DecideRemoteAdd(entryKnown: true, verbose: false).Should().Be(PdaBadgeRules.RemoteAddAction.Ignore);
    }

    [TestMethod]
    public void ANewEntryOfAScanGetsOneBadge()
    {
        PdaBadgeRules.DecideRemoteAdd(entryKnown: false, verbose: true).Should().Be(PdaBadgeRules.RemoteAddAction.AddWithBadge);
    }

    [TestMethod]
    public void TheRestoreAtJoinAddsWithoutABadge()
    {
        // the 62 entries of the world that a joiner gets are not 62 "new" badges
        PdaBadgeRules.DecideRemoteAdd(entryKnown: false, verbose: false).Should().Be(PdaBadgeRules.RemoteAddAction.AddQuietly);
    }

    [TestMethod]
    public void TheSameUnlockReportedByBothPlayersAddsOnceAndBadgesOnce()
    {
        // the server relays only the first report, but the story goals run on both clients and the scanner of each client can finish the same creature: replay what a client sees. The set stands in for PDAEncyclopedia.ContainsEntry (the game's own guard, case-sensitive keys), the decision under test is DecideRemoteAdd
        HashSet<string> known = new(StringComparer.Ordinal);
        int badges = 0;
        int adds = 0;
        foreach ((string key, bool verbose) in new[] { ("Jellyray", true), ("Jellyray", true), ("Reefback", true), ("Jellyray", false) })
        {
            PdaBadgeRules.RemoteAddAction action = PdaBadgeRules.DecideRemoteAdd(known.Contains(key), verbose);
            if (action == PdaBadgeRules.RemoteAddAction.Ignore)
            {
                continue;
            }

            known.Add(key);
            adds++;
            if (action == PdaBadgeRules.RemoteAddAction.AddWithBadge)
            {
                badges++;
            }
        }

        adds.Should().Be(2);
        badges.Should().Be(2);
        known.Should().BeEquivalentTo(new[] { "Jellyray", "Reefback" });
    }

    [TestMethod]
    public void OpeningAnEntryClearsItsBadge()
    {
        PdaBadgeRules.ShouldClearOnActivate(activated: true, key: "Jellyray").Should().BeTrue();
    }

    [TestMethod]
    public void NothingIsClearedWhenNoEntryWasActivated()
    {
        // Activate returns false for a category, for a node that is null (a popup that names no entry) and for the entry that is displayed already
        PdaBadgeRules.ShouldClearOnActivate(activated: false, key: "Jellyray").Should().BeFalse();
    }

    [TestMethod]
    public void NothingIsClearedWithoutAKey()
    {
        PdaBadgeRules.ShouldClearOnActivate(activated: true, key: null).Should().BeFalse();
        PdaBadgeRules.ShouldClearOnActivate(activated: true, key: string.Empty).Should().BeFalse();
    }

    [TestMethod]
    public void AnAuditOfTheNormalCaseIsClean()
    {
        string[] entries = ["Jellyray", "Reefback", "Bladderfish"];

        PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit(["Jellyray", "Reefback"], key => System.Array.IndexOf(entries, key) >= 0);

        audit.Total.Should().Be(2);
        audit.Distinct.Should().Be(2);
        audit.CaseDuplicates.Should().Be(0);
        audit.Orphans.Should().Be(0);
        audit.IsClean.Should().BeTrue();
    }

    [TestMethod]
    public void AnAuditOfNoBadgesIsClean()
    {
        PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit([], _ => true);

        audit.Total.Should().Be(0);
        audit.IsClean.Should().BeTrue();
    }

    [TestMethod]
    public void AKeyThatDiffersOnlyInCaseIsCountedAsOneEntry()
    {
        // PDAEncyclopedia.entries compares without regard to case, NotificationId does not: the only way two notifications could be about one entry
        PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit(["Jellyray", "jellyray"], _ => true);

        audit.Total.Should().Be(2);
        audit.Distinct.Should().Be(1);
        audit.CaseDuplicates.Should().Be(1);
        audit.IsClean.Should().BeFalse();
    }

    [TestMethod]
    public void ABadgeOfAnEntryThatThisClientDoesNotHaveIsAnOrphan()
    {
        PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit(["Jellyray", "Ghost"], key => key == "Jellyray");

        audit.Orphans.Should().Be(1);
        audit.CaseDuplicates.Should().Be(0);
        audit.IsClean.Should().BeFalse();
    }

    [TestMethod]
    public void AnEmptyKeyIsAnOrphanAndDoesNotAskTheGame()
    {
        bool asked = false;

        PdaBadgeRules.BadgeAudit audit = PdaBadgeRules.Audit([string.Empty, null], _ =>
        {
            asked = true;
            return true;
        });

        audit.Orphans.Should().Be(2);
        asked.Should().BeFalse();
    }

    // ---- "Clear all"

    // stands in for NotificationManager.Group (the rules are generic so that they need no game type)
    private enum FakeGroup
    {
        // Encyclopedia is not the zero value on purpose: a ClearAll that filtered on default(TGroup) or ignored its group would pass every test with the zero value
        Blueprints,
        Log,
        Encyclopedia
    }

    private static (FakeGroup, string) N(FakeGroup group, string key)
    {
        return (group, key);
    }

    [TestMethod]
    public void NothingToClearHidesTheClearAllButton()
    {
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 0, primaryDeviceIsGamepad: false).Should().BeFalse();
    }

    [TestMethod]
    public void OneOrMoreBadgesShowTheClearAllButton()
    {
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 1, primaryDeviceIsGamepad: false).Should().BeTrue();
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 2, primaryDeviceIsGamepad: false).Should().BeTrue();
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 300, primaryDeviceIsGamepad: false).Should().BeTrue();
    }

    [TestMethod]
    public void ANegativeCountNeverShowsTheButton()
    {
        PdaBadgeRules.ShouldShowClearAll(badgeCount: -1, primaryDeviceIsGamepad: false).Should().BeFalse();
        PdaBadgeRules.ShouldShowClearAll(badgeCount: int.MinValue, primaryDeviceIsGamepad: false).Should().BeFalse();
    }

    [TestMethod]
    public void AGamepadPlayerDoesNotGetAButtonThatCannotBePressed()
    {
        // the PDA switches its pointer raycaster off for a primary gamepad, a visible button would be dead
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 5, primaryDeviceIsGamepad: true).Should().BeFalse();
        PdaBadgeRules.ShouldShowClearAll(badgeCount: 0, primaryDeviceIsGamepad: true).Should().BeFalse();
    }

    [TestMethod]
    public void ClearingNothingRemovesNothingAndReturnsZero()
    {
        int calls = 0;
        bool Remove(string key)
        {
            calls++;
            return true;
        }

        PdaBadgeRules.ClearAll(Array.Empty<(FakeGroup, string)>(), FakeGroup.Encyclopedia, Remove).Should().Be(0);
        PdaBadgeRules.ClearAll(null, FakeGroup.Encyclopedia, Remove).Should().Be(0);
        PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, "Jellyray")], FakeGroup.Encyclopedia, null).Should().Be(0);
        calls.Should().Be(0);
    }

    [TestMethod]
    public void ClearAllRemovesEveryBadgeOfTheGroupAndCountsThem()
    {
        List<string> removed = [];

        int cleared = PdaBadgeRules.ClearAll(
            [N(FakeGroup.Encyclopedia, "Jellyray"), N(FakeGroup.Encyclopedia, "Reefback"), N(FakeGroup.Encyclopedia, "Seamoth")],
            FakeGroup.Encyclopedia,
            key =>
            {
                removed.Add(key);
                return true;
            });

        cleared.Should().Be(3);
        removed.Should().BeEquivalentTo(["Jellyray", "Reefback", "Seamoth"]);
    }

    [TestMethod]
    public void ClearAllLeavesTheOtherGroupsAlone()
    {
        // the same key can be a badge in two groups: only the databank's goes
        List<string> removed = [];

        int cleared = PdaBadgeRules.ClearAll(
            [N(FakeGroup.Blueprints, "Seamoth"), N(FakeGroup.Encyclopedia, "Jellyray"), N(FakeGroup.Log, "Jellyray"), N(FakeGroup.Log, "Radio1")],
            FakeGroup.Encyclopedia,
            key =>
            {
                removed.Add(key);
                return true;
            });

        cleared.Should().Be(1);
        removed.Should().Equal("Jellyray");
    }

    [TestMethod]
    public void TheKeysAreReadToTheEndBeforeTheFirstRemoval()
    {
        List<string> trace = [];
        IEnumerable<(FakeGroup, string)> Source()
        {
            foreach (string key in new[] { "a", "b", "c" })
            {
                trace.Add("read:" + key);
                yield return N(FakeGroup.Encyclopedia, key);
            }
        }

        PdaBadgeRules.ClearAll(Source(), FakeGroup.Encyclopedia, key =>
        {
            trace.Add("remove:" + key);
            return true;
        });

        trace.Should().Equal("read:a", "read:b", "read:c", "remove:a", "remove:b", "remove:c");
    }

    [TestMethod]
    public void ClearAllSurvivesARemovalThatChangesTheStoreItReadFrom()
    {
        // a List throws when it is changed while it is walked, like the game's dictionary does: the snapshot is what keeps the removal from walking the store
        List<(FakeGroup, string)> store = [N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Blueprints, "x"), N(FakeGroup.Encyclopedia, "b")];

        int cleared = PdaBadgeRules.ClearAll(store, FakeGroup.Encyclopedia, key => store.RemoveAll(entry => entry.Item1 == FakeGroup.Encyclopedia && entry.Item2 == key) > 0);

        cleared.Should().Be(2);
        store.Should().Equal(N(FakeGroup.Blueprints, "x"));
    }

    [TestMethod]
    public void ClearAllDoesNotDependOnTheOrderOfTheKeys()
    {
        (FakeGroup, string)[] forward = [N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Encyclopedia, "b"), N(FakeGroup.Encyclopedia, "c")];
        List<string> removedForward = [];
        List<string> removedBackward = [];

        int clearedForward = PdaBadgeRules.ClearAll(forward, FakeGroup.Encyclopedia, key =>
        {
            removedForward.Add(key);
            return true;
        });
        int clearedBackward = PdaBadgeRules.ClearAll(Enumerable.Reverse(forward), FakeGroup.Encyclopedia, key =>
        {
            removedBackward.Add(key);
            return true;
        });

        clearedForward.Should().Be(clearedBackward);
        removedForward.Should().BeEquivalentTo(removedBackward);
    }

    [TestMethod]
    public void ASecondClearAllIsANoOp()
    {
        List<(FakeGroup, string)> store = [N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Encyclopedia, "b")];
        bool Remove(string key)
        {
            return store.RemoveAll(entry => entry.Item1 == FakeGroup.Encyclopedia && entry.Item2 == key) > 0;
        }

        PdaBadgeRules.ClearAll(store, FakeGroup.Encyclopedia, Remove).Should().Be(2);
        PdaBadgeRules.ClearAll(store, FakeGroup.Encyclopedia, Remove).Should().Be(0);
    }

    [TestMethod]
    public void ADuplicateKeyIsRemovedOnce()
    {
        int calls = 0;

        int cleared = PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Encyclopedia, "a")], FakeGroup.Encyclopedia, _ =>
        {
            calls++;
            return true;
        });

        cleared.Should().Be(1);
        calls.Should().Be(1);
    }

    [TestMethod]
    public void KeysThatDifferOnlyInCaseAreBothCleared()
    {
        // NotificationId compares keys by ordinal: these are two notifications (the audit calls it a case duplicate), a Clear all that kept one would leave a number on the tab
        List<string> removed = [];

        int cleared = PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, "Jellyray"), N(FakeGroup.Encyclopedia, "jellyray")], FakeGroup.Encyclopedia, key =>
        {
            removed.Add(key);
            return true;
        });

        cleared.Should().Be(2);
        removed.Should().BeEquivalentTo(["Jellyray", "jellyray"]);
    }

    [TestMethod]
    public void ABadgeThatVanishedBeforeItsRemovalIsNotCounted()
    {
        int cleared = PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Encyclopedia, "gone"), N(FakeGroup.Encyclopedia, "c")], FakeGroup.Encyclopedia, key => key != "gone");

        cleared.Should().Be(2);
    }

    [TestMethod]
    public void ABadgeThatArrivesDuringTheClearIsKept()
    {
        List<(FakeGroup, string)> store = [N(FakeGroup.Encyclopedia, "a"), N(FakeGroup.Encyclopedia, "b")];
        bool arrived = false;

        int cleared = PdaBadgeRules.ClearAll(store, FakeGroup.Encyclopedia, key =>
        {
            if (!arrived)
            {
                arrived = true;
                store.Add(N(FakeGroup.Encyclopedia, "late"));
            }

            return store.RemoveAll(entry => entry.Item1 == FakeGroup.Encyclopedia && entry.Item2 == key) > 0;
        });

        cleared.Should().Be(2);
        store.Should().Equal(N(FakeGroup.Encyclopedia, "late"));
    }

    [TestMethod]
    public void AnEmptyOrMissingKeyIsNeverHandedToTheGame()
    {
        List<string> removed = [];

        int cleared = PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, string.Empty), N(FakeGroup.Encyclopedia, null), N(FakeGroup.Encyclopedia, "a")], FakeGroup.Encyclopedia, key =>
        {
            removed.Add(key);
            return true;
        });

        cleared.Should().Be(1);
        removed.Should().Equal("a");
    }

    [TestMethod]
    public void ABadgeOfAnEntryThisClientDoesNotHaveIsClearedLikeAnyOther()
    {
        // an orphan counts in the number on the tab (GetCount) and no look can ever clear it: Clear all asks no question about the entry
        PdaBadgeRules.ClearAll([N(FakeGroup.Encyclopedia, "Ghost")], FakeGroup.Encyclopedia, _ => true).Should().Be(1);
    }

    // ---- where the button goes

    [TestMethod]
    public void TheButtonSitsRightBehindTheHeaderText()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 800f, minWidth: 60f);

        placement.Left.Should().Be(224f);
        placement.Width.Should().Be(120f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void TheButtonIsNarrowedBeforeItPassesTheEdgeOfTheList()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 300f, minWidth: 60f);

        placement.Left.Should().Be(224f);
        placement.Width.Should().Be(76f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void WithoutRoomTheButtonKeepsItsLeastWidthAndSaysItDoesNotFit()
    {
        // never moved onto the header text
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 250f, minWidth: 60f);

        placement.Left.Should().Be(224f);
        placement.Width.Should().Be(60f);
        placement.Fits.Should().BeFalse();
    }

    [TestMethod]
    public void ALabelThatNeedsLessThanTheLeastWidthStillGetsTheLeastWidth()
    {
        PdaBadgeRules.PlaceClearAllButton(textRight: 0f, gap: 10f, wantedWidth: 20f, limitRight: 500f, minWidth: 60f).Width.Should().Be(60f);
    }

    [TestMethod]
    public void AButtonThatEndsExactlyAtTheEdgeOfTheListFits()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 344f, minWidth: 60f);

        placement.Width.Should().Be(120f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void ARoomOfExactlyTheLeastWidthFits()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 284f, minWidth: 60f);

        placement.Width.Should().Be(60f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void AnEdgeOfTheListLeftOfTheHeaderTextDoesNotFitAndDoesNotMoveTheButton()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButton(textRight: 200f, gap: 24f, wantedWidth: 120f, limitRight: 100f, minWidth: 60f);

        placement.Left.Should().Be(224f);
        placement.Width.Should().Be(60f);
        placement.Fits.Should().BeFalse();
    }

    [TestMethod]
    public void AHeaderThatIsRightOfTheListLetsTheButtonSitOverTheList()
    {
        // the numbers of the first live run: the list's right edge 138 left of the middle, the header text from -59 to +59
        PdaBadgeRules.ShouldPlaceOverList(listLeft: -450f, listRight: -138f, textLeft: -59f, gap: 24f, minWidth: 72f).Should().BeTrue();
    }

    [TestMethod]
    public void AHeaderThatStartsAtTheListDoesNotLetTheButtonSitOverIt()
    {
        PdaBadgeRules.ShouldPlaceOverList(listLeft: -450f, listRight: -138f, textLeft: -440f, gap: 24f, minWidth: 72f).Should().BeFalse();
    }

    [TestMethod]
    public void ARoomThatIsExactlyTheLeastWidthStillLetsTheButtonSitOverTheList()
    {
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 0f, listRight: 500f, textLeft: 96f, gap: 24f, minWidth: 72f).Should().BeTrue();
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 0f, listRight: 500f, textLeft: 95.9f, gap: 24f, minWidth: 72f).Should().BeFalse();
    }

    [TestMethod]
    public void ANotAListOrANumberThatIsNotOneNeverPlacesTheButtonOverTheList()
    {
        PdaBadgeRules.ShouldPlaceOverList(listLeft: float.NaN, listRight: 10f, textLeft: 300f, gap: 24f, minWidth: 72f).Should().BeFalse();
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 100f, listRight: 50f, textLeft: 300f, gap: 24f, minWidth: 72f).Should().BeFalse();
    }

    [TestMethod]
    public void AnInfiniteMeasureNeverPlacesTheButtonOverTheList()
    {
        // NaN fails every comparison on its own; the infinities are what the IsFinite guards are for (without them each of these is true)
        PdaBadgeRules.ShouldPlaceOverList(listLeft: float.NegativeInfinity, listRight: 500f, textLeft: 0f, gap: 24f, minWidth: 72f).Should().BeFalse();
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 0f, listRight: float.PositiveInfinity, textLeft: 300f, gap: 24f, minWidth: 72f).Should().BeFalse();
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 0f, listRight: 500f, textLeft: float.PositiveInfinity, gap: 24f, minWidth: 72f).Should().BeFalse();
        PdaBadgeRules.ShouldPlaceOverList(listLeft: 0f, listRight: float.NaN, textLeft: 300f, gap: 24f, minWidth: 72f).Should().BeFalse();
    }

    [TestMethod]
    public void OverTheListTheButtonStartsAtTheLeftEndOfTheList()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButtonOverList(listLeft: -450f, listRight: -138f, textLeft: -59f, gap: 24f, wantedWidth: 120f, minWidth: 72f);

        placement.Left.Should().Be(-450f);
        placement.Width.Should().Be(120f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void OverTheListTheButtonStaysClearOfTheHeaderText()
    {
        // the text starts 100 right of the list's left end: 100 - 24 = 76 of room, narrower than the 120 the label wants
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButtonOverList(listLeft: 0f, listRight: 500f, textLeft: 100f, gap: 24f, wantedWidth: 120f, minWidth: 72f);

        placement.Width.Should().Be(76f);
        placement.Fits.Should().BeTrue();
    }

    [TestMethod]
    public void OverTheListTheButtonStaysInsideTheListToo()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButtonOverList(listLeft: 0f, listRight: 90f, textLeft: 900f, gap: 24f, wantedWidth: 120f, minWidth: 72f);

        placement.Width.Should().Be(90f);
    }

    [TestMethod]
    public void OverTheListWithoutRoomTheButtonKeepsItsLeastWidthAndSaysItDoesNotFit()
    {
        PdaBadgeRules.ButtonPlacement placement = PdaBadgeRules.PlaceClearAllButtonOverList(listLeft: 0f, listRight: 50f, textLeft: 900f, gap: 24f, wantedWidth: 120f, minWidth: 72f);

        placement.Left.Should().Be(0f);
        placement.Width.Should().Be(72f);
        placement.Fits.Should().BeFalse();
    }

    [TestMethod]
    public void TheButtonKeepsItsHeightWhenThereIsRoom()
    {
        // centred on the header line at 100, the list starts at 70: 30 above it, so 60 of room
        PdaBadgeRules.ClampButtonHeight(wantedHeight: 40f, centerY: 100f, listTopY: 70f, minHeight: 24f).Should().Be(40f);
    }

    [TestMethod]
    public void TheButtonIsShortenedBeforeItCoversTheFirstRowOfTheList()
    {
        // the list starts 10 below the line: the button may reach 10 down, so it may be 20 tall
        PdaBadgeRules.ClampButtonHeight(wantedHeight: 40f, centerY: 100f, listTopY: 90f, minHeight: 10f).Should().Be(20f);
    }

    [TestMethod]
    public void AShortenedButtonIsNeverBelowTheLeastHeight()
    {
        PdaBadgeRules.ClampButtonHeight(wantedHeight: 40f, centerY: 100f, listTopY: 95f, minHeight: 24f).Should().Be(24f);
    }

    [TestMethod]
    public void AListThatStartsAboveTheHeaderLineLeavesTheHeightAlone()
    {
        // nothing to protect, and no height would help
        PdaBadgeRules.ClampButtonHeight(wantedHeight: 40f, centerY: 100f, listTopY: 120f, minHeight: 24f).Should().Be(40f);
        PdaBadgeRules.ClampButtonHeight(wantedHeight: 40f, centerY: 100f, listTopY: float.NegativeInfinity, minHeight: 24f).Should().Be(40f);
    }

    [TestMethod]
    public void OnlyANumberIsAMeasure()
    {
        PdaBadgeRules.IsFinite(0f).Should().BeTrue();
        PdaBadgeRules.IsFinite(-512.5f).Should().BeTrue();
        PdaBadgeRules.IsFinite(float.NaN).Should().BeFalse();
        PdaBadgeRules.IsFinite(float.PositiveInfinity).Should().BeFalse();
        PdaBadgeRules.IsFinite(float.NegativeInfinity).Should().BeFalse();
    }
}
