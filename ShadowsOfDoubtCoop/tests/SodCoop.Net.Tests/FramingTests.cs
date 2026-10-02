using System.IO;
using SodCoop.Net;
using Xunit;

namespace SodCoop.Net.Tests
{
    public class FramingTests
    {
        static T RoundTrip<T>(T message) where T : IMessage
        {
            var stream = new MemoryStream(Framing.Encode(message));
            var decoded = Framing.Read(stream);
            Assert.Null(Framing.Read(stream));
            return Assert.IsType<T>(decoded);
        }

        [Fact]
        public void PlayerStateRoundTrips()
        {
            var s = RoundTrip(new PlayerState { PlayerId = 3, X = 1.5f, Y = -2f, Z = 300.25f, Yaw = 90f, Pitch = 350f, Flags = PlayerFlags.Crouched });
            Assert.Equal(3, s.PlayerId);
            Assert.Equal(1.5f, s.X);
            Assert.Equal(-2f, s.Y);
            Assert.Equal(300.25f, s.Z);
            Assert.Equal(90f, s.Yaw);
            Assert.Equal(350f, s.Pitch);
            Assert.Equal(PlayerFlags.Crouched, s.Flags);
        }

        [Fact]
        public void WelcomeRoundTripsWithPlayers()
        {
            var m = new Welcome { YourId = 2, HostCityId = "Ravenwood|seed123" };
            m.Players.Add(new PlayerInfo(0, "Host"));
            m.Players.Add(new PlayerInfo(1, "Ünïcødé"));

            var w = RoundTrip(m);
            Assert.Equal(2, w.YourId);
            Assert.Equal("Ravenwood|seed123", w.HostCityId);
            Assert.Equal(2, w.Players.Count);
            Assert.Equal("Ünïcødé", w.Players[1].Name);
        }

        [Fact]
        public void OtherMessagesRoundTrip()
        {
            Assert.Equal("Bob", RoundTrip(new Hello { ProtocolVersion = 7, PlayerName = "Bob", CityId = "c" }).PlayerName);
            Assert.Equal("full", RoundTrip(new Reject { Reason = "full" }).Reason);
            Assert.Equal("Ann", RoundTrip(new PlayerJoined { PlayerId = 4, Name = "Ann" }).Name);
            Assert.Equal(4, RoundTrip(new PlayerLeft { PlayerId = 4 }).PlayerId);
            Assert.Equal(12.75, RoundTrip(new WorldTime { GameTime = 12.75 }).GameTime);
            Assert.Equal("hi", RoundTrip(new Chat { PlayerId = 1, Text = "hi" }).Text);
        }

        [Fact]
        public void SeveralFramesReadBackInOrder()
        {
            var stream = new MemoryStream();
            foreach (var m in new IMessage[] { new PlayerLeft { PlayerId = 1 }, new Chat { Text = "x" }, new PlayerLeft { PlayerId = 2 } })
            {
                var bytes = Framing.Encode(m);
                stream.Write(bytes, 0, bytes.Length);
            }
            stream.Position = 0;

            Assert.Equal(1, Assert.IsType<PlayerLeft>(Framing.Read(stream)).PlayerId);
            Assert.IsType<Chat>(Framing.Read(stream));
            Assert.Equal(2, Assert.IsType<PlayerLeft>(Framing.Read(stream)).PlayerId);
            Assert.Null(Framing.Read(stream));
        }

        [Fact]
        public void TruncatedFrameThrows()
        {
            var bytes = Framing.Encode(new Chat { Text = "hello" });
            var stream = new MemoryStream(bytes, 0, bytes.Length - 2);
            Assert.Throws<EndOfStreamException>(() => Framing.Read(stream));
        }

        [Fact]
        public void OversizedLengthIsRejected()
        {
            var stream = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1 });
            Assert.Throws<InvalidDataException>(() => Framing.Read(stream));
        }

        [Fact]
        public void UnknownTypeIsRejected()
        {
            Assert.Throws<InvalidDataException>(() => Framing.Decode(new byte[] { 200 }));
        }
    }
}
