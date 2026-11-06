using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Game time of a match: pause held by any number of owners, slow motion and hit stops, the time that passed.
    /// The flow state that runs the match owns it and ticks the match with <c>Match.Tick(clock.Advance(deltaTime))</c>;
    /// presentation animates the world with <see cref="Delta"/> and the UI with real time. The recording stores the
    /// game steps, so slow motion replays the same.
    /// </summary>
    public sealed class GameClock
    {
        private readonly List<Hold> _slowdowns = new List<Hold>();
        private int _pauseHolds;
        private float _baseScale = 1;

        /// <summary>The speed of the game without slowdowns: 2 for a fast-forward debug button.</summary>
        public float BaseScale
        {
            get => _baseScale;
            set => _baseScale = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Must not be negative");
        }

        public bool IsPaused => _pauseHolds > 0;

        /// <summary>BaseScale times every active slowdown.</summary>
        public float Scale
        {
            get
            {
                var scale = _baseScale;
                for (var i = 0; i < _slowdowns.Count; i++)
                    scale *= _slowdowns[i].Scale;
                return scale;
            }
        }

        /// <summary>Game time passed, without pauses.</summary>
        public float Time { get; private set; }

        /// <summary>The game step of the last Advance: 0 on pause.</summary>
        public float Delta { get; private set; }

        /// <summary>The real step of the last Advance.</summary>
        public float RealDelta { get; private set; }

        /// <summary>The game stands while at least one hold is not disposed.</summary>
        public IDisposable HoldPause()
        {
            _pauseHolds++;
            return new PauseHold(this);
        }

        /// <summary>
        /// Multiplies the speed by <paramref name="scale"/> (0 freezes the game) until disposed, or for
        /// <paramref name="realSeconds"/> of real time if it is given: a hit stop is <c>SlowDown(0, 0.08f)</c>.
        /// </summary>
        public IDisposable SlowDown(float scale, float realSeconds = float.PositiveInfinity)
        {
            if (scale < 0)
                throw new ArgumentOutOfRangeException(nameof(scale), "Must not be negative");

            var hold = new Hold(this, scale, realSeconds);
            _slowdowns.Add(hold);
            return hold;
        }

        /// <returns>The game step for this frame, to tick the match with.</returns>
        public float Advance(float realDelta)
        {
            RealDelta = realDelta > 0 ? realDelta : 0;
            Delta = IsPaused ? 0 : RealDelta * Scale;
            Time += Delta;

            for (var i = _slowdowns.Count - 1; i >= 0; i--)
                if ((_slowdowns[i].Left -= RealDelta) <= 0)
                    _slowdowns.RemoveAt(i);

            return Delta;
        }

        private sealed class PauseHold : IDisposable
        {
            private GameClock _clock;

            public PauseHold(GameClock clock) => _clock = clock;

            public void Dispose()
            {
                if (_clock == null)
                    return;

                _clock._pauseHolds--;
                _clock = null;
            }
        }

        private sealed class Hold : IDisposable
        {
            private readonly GameClock _clock;

            public Hold(GameClock clock, float scale, float left)
            {
                _clock = clock;
                Scale = scale;
                Left = left;
            }

            public float Scale { get; }
            public float Left { get; set; }

            public void Dispose() => _clock._slowdowns.Remove(this);
        }
    }
}
