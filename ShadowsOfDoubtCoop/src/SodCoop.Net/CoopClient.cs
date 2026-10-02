using System;
using System.Net.Sockets;
using System.Threading;

namespace SodCoop.Net
{
    public sealed class CoopClient : CoopSession
    {
        readonly string _playerName;
        readonly string _cityId;
        Connection _conn;
        int _localId = -1;
        string _rejectReason;
        volatile bool _disposed;

        /// <summary>Connects in the background; watch for a Connected or Disconnected event.</summary>
        public CoopClient(string host, int port, string playerName, string cityId, int connectTimeoutMs = 10000)
        {
            _playerName = playerName;
            _cityId = cityId;
            new Thread(() => Connect(host, port, connectTimeoutMs)) { IsBackground = true, Name = "SodCoop connect" }.Start();
        }

        public override bool IsHost => false;

        public override byte? LocalPlayerId
        {
            get
            {
                int id = Volatile.Read(ref _localId);
                return id < 0 ? (byte?)null : (byte)id;
            }
        }

        public override void SendLocalState(PlayerState state)
        {
            var id = LocalPlayerId;
            if (id == null)
                return;
            var s = state.Clone();
            s.PlayerId = id.Value;
            _conn?.Send(s);
        }

        public override void SendChat(string text)
        {
            var id = LocalPlayerId;
            if (id != null)
                _conn?.Send(new Chat { PlayerId = id.Value, Text = text });
        }

        void Connect(string host, int port, int timeoutMs)
        {
            var tcp = new TcpClient();
            try
            {
                var connect = tcp.ConnectAsync(host, port);
                if (!connect.Wait(timeoutMs))
                    throw new TimeoutException($"No answer from {host}:{port} after {timeoutMs / 1000}s");
            }
            catch (Exception ex)
            {
                tcp.Close();
                var inner = ex is AggregateException agg ? agg.GetBaseException() : ex;
                Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Text = $"Could not connect to {host}:{port}: {inner.Message}" });
                return;
            }

            if (_disposed)
            {
                tcp.Close();
                return;
            }

            _conn = new Connection(tcp, OnMessage, OnClosed);
            _conn.Start();
            _conn.Send(new Hello { ProtocolVersion = Framing.ProtocolVersion, PlayerName = _playerName, CityId = _cityId });
        }

        void OnMessage(Connection conn, IMessage message)
        {
            switch (message)
            {
                case Welcome welcome:
                    Volatile.Write(ref _localId, welcome.YourId);
                    Enqueue(new NetEvent { Kind = NetEventKind.Connected, PlayerId = welcome.YourId, Text = welcome.HostCityId });
                    foreach (var p in welcome.Players)
                        Enqueue(new NetEvent { Kind = NetEventKind.PlayerJoined, PlayerId = p.Id, Text = p.Name });
                    if (!string.Equals(welcome.HostCityId, _cityId, StringComparison.Ordinal))
                    {
                        Enqueue(new NetEvent
                        {
                            Kind = NetEventKind.Warning,
                            Text = $"The host is in city '{welcome.HostCityId}' but you loaded '{_cityId}'. Load the host's city or positions will not match.",
                        });
                    }
                    break;

                case Reject reject:
                    _rejectReason = reject.Reason;
                    break;

                case PlayerJoined joined:
                    Enqueue(new NetEvent { Kind = NetEventKind.PlayerJoined, PlayerId = joined.PlayerId, Text = joined.Name });
                    break;

                case PlayerLeft left:
                    Enqueue(new NetEvent { Kind = NetEventKind.PlayerLeft, PlayerId = left.PlayerId });
                    break;

                case PlayerState state:
                    Enqueue(new NetEvent { Kind = NetEventKind.PlayerState, PlayerId = state.PlayerId, State = state });
                    break;

                case WorldTime time:
                    Enqueue(new NetEvent { Kind = NetEventKind.WorldTime, GameTime = time.GameTime });
                    break;

                case Chat chat:
                    Enqueue(new NetEvent { Kind = NetEventKind.Chat, PlayerId = chat.PlayerId, Text = chat.Text });
                    break;
            }
        }

        void OnClosed(Connection conn, Exception error)
        {
            if (_disposed)
                return;
            string reason = _rejectReason != null ? "Host refused: " + _rejectReason
                : error != null ? "Connection lost: " + error.Message
                : "Host closed the connection.";
            Enqueue(new NetEvent { Kind = NetEventKind.Disconnected, Text = reason });
        }

        public override void Dispose()
        {
            _disposed = true;
            _conn?.Close(null);
        }
    }
}
