using System;
using System.Collections.Generic;

namespace SodCoop.Net
{
    /// <summary>
    /// Smooths a remote player's movement. States are stamped with the local time they arrived and
    /// rendered a fixed delay in the past, interpolating between the two states around that moment.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        struct Entry
        {
            public double Time;
            public PlayerState State;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly int _capacity;

        public SnapshotBuffer(int capacity = 32)
        {
            _capacity = Math.Max(2, capacity);
        }

        public int Count => _entries.Count;

        public void Add(double receivedAt, PlayerState state)
        {
            // Arrival times are monotonic on one connection, so appending keeps the list sorted.
            if (_entries.Count > 0 && receivedAt < _entries[_entries.Count - 1].Time)
                receivedAt = _entries[_entries.Count - 1].Time;
            _entries.Add(new Entry { Time = receivedAt, State = state });
            if (_entries.Count > _capacity)
                _entries.RemoveAt(0);
        }

        /// <summary>The interpolated state at <paramref name="renderTime"/>, or null if nothing has arrived.</summary>
        public PlayerState Sample(double renderTime)
        {
            if (_entries.Count == 0)
                return null;
            if (renderTime <= _entries[0].Time)
                return _entries[0].State;

            var last = _entries[_entries.Count - 1];
            if (renderTime >= last.Time)
                return last.State;

            for (int i = _entries.Count - 1; i > 0; i--)
            {
                var a = _entries[i - 1];
                var b = _entries[i];
                if (renderTime >= a.Time)
                {
                    double span = b.Time - a.Time;
                    float t = span <= 0 ? 1f : (float)((renderTime - a.Time) / span);
                    return Lerp(a.State, b.State, t);
                }
            }
            return last.State;
        }

        public static PlayerState Lerp(PlayerState a, PlayerState b, float t)
        {
            return new PlayerState
            {
                PlayerId = b.PlayerId,
                X = a.X + (b.X - a.X) * t,
                Y = a.Y + (b.Y - a.Y) * t,
                Z = a.Z + (b.Z - a.Z) * t,
                Yaw = LerpAngle(a.Yaw, b.Yaw, t),
                Pitch = LerpAngle(a.Pitch, b.Pitch, t),
                Flags = t < 0.5f ? a.Flags : b.Flags,
            };
        }

        /// <summary>Interpolates degrees the short way round, so 350 to 10 passes through 0, not 180.</summary>
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            float result = (a + delta * t) % 360f;
            return result < 0 ? result + 360f : result;
        }
    }
}
