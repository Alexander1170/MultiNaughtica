using System;
using NitroxClient.Communication.NetworkingLayer.LiteNetLib;
using Nitrox.Model.Networking;

namespace NitroxClient.GameLogic;

public partial class TimeManager
{
    private readonly NtpSyncer ntpSyncer;

    /// <summary>
    /// Whether or not the local player could obtain a correction from the global NTP server
    /// </summary>
    private bool clientOnlineMode;
    /// <summary>
    /// Local's UTC correction with a global NTP server ("real"). Client Correction = Real UTC Time - Client UTC Time
    /// </summary>
    private TimeSpan clientCorrection;


    /// <summary>
    /// Whether or not server could obtain a correction from the global NTP server
    /// </summary>
    private bool serverOnlineMode;
    /// <summary>
    /// Server's UTC correction with a global NTP server ("real"). Server Correction = Real UTC Time - Server UTC Time
    /// </summary>
    private TimeSpan serverCorrection;

    /// <summary>
    /// Correction Delta = Server UTC Time - Client UTC Time. Calculated thanks to <see cref="ClockSyncProcedure"/>
    /// </summary>
    private TimeSpan correctionDelta;

    /// <summary>
    /// What has to be added to this computer's UTC clock to get the server's UTC clock (<see cref="GameClockRules.ServerUtcOffset"/>)
    /// </summary>
    public TimeSpan ServerUtcOffset => GameClockRules.ServerUtcOffset(clientOnlineMode, serverOnlineMode, clientCorrection, serverCorrection, correctionDelta);

    public DateTimeOffset ServerUtcNow()
    {
        // With NTP on both sides: Server UTC Time + Server correction = Client UTC Time + Client correction, so Server UTC Time = Client UTC Time + Client correction - Server correction.
        // In any other case we can only rely on the clock sync procedure (Server UTC Time = Client UTC Time + correction delta)
        return DateTimeOffset.UtcNow + ServerUtcOffset;
    }

    public void SetCorrectionDelta(long remoteTimeDelta)
    {
        correctionDelta = new TimeSpan(remoteTimeDelta);
        Log.Info($"OFFLINE mode: delta = {correctionDelta}");
    }

    public void SetClientCorrectionData(bool clientOnlineMode, TimeSpan correctionOffset)
    {
        this.clientOnlineMode = clientOnlineMode;
        clientCorrection = correctionOffset;
        Log.Info($"Client ONLINE: correction = {correctionOffset}");
    }

    public void SetServerCorrectionData(bool serverOnlineMode, long serverUtcCorrectionTicks)
    {
        this.serverOnlineMode = serverOnlineMode;
        serverCorrection = new(serverUtcCorrectionTicks);
        Log.Info($"Server ONLINE: correction = {serverCorrection}");
    }

    /// <summary>
    /// The one retry of the NTP sync a client without NTP makes once it learns that the server has it (see <see cref="TimeManager.ProcessUpdate"/>).
    /// </summary>
    private bool ntpRetryAttempted;

    public void AttemptNtpSync()
    {
        // The syncer of this client is complete after the initial sync, so a later request is never handed to the callback below (NtpSyncer.Complete ends early). Every call would only start a
        // NetManager (a socket and a thread) that nothing stops again, once per TimeChange (every minute) for the rest of the session: one try is all it can do.
        if (ntpRetryAttempted)
        {
            return;
        }
        ntpRetryAttempted = true;

        ntpSyncer.Setup((onlineMode, correction) =>
        {
            if (onlineMode)
            {
                SetClientCorrectionData(onlineMode, correction);
            }
        });
        ntpSyncer.RequestNtpService();
    }
}
