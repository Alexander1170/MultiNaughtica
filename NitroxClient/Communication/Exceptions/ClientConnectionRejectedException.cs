using System;

namespace NitroxClient.Communication.Exceptions;

/// <summary>
///     The server answered the connection request with a refusal: it runs another build (stock Nitrox, an older co-op zip). The message is written for the player.
/// </summary>
public class ClientConnectionRejectedException : ClientConnectionFailedException
{
    /// <param name="serverKey">The connection key the server named in its refusal, if it named one (a stock server names none)</param>
    public ClientConnectionRejectedException(string message, string serverKey) : base(message)
    {
        ServerKey = serverKey;
    }

    public string ServerKey { get; }
}
