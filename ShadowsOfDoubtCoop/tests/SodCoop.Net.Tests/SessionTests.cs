using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using SodCoop.Net;
using Xunit;

namespace SodCoop.Net.Tests
{
    /// <summary>Real host and clients talking over loopback TCP.</summary>
    public class SessionTests : IDisposable
    {
        readonly List<CoopSession> _sessions = new List<CoopSession>();

        T Track<T>(T session) where T : CoopSession
        {
            _sessions.Add(session);
            return session;
        }

        CoopHost StartHost(string city = "city-1", int maxPlayers = 4) =>
            Track(new CoopHost(0, "HostPlayer", city, maxPlayers, IPAddress.Loopback));

        CoopClient Join(CoopHost host, string name, string city = "city-1") =>
            Track(new CoopClient("127.0.0.1", host.Port, name, city));

        /// <summary>Polls the session, collecting events, until one matches or the timeout expires.</summary>
        static NetEvent WaitFor(CoopSession session, List<NetEvent> seen, Func<NetEvent, bool> match, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                session.Poll(seen.Add);
                var hit = seen.FirstOrDefault(match);
                if (hit != null)
                    return hit;
                Thread.Sleep(5);
            }
            throw new TimeoutException("Timed out. Saw: " + string.Join("; ", seen));
        }

        [Fact]
        public void ClientHandshakeAssignsIdAndListsHost()
        {
            var host = StartHost();
            var client = Join(host, "Alice");
            var clientEvents = new List<NetEvent>();
            var hostEvents = new List<NetEvent>();

            var connected = WaitFor(client, clientEvents, e => e.Kind == NetEventKind.Connected);
            Assert.Equal(1, connected.PlayerId);
            Assert.Equal("city-1", connected.Text);
            Assert.Equal((byte?)1, client.LocalPlayerId);

            WaitFor(client, clientEvents, e => e.Kind == NetEventKind.PlayerJoined && e.PlayerId == 0 && e.Text == "HostPlayer");
            WaitFor(host, hostEvents, e => e.Kind == NetEventKind.PlayerJoined && e.PlayerId == 1 && e.Text == "Alice");
            Assert.DoesNotContain(clientEvents, e => e.Kind == NetEventKind.Warning);
        }

        [Fact]
        public void HostStateReachesClient()
        {
            var host = StartHost();
            var client = Join(host, "Alice");
            var clientEvents = new List<NetEvent>();
            WaitFor(client, clientEvents, e => e.Kind == NetEventKind.Connected);

            host.SendLocalState(new PlayerState { PlayerId = 99, X = 4, Y = 5, Z = 6, Yaw = 180 });

            var state = WaitFor(client, clientEvents, e => e.Kind == NetEventKind.PlayerState).State;
            Assert.Equal(0, state.PlayerId);
            Assert.Equal(4f, state.X);
            Assert.Equal(180f, state.Yaw);
        }

        [Fact]
        public void ClientStateIsRelayedToOtherClientsWithTrustedId()
        {
            var host = StartHost();
            var alice = Join(host, "Alice");
            var aliceEvents = new List<NetEvent>();
            WaitFor(alice, aliceEvents, e => e.Kind == NetEventKind.Connected);

            var bob = Join(host, "Bob");
            var bobEvents = new List<NetEvent>();
            WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.Connected);
            Assert.Equal((byte?)2, bob.LocalPlayerId);

            // Bob's welcome lists Alice; Alice is told Bob joined.
            WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.PlayerJoined && e.Text == "Alice");
            WaitFor(alice, aliceEvents, e => e.Kind == NetEventKind.PlayerJoined && e.Text == "Bob");

            alice.SendLocalState(new PlayerState { X = 42 });

            var atBob = WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.PlayerState);
            Assert.Equal(1, atBob.PlayerId);
            Assert.Equal(42f, atBob.State.X);

            var hostEvents = new List<NetEvent>();
            var atHost = WaitFor(host, hostEvents, e => e.Kind == NetEventKind.PlayerState);
            Assert.Equal(1, atHost.PlayerId);
            Assert.DoesNotContain(aliceEvents, e => e.Kind == NetEventKind.PlayerState);
        }

        [Fact]
        public void WorldTimeAndChatReachClient()
        {
            var host = StartHost();
            var client = Join(host, "Alice");
            var clientEvents = new List<NetEvent>();
            WaitFor(client, clientEvents, e => e.Kind == NetEventKind.Connected);

            host.SendWorldTime(13.5);
            host.SendChat("hello");

            Assert.Equal(13.5, WaitFor(client, clientEvents, e => e.Kind == NetEventKind.WorldTime).GameTime);
            Assert.Equal("hello", WaitFor(client, clientEvents, e => e.Kind == NetEventKind.Chat).Text);

            client.SendChat("hi back");
            var hostEvents = new List<NetEvent>();
            var chat = WaitFor(host, hostEvents, e => e.Kind == NetEventKind.Chat);
            Assert.Equal("hi back", chat.Text);
            Assert.Equal(1, chat.PlayerId);
        }

        [Fact]
        public void DisconnectIsAnnouncedAndIdIsReused()
        {
            var host = StartHost();
            var alice = Join(host, "Alice");
            var bob = Join(host, "Bob");
            var aliceEvents = new List<NetEvent>();
            var bobEvents = new List<NetEvent>();
            WaitFor(alice, aliceEvents, e => e.Kind == NetEventKind.Connected);
            WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.Connected);
            byte aliceId = alice.LocalPlayerId.Value;

            alice.Dispose();

            var hostEvents = new List<NetEvent>();
            WaitFor(host, hostEvents, e => e.Kind == NetEventKind.PlayerLeft && e.PlayerId == aliceId);
            WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.PlayerLeft && e.PlayerId == aliceId);
            Assert.Equal(1, host.ClientCount);

            var carol = Join(host, "Carol");
            var carolEvents = new List<NetEvent>();
            WaitFor(carol, carolEvents, e => e.Kind == NetEventKind.Connected);
            Assert.Equal((byte?)aliceId, carol.LocalPlayerId);
        }

        [Fact]
        public void HostShutdownDisconnectsClient()
        {
            var host = StartHost();
            var client = Join(host, "Alice");
            var events = new List<NetEvent>();
            WaitFor(client, events, e => e.Kind == NetEventKind.Connected);

            host.Dispose();

            WaitFor(client, events, e => e.Kind == NetEventKind.Disconnected);
        }

        [Fact]
        public void FullServerRejectsWithReason()
        {
            var host = StartHost(maxPlayers: 2);
            var alice = Join(host, "Alice");
            var aliceEvents = new List<NetEvent>();
            WaitFor(alice, aliceEvents, e => e.Kind == NetEventKind.Connected);

            var bob = Join(host, "Bob");
            var bobEvents = new List<NetEvent>();
            var gone = WaitFor(bob, bobEvents, e => e.Kind == NetEventKind.Disconnected);
            Assert.Contains("full", gone.Text);
            Assert.Null(bob.LocalPlayerId);
        }

        [Fact]
        public void CityMismatchWarnsBothSides()
        {
            var host = StartHost(city: "Ravenwood");
            var client = Join(host, "Alice", city: "Blackmoor");
            var clientEvents = new List<NetEvent>();
            var hostEvents = new List<NetEvent>();

            var warning = WaitFor(client, clientEvents, e => e.Kind == NetEventKind.Warning);
            Assert.Contains("Ravenwood", warning.Text);
            WaitFor(host, hostEvents, e => e.Kind == NetEventKind.Warning && e.Text.Contains("Blackmoor"));
        }

        [Fact]
        public void UnreachableHostReportsFailure()
        {
            // Bind and release a port so nothing is listening on it.
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var client = Track(new CoopClient("127.0.0.1", port, "Alice", "c", connectTimeoutMs: 2000));
            var events = new List<NetEvent>();
            var e = WaitFor(client, events, x => x.Kind == NetEventKind.Disconnected);
            Assert.Contains("Could not connect", e.Text);
        }

        public void Dispose()
        {
            foreach (var s in _sessions)
                s.Dispose();
        }
    }
}
