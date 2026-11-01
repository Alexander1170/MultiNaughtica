using System;

namespace NitroxClient.GameLogic;

/// <summary>
///     One reading of a server clock (its value and the server UTC moment of the reading), from which a client projects the clock to any later moment. See <see cref="GameClockRules" />.
/// </summary>
/// <remarks>
///     There is deliberately no way to add to a sample: <see cref="Register" /> replaces it, so a newer packet can never be counted on top of the time a client has already advanced.
/// </remarks>
public sealed class ServerClockSample
{
    /// <summary>
    ///     False until the first packet was registered.
    /// </summary>
    public bool IsSet { get; private set; }

    /// <summary>
    ///     The server's clock at <see cref="RegisteredAt" />, in seconds.
    /// </summary>
    public double RegisteredSeconds { get; private set; }

    /// <summary>
    ///     The server's UTC moment at which <see cref="RegisteredSeconds" /> was read.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    public void Register(double seconds, DateTimeOffset serverUtcOfReading)
    {
        RegisteredSeconds = seconds;
        RegisteredAt = serverUtcOfReading;
        IsSet = true;
    }

    /// <summary>
    ///     The server's clock at <paramref name="serverUtcNow" /> (the server's UTC clock as the caller estimates it).
    /// </summary>
    public double Project(DateTimeOffset serverUtcNow)
    {
        return GameClockRules.Project(RegisteredSeconds, (serverUtcNow - RegisteredAt).TotalSeconds);
    }
}
