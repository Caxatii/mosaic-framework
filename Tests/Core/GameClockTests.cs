using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class GameClockTests
    {
        [Test]
        public void Pause_holds_stop_time_until_all_are_released()
        {
            var clock = new GameClock();
            var window = clock.HoldPause();
            var popup = clock.HoldPause();

            Assert.That(clock.Advance(0.1f), Is.Zero);

            popup.Dispose();
            popup.Dispose();
            Assert.That(clock.IsPaused, Is.True, "a second Dispose does not release someone else's hold");

            window.Dispose();
            Assert.That(clock.Advance(0.1f), Is.EqualTo(0.1f).Within(1e-6));
            Assert.That(clock.Time, Is.EqualTo(0.1f).Within(1e-6));
            Assert.That(clock.RealDelta, Is.EqualTo(0.1f).Within(1e-6));
        }

        [Test]
        public void Slowdowns_multiply_and_end_when_disposed()
        {
            var clock = new GameClock { BaseScale = 2 };
            var slow = clock.SlowDown(0.5f);
            var slower = clock.SlowDown(0.5f);

            Assert.That(clock.Advance(1), Is.EqualTo(0.5f).Within(1e-6));

            slow.Dispose();
            slower.Dispose();
            Assert.That(clock.Advance(1), Is.EqualTo(2).Within(1e-6));
        }

        [Test]
        public void Timed_slowdown_lasts_for_real_time()
        {
            var clock = new GameClock();
            clock.SlowDown(0, realSeconds: 0.1f);

            Assert.That(clock.Advance(0.06f), Is.Zero);
            Assert.That(clock.Advance(0.06f), Is.Zero, "the frame in which it runs out is still frozen");
            Assert.That(clock.Advance(0.06f), Is.EqualTo(0.06f).Within(1e-6));
        }

        [Test]
        public void Delta_is_zero_on_pause_and_scaled_in_slow_motion()
        {
            var clock = new GameClock();
            using (clock.SlowDown(0.25f))
            {
                clock.Advance(0.04f);
                Assert.That(clock.Delta, Is.EqualTo(0.01f).Within(1e-6));
            }

            using (clock.HoldPause())
            {
                clock.Advance(0.04f);
                Assert.That(clock.Delta, Is.Zero);
            }
        }
    }
}
