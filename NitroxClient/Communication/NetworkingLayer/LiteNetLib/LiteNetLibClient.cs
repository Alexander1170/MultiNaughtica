using System;
using System.Buffers;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Layers;
using LiteNetLib.Utils;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica;
using NitroxClient.Communication.Exceptions;
using NitroxClient.Communication.Abstract;
using NitroxClient.Debuggers;
using NitroxClient.MonoBehaviours;
using NitroxClient.MonoBehaviours.Gui.Modals;
using Nitrox.Model.Networking;
using Nitrox.Model.Packets;

namespace NitroxClient.Communication.NetworkingLayer.LiteNetLib;

public class LiteNetLibClient : IClient
{
    private readonly NetManager client;

    private readonly AutoResetEvent connectedEvent = new(false);
    private readonly NetDataWriter dataWriter = new();
    private readonly INetworkDebugger networkDebugger;
    private readonly PacketReceiver packetReceiver;
    private readonly FieldInfo manualModeFieldInfo = typeof(NetManager).GetField("_manualMode", BindingFlags.Instance | BindingFlags.NonPublic);
    private const int LATE_ANSWER_WAIT_MS = 500;
    private const int MAX_LATE_ANSWER_WAITS = 8;
    private const int ANSWER_DELIVERY_GRACE_MS = 1000;
    private volatile bool rejected;

    /// <summary>
    /// The attempt of the current <see cref="StartAsync"/> ended without ever connecting and the game has been told (the Disconnected event was delivered): nothing more is on its way.
    /// </summary>
    private volatile bool attemptEnded;
    private volatile string rejectionServerKey;

    public bool IsConnected { get; private set; }

    /// <summary>
    ///     The peer is trying to connect and has neither been accepted nor refused yet.
    /// </summary>
    private bool IsStillConnecting => client.FirstPeer is { } peer && (peer.ConnectionState & ConnectionState.Outgoing) != 0;
    public int PingInterval
    {
        get => client.PingInterval;
        set => client.PingInterval = value;
    }
    public Action<long> LatencyUpdateCallback;

    public LiteNetLibClient(PacketReceiver packetReceiver, INetworkDebugger networkDebugger = null)
    {
        this.packetReceiver = packetReceiver;
        this.networkDebugger = networkDebugger;
        EventBasedNetListener listener = new();
        listener.PeerConnectedEvent += Connected;
        listener.PeerDisconnectedEvent += Disconnected;
        listener.NetworkReceiveEvent += ReceivedNetworkData;
        listener.NetworkLatencyUpdateEvent += (peer, _) =>
        {
            LatencyUpdateCallback?.Invoke(peer.RemoteTimeDelta);
        };


        client = new NetManager(listener, NitroxEnvironment.IsReleaseMode ? new Crc32cLayer() : null)
        {
            UpdateTime = 15,
            ChannelsCount = (byte)typeof(Packet.UdpChannelId).GetEnumValues().Length,
            IPv6Enabled = true,
#if DEBUG
            DisconnectTimeout = 300_000, //Disables Timeout (for 5 min) for debug purpose (like if you jump though the server code)
#else
            DisconnectTimeout = 30_000, // 30 seconds; prevents false disconnects when post-sync game-loading stalls LiteNetLib briefly
#endif
        };
    }

    public async Task StartAsync(string ipAddress, int serverPort)
    {
        Log.Info($"Initializing LiteNetLibClient... {CoopProtocol.Describe()}, connection key {CoopProtocol.ConnectionKey}");
        rejected = false;
        attemptEnded = false;
        rejectionServerKey = null;
        // The end of an earlier attempt may have signalled after it returned
        connectedEvent.Reset();

        // ConfigureAwait(false) is needed because Unity uses a custom "UnitySynchronizationContext". Which makes async/await work like Unity coroutines.
        // Because this Task.Run is async-over-sync this would otherwise blocks the main thread as it wants to, without ConfigureAwait(false), continue on the same thread (i.e. main thread).
        await Task.Run(() =>
        {
            client.Start();
            client.Connect(ipAddress, serverPort, CoopProtocol.ConnectionKey);
        }).ConfigureAwait(false);

        connectedEvent.WaitOne(2000);
        connectedEvent.Reset();

        // The answer (the connection or the refusal) is delivered with a poll of the game's main thread: after a slow frame or over a slow link it comes late. While the peer is
        // still trying, wait a few seconds more instead of reporting "unable to connect" for an answer that is on its way.
        for (int i = 0; i < MAX_LATE_ANSWER_WAITS && !IsConnected && !rejected && !attemptEnded && IsStillConnecting; i++)
        {
            connectedEvent.WaitOne(LATE_ANSWER_WAIT_MS);
        }

        // The state of the peer changes on LiteNetLib's own thread but the event that tells us (connected, refused, failed) is only delivered by the next poll of the game's main
        // thread. A peer that is no longer "connecting" can therefore have an answer that is still on its way (a refusal that arrives just as one of the waits above ends was
        // reported as "unable to connect" without this): give the poll a moment to deliver it.
        if (!IsConnected && !rejected && !attemptEnded)
        {
            connectedEvent.WaitOne(ANSWER_DELIVERY_GRACE_MS);
        }

        if (rejected)
        {
            // The join screen throws the session away without stopping this transport: do it here so that a refused attempt leaves no running NetManager behind
            Stop();
            throw new ClientConnectionRejectedException(CoopProtocol.DescribeRefusal(rejectionServerKey), rejectionServerKey);
        }
    }

    public void Send(Packet packet)
    {
        byte[] packetData = packet.Serialize();
        dataWriter.Reset();
        dataWriter.Put(packetData.Length);
        dataWriter.Put(packetData);

        networkDebugger?.PacketSent(packet, dataWriter.Length);
        client.SendToAll(dataWriter, (byte)packet.UdpChannel, NitroxDeliveryMethod.ToLiteNetLib(packet.DeliveryMethod));
    }

    public void Stop()
    {
        IsConnected = false;
        client.Stop();
    }

    /// <summary>
    ///     This should be called <b>once</b> each game tick
    /// </summary>
    public void PollEvents() => client.PollEvents();

#if DEBUG
    /// <summary>
    ///     Sync test harness: LiteNetLib's smoothed round trip time (ms) to the server, or -1 when not connected.
    /// </summary>
    public int RoundTripMs => client.FirstPeer?.RoundTripTime ?? -1;
#endif

    private void ReceivedNetworkData(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        int packetDataLength = reader.GetInt();
        byte[] packetData = ArrayPool<byte>.Shared.Rent(packetDataLength);
        try
        {
            reader.GetBytes(packetData, packetDataLength);
            Packet packet = Packet.Deserialize(packetData);
            packetReceiver.Add(packet);
            networkDebugger?.PacketReceived(packet, packetDataLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packetData, true);
        }
    }

    private void Connected(NetPeer peer)
    {
        // IsConnected must happen before Set() so that its state is noticed WHEN we unblock the thread (cf. connectedEvent.WaitOne(...))
        IsConnected = true;
        connectedEvent.Set();
        Log.Info("Connected to server");
    }

    private void Disconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        // An attempt that ends without ever connecting (failed, refused) wakes the thread that waits for the answer, at the end of this method; the end of a running session has no one waiting
        bool endsAnAttempt = !IsConnected;

        if (disconnectInfo.Reason == DisconnectReason.ConnectionRejected)
        {
            // A refused connection is the answer of a server of another build; the server of this fork names its own key, a stock server names nothing
            string serverKey = null;
            try
            {
                if (disconnectInfo.AdditionalData != null && !disconnectInfo.AdditionalData.IsNull && disconnectInfo.AdditionalData.AvailableBytes > 0)
                {
                    serverKey = disconnectInfo.AdditionalData.GetString();
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"The refusal of the server could not be read: {ex.Message}");
            }

            rejectionServerKey = serverKey;
            rejected = true;
            Log.Error($"The server refused the connection. {CoopProtocol.DescribeRefusal(serverKey)}");
            connectedEvent.Set();
        }

        // Check must happen before IsConnected is set to false, so that it doesn't send an exception when we aren't even ingame
        if (Multiplayer.Active)
        {
            Modal.Get<LostConnectionModal>()?.Show();
        }

        IsConnected = false;
        Log.Info("Disconnected from server");
        if (endsAnAttempt)
        {
            // after the state above (rejected, IsConnected): the waiting thread looks at it as soon as it wakes
            attemptEnded = true;
            connectedEvent.Set();
        }
    }

    internal void ForceUpdate()
    {
        int pingInterval = PingInterval;
        // Set PingInterval to 0 so another ping is sent immediately
        PingInterval = 0;
        // ManualUpdate requires the client to have _manualMode set to true so we temporarily do so
        manualModeFieldInfo.SetValue(client, true);
        client.ManualUpdate(0);
        manualModeFieldInfo.SetValue(client, false);
        // We set it back to its high value so another ping isn't sent while we're waiting for the previous one
        PingInterval = pingInterval;
    }
}
