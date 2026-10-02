using System;
using System.Collections.Concurrent;

namespace SodCoop.Net
{
    public enum NetEventKind
    {
        /// <summary>Client only: the host accepted us. <see cref="NetEvent.Text"/> is the host's city id.</summary>
        Connected,
        /// <summary>The session ended. <see cref="NetEvent.Text"/> says why.</summary>
        Disconnected,
        PlayerJoined,
        PlayerLeft,
        PlayerState,
        WorldTime,
        Chat,
        /// <summary>Something worth showing the player that does not end the session.</summary>
        Warning,
    }

    public sealed class NetEvent
    {
        public NetEventKind Kind;
        public byte PlayerId;
        public string Text;
        public PlayerState State;
        public double GameTime;

        public override string ToString() => $"{Kind} player={PlayerId} text={Text}";
    }

    /// <summary>
    /// Common surface of <see cref="CoopHost"/> and <see cref="CoopClient"/>. Network threads only
    /// enqueue events; the game thread drains them with <see cref="Poll"/>, so all game-side
    /// handling happens on the Unity main thread.
    /// </summary>
    public abstract class CoopSession : IDisposable
    {
        public const byte HostPlayerId = 0;

        readonly ConcurrentQueue<NetEvent> _events = new ConcurrentQueue<NetEvent>();

        public abstract bool IsHost { get; }

        /// <summary>Our player id, or null until the handshake finishes.</summary>
        public abstract byte? LocalPlayerId { get; }

        public abstract void SendLocalState(PlayerState state);

        public abstract void SendChat(string text);

        public void Poll(Action<NetEvent> handler)
        {
            while (_events.TryDequeue(out var e))
                handler(e);
        }

        protected void Enqueue(NetEvent e) => _events.Enqueue(e);

        public abstract void Dispose();
    }
}
