using System;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class TraceTests
    {
        private readonly struct Hit : IEvent
        {
            public Hit(int amount) => Amount = amount;
            public int Amount { get; }
        }

        private readonly struct Died : IEvent { }

        private readonly struct Shoot : ICommand
        {
            public Shoot(int power) => Power = power;
            public int Power { get; }
        }

        private sealed class ShootHandler : ICommandHandler<Shoot>
        {
            private readonly IEventBus _events;
            public ShootHandler(IEventBus events) => _events = events;

            public CommandResult Handle(Shoot command)
            {
                _events.Publish(new Hit(command.Power));
                return CommandResult.Ok;
            }
        }

        private sealed class Quiet : ISystem
        {
            public void Tick(float deltaTime) { }
        }

        private sealed class DeathRule
        {
            public DeathRule(IEventBus events) =>
                events.Subscribe<Hit>(hit =>
                {
                    if (hit.Amount >= 10)
                        events.Publish(new Died());
                });
        }

        private GameplayContext _context;
        private SimulationTrace _trace;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext { Trace = new SimulationTrace() };
            _trace = _context.Trace;
            _context.Commands.Register(new ShootHandler(_context.Events));
            new DeathRule(_context.Events);
        }

        [Test]
        public void Command_its_events_and_their_consequences_form_one_tree()
        {
            _context.Commands.Send(new Shoot(10));

            Assert.That(_trace.Recent.Count, Is.EqualTo(1));
            Assert.That(_trace.Recent.Single().ToString().Replace("\r", ""), Is.EqualTo(
                "Shoot(Power=10) → Ok\n" +
                "  + Hit(Amount=10)\n" +
                "  Hit → DeathRule\n" +
                "    + Died\n"));
        }

        [Test]
        public void Subscribers_and_systems_that_did_nothing_are_not_recorded()
        {
            _context.Commands.Send(new Shoot(1));
            _context.Simulation.Tick(new Quiet(), 0.1f);

            var lines = _trace.Recent.Single().Lines.Select(line => line.Text).ToArray();
            Assert.That(lines, Is.EqualTo(new[] { "Shoot(Power=1) → Ok", "+ Hit(Amount=1)" }));
        }

        [Test]
        public void Quiet_frames_can_be_included()
        {
            _trace.IncludeQuiet = true;
            _context.Simulation.Tick(new Quiet(), 0.1f);

            Assert.That(_trace.Recent.Single().Lines.Single().Text, Is.EqualTo("Tick Quiet"));
        }

        [Test]
        public void Recent_keeps_only_the_last_records()
        {
            _context.Trace = _trace = new SimulationTrace(capacity: 2);

            for (var i = 0; i < 5; i++)
                _context.Commands.Send(new Shoot(i));

            Assert.That(_trace.Recent.Select(r => r.Lines[0].Text), Is.EqualTo(new[] { "Shoot(Power=3) → Ok", "Shoot(Power=4) → Ok" }));
        }

        [Test]
        public void Without_trace_nothing_is_recorded()
        {
            _context.Trace = null;
            Assert.DoesNotThrow(() => _context.Commands.Send(new Shoot(10)));
        }

        [Test]
        public void Failing_subscriber_is_visible_in_the_trace()
        {
            _context.Events.Subscribe<Died>(_ => throw new InvalidOperationException("boom"));

            _context.Commands.Send(new Shoot(10));

            Assert.That(_trace.Recent.Single().ToString(), Does.Contain("! InvalidOperationException: boom"));
        }
    }
}
