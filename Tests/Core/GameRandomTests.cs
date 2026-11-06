using System;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class GameRandomTests
    {
        [Test]
        public void Sequence_is_SplitMix64()
        {
            var random = new GameRandom(0UL);

            Assert.That(random.NextULong(), Is.EqualTo(0xE220A8397B1DCDAFUL));
            Assert.That(random.NextULong(), Is.EqualTo(0x6E789E6AA1B965F4UL));
        }

        [Test]
        public void Restored_state_continues_the_same_sequence()
        {
            var random = new GameRandom(42);
            random.NextULong();
            var copy = new GameRandom(random.State);

            Assert.That(Enumerable.Range(0, 10).Select(_ => copy.Next(100)),
                Is.EqualTo(Enumerable.Range(0, 10).Select(_ => random.Next(100))));
        }

        [Test]
        public void Ranges_stay_inside_their_bounds()
        {
            var random = new GameRandom(7);
            for (var i = 0; i < 10000; i++)
            {
                Assert.That(random.Range(-3, 4), Is.InRange(-3, 3));
                Assert.That(random.Next(5), Is.InRange(0, 4));
                Assert.That(random.NextFloat(), Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(random.NextDouble(), Is.GreaterThanOrEqualTo(0d).And.LessThan(1d));
                Assert.That(random.Range(2f, 3f), Is.GreaterThanOrEqualTo(2f).And.LessThan(3f));
            }

            Assert.That(random.Range(int.MinValue, int.MaxValue), Is.LessThan(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(3, 3));
        }

        [Test]
        public void Every_value_of_a_small_range_comes_up()
        {
            var random = new GameRandom(1);
            var counts = new int[6];
            for (var i = 0; i < 6000; i++)
                counts[random.Next(6)]++;

            Assert.That(counts, Has.All.InRange(850, 1150));
        }

        [Test]
        public void Streams_do_not_affect_each_other()
        {
            var streams = new RandomStreams(5);
            var expectedAi = new RandomStreams(5)["ai"].NextULong();

            streams["loot"].NextULong();
            streams["loot"].NextULong();

            Assert.That(streams["ai"].NextULong(), Is.EqualTo(expectedAi));
            Assert.That(GameRandom.ForStream(5, "ai").State, Is.Not.EqualTo(GameRandom.ForStream(5, "loot").State));
            Assert.That(GameRandom.ForStream(5, "ai").State, Is.Not.EqualTo(GameRandom.ForStream(6, "ai").State));
        }

        [Test]
        public void Captured_streams_are_restored_in_a_new_match()
        {
            var streams = new RandomStreams(9);
            streams["ai"].NextULong();
            streams["loot"].Next(10);
            var saved = streams.Capture();

            var restored = new RandomStreams(9);
            restored.Restore(saved);

            Assert.That(saved.Select(state => state.Name), Is.EqualTo(new[] { "ai", "loot" }));
            Assert.That(restored["ai"].NextULong(), Is.EqualTo(streams["ai"].NextULong()));
            Assert.That(restored["loot"].NextULong(), Is.EqualTo(streams["loot"].NextULong()));
        }
    }
}
