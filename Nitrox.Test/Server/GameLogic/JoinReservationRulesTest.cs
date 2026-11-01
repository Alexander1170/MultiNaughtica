namespace Nitrox.Server.Subnautica.Models.GameLogic;

/// <summary>
///     The heal rehearsal of 2026-10-26: both real players connected while the repaired world was still loading, were reserved as new players and got a new body, the lifepod and the starting
///     items although their saved players were known when their turn in the join queue came (<see cref="JoinReservationRules" />).
/// </summary>
[TestClass]
public class JoinReservationRulesTest
{
    [TestMethod]
    public void ASavedPlayerThatWasReservedAsNewBeforeTheWorldLoadedIsNotNew()
    {
        JoinReservationRules.IsBrandNewAtJoin(reservedAsBrandNew: true, knownAtJoin: true).Should().BeFalse();
    }

    [TestMethod]
    public void ANameNobodyKnowsStaysNew()
    {
        JoinReservationRules.IsBrandNewAtJoin(reservedAsBrandNew: true, knownAtJoin: false).Should().BeTrue();
    }

    [TestMethod]
    public void AKnownPlayerReservedAsKnownIsNotNew()
    {
        JoinReservationRules.IsBrandNewAtJoin(reservedAsBrandNew: false, knownAtJoin: true).Should().BeFalse();
    }

    [TestMethod]
    public void AReservationThatSaidKnownIsNeverMadeNewByTheJoin()
    {
        JoinReservationRules.IsBrandNewAtJoin(reservedAsBrandNew: false, knownAtJoin: false).Should().BeFalse();
    }
}
