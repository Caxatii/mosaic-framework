using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class CountdownTests
    {
        [Test]
        public void Counts_down_to_zero_and_reports_the_finishing_step_once()
        {
            var timer = Countdown.Start(1);

            timer = timer.Advance(0.6f, out var finished);
            Assert.That(finished, Is.False);
            Assert.That(timer.Progress, Is.EqualTo(0.6f).Within(1e-5));

            timer = timer.Advance(0.6f, out finished);
            Assert.That(finished, Is.True);
            Assert.That(timer.Left, Is.Zero, "never negative");
            Assert.That(timer.Progress, Is.EqualTo(1));

            timer.Advance(0.6f, out finished);
            Assert.That(finished, Is.False, "a done timer does not finish again");
        }

        [Test]
        public void Done_timer_is_not_running()
        {
            Assert.That(Countdown.Done.IsDone, Is.True);
            Assert.That(Countdown.Done.IsRunning, Is.False);
            Assert.That(Countdown.Start(0).IsDone, Is.True);
        }

        [Test]
        public void Repeat_counts_intervals_and_keeps_the_overshoot()
        {
            var timer = new Countdown(0.1f, left: 0.05f);

            timer = timer.Repeat(0.25f, out var times);
            Assert.That(times, Is.EqualTo(3));
            Assert.That(timer.Left, Is.EqualTo(0.1f).Within(1e-5));

            var total = 0;
            for (var i = 0; i < 100; i++)
            {
                timer = timer.Repeat(1 / 60f, out times);
                total += times;
            }

            Assert.That(total, Is.EqualTo(16), "1.667 s of 0.1 s intervals: no time is lost to rounding");
        }

        [Test]
        public void Repeat_without_an_interval_never_fires()
        {
            Countdown.Start(0).Repeat(10, out var times);
            Assert.That(times, Is.Zero);
        }
    }
}
