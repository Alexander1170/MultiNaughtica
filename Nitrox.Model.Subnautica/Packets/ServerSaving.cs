using System;
using Nitrox.Model.Packets;

namespace Nitrox.Model.Subnautica.Packets;

/// <summary>
/// The server starts to save the world (the timed autosave, the <c>save</c> command, the save when the last player leaves). Sent to every client, which shows a small "Saving..." indicator in the
/// top left corner for a moment (item a, <c>AutosaveIndicator</c>). Nothing is asked of the client and nothing is saved by it.
/// </summary>
[Serializable]
public class ServerSaving : Packet
{
    /// <summary>
    /// The number of this save in the life of the server process (1 for the first one): a client that logs it and a test that counts the saves of the server can tell that every save was announced once.
    /// </summary>
    public int Number { get; }

    public ServerSaving(int number)
    {
        Number = number;
    }
}
