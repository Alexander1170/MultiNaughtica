namespace NitroxClient.GameLogic.Simulation;

/// <summary>
/// Remembers the Bleeder attack that waits for the exclusive simulation lock of its creature (see <see cref="BleederClaims" />).
/// </summary>
public class BleederClaim : LockRequestContext
{
    public AttachAndSuck Attack { get; }

    public BleederClaim(AttachAndSuck attack)
    {
        Attack = attack;
    }
}
