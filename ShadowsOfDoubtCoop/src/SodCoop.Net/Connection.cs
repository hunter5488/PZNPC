using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace SodCoop.Net
{
    /// <summary>
    /// One TCP connection with its own reader and writer threads, so the game thread never blocks
    /// on the network. If the send queue fills up (a stalled peer), droppable messages such as
    /// <see cref="PlayerState"/> are discarded instead of queued.
    /// </summary>
    internal sealed class Connection : IDisposable
    {
        const int SendQueueCapacity = 256;

        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly BlockingCollection<byte[]> _sendQueue = new BlockingCollection<byte[]>(SendQueueCapacity);
        readonly Action<Connection, IMessage> _onMessage;
        readonly Action<Connection, Exception> _onClosed;
        int _closed;

        public byte PlayerId = byte.MaxValue;
        public string PlayerName = "";
        public bool Handshaken;

        public Connection(TcpClient tcp, Action<Connection, IMessage> onMessage, Action<Connection, Exception> onClosed)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
            _onMessage = onMessage;
            _onClosed = onClosed;
        }

        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        public void Start()
        {
            new Thread(ReadLoop) { IsBackground = true, Name = "SodCoop read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "SodCoop write" }.Start();
        }

        public void Send(IMessage message)
        {
            if (IsClosed)
                return;

            byte[] frame = Framing.Encode(message);
            try
            {
                if (message is PlayerState)
                    _sendQueue.TryAdd(frame);
                else
                    _sendQueue.Add(frame);
            }
            catch (InvalidOperationException)
            {
                // Queue completed by Close() on another thread.
            }
        }

        /// <summary>Sends everything already queued, then closes.</summary>
        public void SendAndClose(IMessage message)
        {
            Send(message);
            try { _sendQueue.CompleteAdding(); } catch (ObjectDisposedException) { }
        }

        void ReadLoop()
        {
            try
            {
                while (!IsClosed)
                {
                    var message = Framing.Read(_stream);
                    if (message == null)
                        break;
                    _onMessage(this, message);
                }
                Close(null);
            }
            catch (Exception ex)
            {
                Close(ex);
            }
        }

        void WriteLoop()
        {
            try
            {
                foreach (var frame in _sendQueue.GetConsumingEnumerable())
                    _stream.Write(frame, 0, frame.Length);
                _stream.Flush();
                Close(null);
            }
            catch (Exception ex)
            {
                Close(ex);
            }
        }

        public void Close(Exception error)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return;

            try { _sendQueue.CompleteAdding(); } catch (ObjectDisposedException) { }
            try { _tcp.Close(); } catch { }
            _onClosed(this, IsBenign(error) ? null : error);
        }

        // Socket errors caused by our own Close() or a peer hanging up are normal disconnects.
        static bool IsBenign(Exception error) =>
            error is ObjectDisposedException ||
            error is System.IO.IOException io && io.InnerException is SocketException;

        public void Dispose() => Close(null);
    }
}
