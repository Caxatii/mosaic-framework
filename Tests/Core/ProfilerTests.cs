using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class ProfilerTests
    {
        private readonly struct Hit : IEvent { }

        private readonly struct Shoot : ICommand { }

        private sealed class Gun : ISystem
        {
            private readonly ICommandBus _commands;
            public Gun(ICommandBus commands) => _commands = commands;
            public void Tick(float deltaTime) => _commands.Send(new Shoot());
        }

        private sealed class ShootHandler : CommandHandler
        {
            public ShootHandler(GameplayContext context, IEventBus events) : base(context) =>
                On<Shoot>(_ =>
                {
                    events.Publish(new Hit());
                    return CommandResult.Ok;
                });
        }

        private sealed class Recorder : ISimulationProfiler
        {
            private readonly Stack<string> _open = new Stack<string>();
            public readonly List<string> Lines = new List<string>();

            public void Begin(string name)
            {
                Lines.Add(new string(' ', _open.Count * 2) + name);
                _open.Push(name);
            }

            public void End() => _open.Pop();
        }

        private sealed class Listener
        {
            public int Hits;
            public void OnHit(Hit hit) => Hits++;
        }

        [Test]
        public void Systems_commands_and_subscribers_are_measured_with_their_nesting()
        {
            var context = new GameplayContext(throwOnSubscriberException: true);
            new ShootHandler(context, context.Events);
            var listener = new Listener();
            context.Events.Subscribe<Hit>(listener.OnHit);
            var recorder = new Recorder();
            context.Profiler = recorder;

            context.Simulation.Tick(new Gun(context.Commands), 0.1f);

            Assert.That(recorder.Lines, Is.EqualTo(new[]
            {
                "Tick Gun",
                "  Shoot",
                "Hit → Listener",
            }));
        }

        [Test]
        public void Stats_count_calls_and_names_are_reused()
        {
            var context = new GameplayContext(throwOnSubscriberException: true);
            new ShootHandler(context, context.Events);
            var stats = new SimulationStats();
            context.Profiler = stats;
            var gun = new Gun(context.Commands);

            for (var i = 0; i < 3; i++)
                context.Simulation.Tick(gun, 0.1f);

            var entries = stats.Snapshot();
            Assert.That(entries.Select(e => e.Name), Is.EquivalentTo(new[] { "Tick Gun", "Shoot" }));
            Assert.That(entries.Single(e => e.Name == "Shoot").Calls, Is.EqualTo(3));
            Assert.That(entries.First().Name, Is.EqualTo("Tick Gun"), "inclusive time: the system contains its command");

            stats.Reset();
            Assert.That(stats.Snapshot(), Is.Empty);
        }

        [Test]
        public void Combined_profilers_both_receive_and_can_be_separated_again()
        {
            var first = new Recorder();
            var second = new Recorder();
            var both = SimulationProfiler.Combine(first, second);

            both.Begin("x");
            both.End();

            Assert.That(first.Lines, Is.EqualTo(new[] { "x" }));
            Assert.That(second.Lines, Is.EqualTo(new[] { "x" }));
            Assert.That(SimulationProfiler.Without(both, second), Is.SameAs(first));
            Assert.That(SimulationProfiler.Without(first, first), Is.Null);
        }

        [Test]
        public void Measuring_does_not_allocate_after_warmup()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Allocation checks run in dotnet test only");
#else
            var context = new GameplayContext(NullLog.Instance);
            new ShootHandler(context, context.Events);
            var listener = new Listener();
            context.Events.Subscribe<Hit>(listener.OnHit);
            context.Profiler = new SimulationStats();
            var gun = new Gun(context.Commands);

            context.Simulation.Tick(gun, 0.1f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
                context.Simulation.Tick(gun, 0.1f);

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
#endif
        }
    }
}
