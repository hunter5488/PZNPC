using SodCoop.Net;
using Xunit;

namespace SodCoop.Net.Tests
{
    public class SnapshotBufferTests
    {
        static PlayerState At(float x, float yaw = 0) => new PlayerState { X = x, Yaw = yaw };

        [Fact]
        public void EmptyBufferReturnsNull()
        {
            Assert.Null(new SnapshotBuffer().Sample(1.0));
        }

        [Fact]
        public void InterpolatesBetweenSnapshots()
        {
            var buf = new SnapshotBuffer();
            buf.Add(1.0, At(0));
            buf.Add(2.0, At(10));
            buf.Add(3.0, At(30));

            Assert.Equal(5f, buf.Sample(1.5).X, 3);
            Assert.Equal(20f, buf.Sample(2.5).X, 3);
        }

        [Fact]
        public void ClampsOutsideTheBufferedRange()
        {
            var buf = new SnapshotBuffer();
            buf.Add(1.0, At(0));
            buf.Add(2.0, At(10));

            Assert.Equal(0f, buf.Sample(0.5).X);
            Assert.Equal(10f, buf.Sample(5.0).X);
        }

        [Fact]
        public void DropsOldestBeyondCapacity()
        {
            var buf = new SnapshotBuffer(capacity: 3);
            for (int i = 0; i < 10; i++)
                buf.Add(i, At(i));

            Assert.Equal(3, buf.Count);
            Assert.Equal(7f, buf.Sample(0).X);
        }

        [Theory]
        [InlineData(350f, 10f, 0.5f, 0f)]
        [InlineData(10f, 350f, 0.5f, 0f)]
        [InlineData(0f, 90f, 0.5f, 45f)]
        [InlineData(270f, 90f, 0.25f, 225f)]
        public void AnglesTakeTheShortWay(float a, float b, float t, float expected)
        {
            Assert.Equal(expected, SnapshotBuffer.LerpAngle(a, b, t), 3);
        }
    }
}
