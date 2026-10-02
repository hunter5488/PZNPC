using System;
using System.IO;
using System.Text;

namespace SodCoop.Net
{
    /// <summary>
    /// Wire format: [int32 little-endian length][byte message type][payload], where length
    /// counts the type byte and payload.
    /// </summary>
    public static class Framing
    {
        public const int ProtocolVersion = 1;
        public const int MaxFrameLength = 64 * 1024;

        public static byte[] Encode(IMessage message)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(0);
                w.Write((byte)message.Type);
                message.Write(w);
                w.Flush();

                int length = (int)ms.Length - 4;
                if (length > MaxFrameLength)
                    throw new InvalidOperationException($"{message.Type} is {length} bytes, over the {MaxFrameLength} byte limit");

                byte[] bytes = ms.ToArray();
                BitConverter.GetBytes(length).CopyTo(bytes, 0);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(bytes, 0, 4);
                return bytes;
            }
        }

        /// <summary>Blocks until a whole message is read. Returns null when the stream ends cleanly.</summary>
        public static IMessage Read(Stream stream)
        {
            var header = new byte[4];
            if (!ReadExactly(stream, header, allowCleanEnd: true))
                return null;
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(header);

            int length = BitConverter.ToInt32(header, 0);
            if (length < 1 || length > MaxFrameLength)
                throw new InvalidDataException($"Bad frame length {length}");

            var body = new byte[length];
            ReadExactly(stream, body, allowCleanEnd: false);
            return Decode(body);
        }

        /// <summary>Decodes a frame body (type byte followed by payload).</summary>
        public static IMessage Decode(byte[] body)
        {
            using (var r = new BinaryReader(new MemoryStream(body), Encoding.UTF8))
            {
                var type = (MessageType)r.ReadByte();
                switch (type)
                {
                    case MessageType.Hello: return Hello.Read(r);
                    case MessageType.Welcome: return Welcome.Read(r);
                    case MessageType.Reject: return Reject.Read(r);
                    case MessageType.PlayerJoined: return PlayerJoined.Read(r);
                    case MessageType.PlayerLeft: return PlayerLeft.Read(r);
                    case MessageType.PlayerState: return PlayerState.Read(r);
                    case MessageType.WorldTime: return WorldTime.Read(r);
                    case MessageType.Chat: return Chat.Read(r);
                    default: throw new InvalidDataException($"Unknown message type {(byte)type}");
                }
            }
        }

        static bool ReadExactly(Stream stream, byte[] buffer, bool allowCleanEnd)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    if (read == 0 && allowCleanEnd)
                        return false;
                    throw new EndOfStreamException("Connection closed mid-message");
                }
                read += n;
            }
            return true;
        }
    }
}
