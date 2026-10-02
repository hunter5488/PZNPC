using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SodCoop.Net
{
    /// <summary>
    /// The host is player 0 and the authority: it assigns ids, relays every client's state to the
    /// other clients, and owns the world clock.
    /// </summary>
    public sealed class CoopHost : CoopSession
    {
        readonly TcpListener _listener;
        readonly string _hostName;
        readonly string _cityId;
        readonly int _maxPlayers;
        readonly object _lock = new object();
        readonly Dictionary<byte, Connection> _peers = new Dictionary<byte, Connection>();
        volatile bool _disposed;

        public CoopHost(int port, string hostName, string cityId, int maxPlayers = 4, IPAddress bindAddress = null)
        {
            _hostName = hostName;
            _cityId = cityId;
            _maxPlayers = Math.Max(2, Math.Min(maxPlayers, 32));
            _listener = new TcpListener(bindAddress ?? IPAddress.Any, port);
            _listener.Start();
            new Thread(AcceptLoop) { IsBackground = true, Name = "SodCoop accept" }.Start();
        }

        /// <summary>The port actually bound, useful when constructed with port 0.</summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public override bool IsHost => true;
        public override byte? LocalPlayerId => HostPlayerId;

        public int ClientCount
        {
            get { lock (_lock) return _peers.Count; }
        }

        public override void SendLocalState(PlayerState state)
        {
            var s = state.Clone();
            s.PlayerId = HostPlayerId;
            Broadcast(s, except: null);
        }

        public override void SendChat(string text) =>
            Broadcast(new Chat { PlayerId = HostPlayerId, Text = text }, except: null);

        public void SendWorldTime(double gameTime) =>
            Broadcast(new WorldTime { GameTime = gameTime }, except: null);

        void AcceptLoop()
        {
            while (!_disposed)
            {
                TcpClient tcp;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                        Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Text = "Stopped listening: " + ex.Message });
                    return;
                }

                var conn = new Connection(tcp, OnMessage, OnClosed);
                conn.Start();
            }
        }

        void OnMessage(Connection conn, IMessage message)
        {
            if (!conn.Handshaken)
            {
                if (message is Hello hello)
                    HandleHello(conn, hello);
                else
                    conn.Close(null);
                return;
            }

            switch (message)
            {
                case PlayerState state:
                    // Never trust the id a client claims.
                    state.PlayerId = conn.PlayerId;
                    Broadcast(state, except: conn);
                    Enqueue(new NetEvent { Kind = NetEventKind.PlayerState, PlayerId = conn.PlayerId, State = state });
                    break;

                case Chat chat:
                    chat.PlayerId = conn.PlayerId;
                    Broadcast(chat, except: conn);
                    Enqueue(new NetEvent { Kind = NetEventKind.Chat, PlayerId = conn.PlayerId, Text = chat.Text });
                    break;

                // Anything else from a client is ignored: only the host sends world state.
            }
        }

        void HandleHello(Connection conn, Hello hello)
        {
            if (hello.ProtocolVersion != Framing.ProtocolVersion)
            {
                conn.SendAndClose(new Reject
                {
                    Reason = $"Version mismatch: host has protocol {Framing.ProtocolVersion}, you have {hello.ProtocolVersion}. Install the same mod version.",
                });
                return;
            }

            Welcome welcome;
            List<Connection> others;
            lock (_lock)
            {
                byte id = 0;
                for (byte candidate = 1; candidate < _maxPlayers; candidate++)
                {
                    if (!_peers.ContainsKey(candidate))
                    {
                        id = candidate;
                        break;
                    }
                }
                if (id == 0)
                {
                    conn.SendAndClose(new Reject { Reason = $"Server is full ({_maxPlayers} players)." });
                    return;
                }

                conn.PlayerId = id;
                conn.PlayerName = string.IsNullOrWhiteSpace(hello.PlayerName) ? $"Player {id}" : hello.PlayerName.Trim();
                conn.Handshaken = true;

                welcome = new Welcome { YourId = id, HostCityId = _cityId };
                welcome.Players.Add(new PlayerInfo(HostPlayerId, _hostName));
                foreach (var p in _peers.Values)
                    welcome.Players.Add(new PlayerInfo(p.PlayerId, p.PlayerName));

                others = _peers.Values.ToList();
                _peers[id] = conn;
            }

            conn.Send(welcome);
            var joined = new PlayerJoined { PlayerId = conn.PlayerId, Name = conn.PlayerName };
            foreach (var other in others)
                other.Send(joined);

            Enqueue(new NetEvent { Kind = NetEventKind.PlayerJoined, PlayerId = conn.PlayerId, Text = conn.PlayerName });
            if (!string.Equals(hello.CityId, _cityId, StringComparison.Ordinal))
            {
                Enqueue(new NetEvent
                {
                    Kind = NetEventKind.Warning,
                    PlayerId = conn.PlayerId,
                    Text = $"{conn.PlayerName} is in a different city ('{hello.CityId}', you are in '{_cityId}'). You will not see each other in the right places.",
                });
            }
        }

        void OnClosed(Connection conn, Exception error)
        {
            bool removed;
            List<Connection> others;
            lock (_lock)
            {
                removed = conn.Handshaken && _peers.TryGetValue(conn.PlayerId, out var current) && current == conn
                    && _peers.Remove(conn.PlayerId);
                others = _peers.Values.ToList();
            }
            if (!removed || _disposed)
                return;

            var left = new PlayerLeft { PlayerId = conn.PlayerId };
            foreach (var other in others)
                other.Send(left);

            Enqueue(new NetEvent
            {
                Kind = NetEventKind.PlayerLeft,
                PlayerId = conn.PlayerId,
                Text = error == null ? conn.PlayerName : $"{conn.PlayerName} ({error.Message})",
            });
        }

        void Broadcast(IMessage message, Connection except)
        {
            List<Connection> targets;
            lock (_lock)
                targets = _peers.Values.Where(p => p != except).ToList();
            foreach (var p in targets)
                p.Send(message);
        }

        public override void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _listener.Stop(); } catch { }

            List<Connection> all;
            lock (_lock)
            {
                all = _peers.Values.ToList();
                _peers.Clear();
            }
            foreach (var p in all)
                p.Close(null);
        }
    }
}
