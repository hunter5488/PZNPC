using System;
using System.Collections.Generic;
using System.IO;

namespace SodCoop.Net
{
    public enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        PlayerJoined = 4,
        PlayerLeft = 5,
        PlayerState = 6,
        WorldTime = 7,
        Chat = 8,
    }

    public interface IMessage
    {
        MessageType Type { get; }
        void Write(BinaryWriter w);
    }

    /// <summary>Client -> host: first message on a new connection.</summary>
    public sealed class Hello : IMessage
    {
        public int ProtocolVersion;
        public string PlayerName = "";
        public string CityId = "";

        public MessageType Type => MessageType.Hello;

        public void Write(BinaryWriter w)
        {
            w.Write(ProtocolVersion);
            w.Write(PlayerName ?? "");
            w.Write(CityId ?? "");
        }

        public static Hello Read(BinaryReader r) => new Hello
        {
            ProtocolVersion = r.ReadInt32(),
            PlayerName = r.ReadString(),
            CityId = r.ReadString(),
        };
    }

    public struct PlayerInfo
    {
        public byte Id;
        public string Name;

        public PlayerInfo(byte id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>Host -> client: handshake accepted.</summary>
    public sealed class Welcome : IMessage
    {
        public byte YourId;
        public string HostCityId = "";
        public List<PlayerInfo> Players = new List<PlayerInfo>();

        public MessageType Type => MessageType.Welcome;

        public void Write(BinaryWriter w)
        {
            w.Write(YourId);
            w.Write(HostCityId ?? "");
            w.Write((byte)Players.Count);
            foreach (var p in Players)
            {
                w.Write(p.Id);
                w.Write(p.Name ?? "");
            }
        }

        public static Welcome Read(BinaryReader r)
        {
            var m = new Welcome { YourId = r.ReadByte(), HostCityId = r.ReadString() };
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
                m.Players.Add(new PlayerInfo(r.ReadByte(), r.ReadString()));
            return m;
        }
    }

    /// <summary>Host -> client: handshake refused. The host closes the connection after sending.</summary>
    public sealed class Reject : IMessage
    {
        public string Reason = "";

        public MessageType Type => MessageType.Reject;
        public void Write(BinaryWriter w) => w.Write(Reason ?? "");
        public static Reject Read(BinaryReader r) => new Reject { Reason = r.ReadString() };
    }

    public sealed class PlayerJoined : IMessage
    {
        public byte PlayerId;
        public string Name = "";

        public MessageType Type => MessageType.PlayerJoined;

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(Name ?? "");
        }

        public static PlayerJoined Read(BinaryReader r) => new PlayerJoined { PlayerId = r.ReadByte(), Name = r.ReadString() };
    }

    public sealed class PlayerLeft : IMessage
    {
        public byte PlayerId;

        public MessageType Type => MessageType.PlayerLeft;
        public void Write(BinaryWriter w) => w.Write(PlayerId);
        public static PlayerLeft Read(BinaryReader r) => new PlayerLeft { PlayerId = r.ReadByte() };
    }

    [Flags]
    public enum PlayerFlags : byte
    {
        None = 0,
        Crouched = 1 << 0,
    }

    /// <summary>
    /// A player's transform. Sent many times a second and safe to drop: the next one supersedes it.
    /// </summary>
    public sealed class PlayerState : IMessage
    {
        public byte PlayerId;
        public float X, Y, Z;
        public float Yaw, Pitch;
        public PlayerFlags Flags;

        public MessageType Type => MessageType.PlayerState;

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(X);
            w.Write(Y);
            w.Write(Z);
            w.Write(Yaw);
            w.Write(Pitch);
            w.Write((byte)Flags);
        }

        public static PlayerState Read(BinaryReader r) => new PlayerState
        {
            PlayerId = r.ReadByte(),
            X = r.ReadSingle(),
            Y = r.ReadSingle(),
            Z = r.ReadSingle(),
            Yaw = r.ReadSingle(),
            Pitch = r.ReadSingle(),
            Flags = (PlayerFlags)r.ReadByte(),
        };

        public PlayerState Clone() => (PlayerState)MemberwiseClone();
    }

    /// <summary>Host -> clients: the host's in-game clock, which is authoritative.</summary>
    public sealed class WorldTime : IMessage
    {
        public double GameTime;

        public MessageType Type => MessageType.WorldTime;
        public void Write(BinaryWriter w) => w.Write(GameTime);
        public static WorldTime Read(BinaryReader r) => new WorldTime { GameTime = r.ReadDouble() };
    }

    public sealed class Chat : IMessage
    {
        public byte PlayerId;
        public string Text = "";

        public MessageType Type => MessageType.Chat;

        public void Write(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(Text ?? "");
        }

        public static Chat Read(BinaryReader r) => new Chat { PlayerId = r.ReadByte(), Text = r.ReadString() };
    }
}
