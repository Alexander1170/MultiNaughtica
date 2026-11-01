using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Layers;
using LiteNetLib.Utils;
using Nitrox.Model.Core;
using Nitrox.Model.Subnautica;
using NitroxClient.Communication.Exceptions;
using NitroxClient.Communication.NetworkingLayer.LiteNetLib;

namespace NitroxClient.Communication;

/// <summary>
///     The client of this fork tells the player which build the server is when the server refuses the connection (a stock Nitrox server, a server of another co-op zip).
/// </summary>
[TestClass]
public class LiteNetLibClientRefusalTest
{
    /// <summary>
    ///     Starts a stand-in server on a free port that refuses every request the way <paramref name="refuse" /> says, connects a real <see cref="LiteNetLibClient" /> and returns
    ///     what <see cref="LiteNetLibClient.StartAsync" /> did.
    /// </summary>
    /// <param name="withholdClientPollsFor">After the slow answer was sent, the client is not polled for this long: LiteNetLib has the answer (the state of the peer changed) but the game has not
    /// delivered the event yet, as in a frame that takes long.</param>
    private static async Task<Exception> ConnectToRefusingServer(Action<ConnectionRequest> refuse, TimeSpan? answerAfter = null, TimeSpan withholdClientPollsFor = default)
    {
        EventBasedNetListener serverListener = new();
        ConnectionRequest held = null;
        Stopwatch sinceHeld = new();
        serverListener.ConnectionRequestEvent += request =>
        {
            if (answerAfter == null)
            {
                refuse(request);
            }
            else if (held == null)
            {
                // the clock of the slow answer starts when the request arrives, not when the test started: a loaded machine cannot shorten or stretch it relative to the client
                held = request;
                sinceHeld.Restart();
            }
        };
        // A Release client wraps its packets in the checksum layer and drops what comes without one, so a Release run needs the stand-in to use it too
        NetManager server = new(serverListener, NitroxEnvironment.IsReleaseMode ? new Crc32cLayer() : null) { IPv6Enabled = false };
        server.Start(IPAddress.Loopback, IPAddress.IPv6None, 0).Should().BeTrue();
        LiteNetLibClient client = new(new PacketReceiver());
        using CancellationTokenSource stop = new();
        // Both sides only deliver their events when they are polled, the game does it once per frame
        Stopwatch sinceAnswer = new();
        Task poller = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                server.PollEvents();
                if (!sinceAnswer.IsRunning || sinceAnswer.Elapsed >= withholdClientPollsFor)
                {
                    client.PollEvents();
                }
                if (held != null && sinceHeld.Elapsed >= answerAfter)
                {
                    // a slow answer: the server sits on the request for a while (a slow link, a slow frame of the game)
                    refuse(held);
                    held = null;
                    sinceAnswer.Restart();
                }
                Thread.Sleep(5);
            }
        });

        try
        {
            await client.StartAsync("127.0.0.1", server.LocalPort);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            stop.Cancel();
            await poller;
            client.Stop();
            server.Stop();
        }
    }

    [TestMethod]
    public async Task AServerThatNamesNoKeyIsStockNitrox()
    {
        Exception result = await ConnectToRefusingServer(request => request.Reject());

        ClientConnectionRejectedException refusal = result.Should().BeOfType<ClientConnectionRejectedException>().Subject;
        refusal.ServerKey.Should().BeNull();
        refusal.Message.Should().Contain("not running this co-op build").And.Contain("stock Nitrox").And.Contain(CoopProtocol.Describe()).And.Contain("same co-op zip");
    }

    [TestMethod]
    public async Task AServerOfAnotherCoopBuildIsNamed()
    {
        const string OTHER_KEY = "nitrox-coop/1.9.0.0-coop.7/0123456789ab";

        Exception result = await ConnectToRefusingServer(request =>
        {
            NetDataWriter writer = new();
            writer.Put(OTHER_KEY);
            request.Reject(writer);
        });

        ClientConnectionRejectedException refusal = result.Should().BeOfType<ClientConnectionRejectedException>().Subject;
        refusal.ServerKey.Should().Be(OTHER_KEY);
        refusal.Message.Should().Contain("The server runs another build: co-op 1.9.0.0-coop.7 (protocol 0123456789ab)").And.Contain(CoopProtocol.Describe());
    }

    [TestMethod]
    public async Task ARefusalThatComesAfterTheFirstTwoSecondsIsStillShown()
    {
        // The client used to give up after 2 s with "unable to connect" although the refusal was on its way
        // Longer than the 2 s the client waits first, with half of what is left of the 5 s that LiteNetLib tries to connect for (10 attempts, 500 ms apart) as room for a slow machine
        Exception result = await ConnectToRefusingServer(request => request.Reject(), TimeSpan.FromSeconds(2.5));

        result.Should().BeOfType<ClientConnectionRejectedException>().Which.Message.Should().Contain("not running this co-op build");
    }

    [TestMethod]
    public async Task ARefusalThatIsOnItsWayWhenAWaitEndsIsStillShown()
    {
        // The refusal reaches LiteNetLib at 2.1 s but the game delivers it only 0.8 s later. The client's second wait ends at 2.5 s, in between: the peer is no longer connecting
        // and the event is not there yet. The client used to report "unable to connect" then (found as a failure in about 40% of the runs of a test that sent the refusal at 3 s,
        // the moment another wait ended).
        Exception result = await ConnectToRefusingServer(request => request.Reject(), TimeSpan.FromSeconds(2.1), TimeSpan.FromSeconds(0.8));

        result.Should().BeOfType<ClientConnectionRejectedException>().Which.Message.Should().Contain("not running this co-op build");
    }

    [TestMethod]
    public async Task NobodyAnsweringEndsWithoutAConnectionAndWithoutARefusal()
    {
        // Nothing listens on the port: StartAsync returns normally and the caller (Disconnected.StartClientAsync) turns "not connected" into the usual "unable to connect"
        // failure. A silent success must not be possible: the client reports that it is not connected.
        int freePort;
        using (System.Net.Sockets.UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            freePort = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        }

        LiteNetLibClient client = new(new PacketReceiver());
        using CancellationTokenSource stop = new();
        Task poller = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                client.PollEvents();
                Thread.Sleep(5);
            }
        });

        try
        {
            Stopwatch took = Stopwatch.StartNew();
            Func<Task> connect = () => client.StartAsync("127.0.0.1", freePort);

            await connect.Should().NotThrowAsync();

            client.IsConnected.Should().BeFalse();
            took.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the wait for a late answer is bounded");
        }
        finally
        {
            stop.Cancel();
            await poller;
            client.Stop();
        }
    }

    [TestMethod]
    public async Task AServerThatAcceptsOnlyOurKeyIsNotRefused()
    {
        // The connection request carries exactly the key of this build: a stand-in that checks it and accepts lets the client through
        string sentKey = null;
        Exception result = await ConnectToRefusingServer(request =>
        {
            sentKey = request.Data.GetString();
            if (sentKey == CoopProtocol.ConnectionKey)
            {
                request.Accept();
            }
            else
            {
                request.Reject();
            }
        });

        sentKey.Should().Be(CoopProtocol.ConnectionKey);
        result.Should().BeNull();
    }
}
