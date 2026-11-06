using System;

namespace Mosaic.Core
{
    /// <summary>
    /// A timer value for models: a cooldown, a reload, a delay, a repeating interval. Immutable: advancing returns a
    /// new value, so a model keeps it in a private field and its internal methods replace it.
    /// <c>_cooldown = _cooldown.Advance(deltaTime);</c>
    /// </summary>
    public readonly struct Countdown : IEquatable<Countdown>
    {
        private const float Tolerance = 1e-5f;

        /// <param name="duration">The full length; the interval of a repeating timer.</param>
        /// <param name="left">Time left; the first interval can differ from the others (a first delay).</param>
        public Countdown(float duration, float left)
        {
            Duration = Math.Max(0, duration);
            Left = Math.Max(0, left);
        }

        /// <summary>A timer that is not running: done, nothing left.</summary>
        public static Countdown Done => default;

        public static Countdown Start(float duration) => new Countdown(duration, duration);

        public float Duration { get; }
        public float Left { get; }

        public bool IsDone => Left <= 0;
        public bool IsRunning => Left > 0;

        /// <summary>How much has passed, 0..1; 1 when done.</summary>
        public float Progress => Duration <= 0 || Left <= 0 ? 1 : 1 - Math.Min(Left, Duration) / Duration;

        public Countdown Advance(float deltaTime) => new Countdown(Duration, Left - deltaTime);

        /// <param name="finished">True exactly on the step that reached zero, not on later ones.</param>
        public Countdown Advance(float deltaTime, out bool finished)
        {
            var next = Advance(deltaTime);
            finished = IsRunning && next.IsDone;
            return next;
        }

        /// <summary>
        /// A repeating timer: counts how many intervals (<see cref="Duration"/>) ended in this step and starts the
        /// next one, keeping the overshoot. A step of 0.25 s over a 0.1 s interval gives 2 or 3, never loses time.
        /// </summary>
        public Countdown Repeat(float deltaTime, out int times)
        {
            times = 0;
            if (Duration <= 0)
                return this;

            var left = Left - deltaTime;
            while (left <= Tolerance)
            {
                times++;
                left += Duration;
            }

            return new Countdown(Duration, left);
        }

        public bool Equals(Countdown other) => Duration.Equals(other.Duration) && Left.Equals(other.Left);
        public override bool Equals(object obj) => obj is Countdown other && Equals(other);
        public override int GetHashCode() => Duration.GetHashCode() * 397 ^ Left.GetHashCode();
        public override string ToString() => $"{Left:0.###}/{Duration:0.###}";
    }
}
