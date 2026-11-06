using System;

namespace Mosaic.Testing
{
    /// <summary>
    /// Advances a game in fixed steps for a given amount of game time: <c>TickLoop.Run(match.Tick, seconds: 3)</c>.
    /// </summary>
    public static class TickLoop
    {
        public const float DefaultStep = 1f / 60;

        /// <returns>The number of ticks made.</returns>
        public static int Run(Action<float> tick, float seconds, float step = DefaultStep)
        {
            if (tick == null)
                throw new ArgumentNullException(nameof(tick));
            if (step <= 0)
                throw new ArgumentOutOfRangeException(nameof(step), "The step must be positive");

            var ticks = 0;
            for (var left = seconds; left > 1e-6f; left -= step)
            {
                tick(Math.Min(step, left));
                ticks++;
            }

            return ticks;
        }

        public static void Times(Action<float> tick, int count, float step = DefaultStep)
        {
            if (tick == null)
                throw new ArgumentNullException(nameof(tick));

            for (var i = 0; i < count; i++)
                tick(step);
        }
    }
}
