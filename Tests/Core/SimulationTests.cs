using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class SimulationTests
    {
        private readonly struct Ping : IEvent { }
        private readonly struct Pong : IEvent { }
        private sealed class Marker : IModel { }

        private struct DoCommand : ICommand
        {
            public Action Action;
        }

        private sealed class DoHandler : ICommandHandler<DoCommand>
        {
            public CommandResult Handle(DoCommand command)
            {
                command.Action();
                return CommandResult.Ok;
            }
        }

        private enum TestError { Nope }

        private World _world;
        private EventBus _events;
        private GameplayContext _context;
        private List<string> _log;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext();
            _world = _context.World;
            _events = _context.EventBus;
            _context.Commands.Register(new DoHandler());
            _log = new List<string>();
        }

        [Test]
        public void Events_are_delivered_after_the_handler_finishes()
        {
            _events.Subscribe<Ping>(_ => _log.Add("subscriber"));

            _context.Commands.Send(new DoCommand
            {
                Action = () =>
                {
                    _events.Publish(new Ping());
                    _log.Add("handler end");
                },
            });

            Assert.That(_log, Is.EqualTo(new[] { "handler end", "subscriber" }));
        }

        [Test]
        public void Events_published_by_subscribers_go_to_the_end_of_the_queue()
        {
            _events.Subscribe<Ping>(_ => { _log.Add("ping 1"); _events.Publish(new Pong()); });
            _events.Subscribe<Ping>(_ => _log.Add("ping 2"));
            _events.Subscribe<Pong>(_ => _log.Add("pong"));

            _context.Simulation.Run(() => _events.Publish(new Ping()));

            Assert.That(_log, Is.EqualTo(new[] { "ping 1", "ping 2", "pong" }));
        }

        [Test]
        public void Failing_subscriber_does_not_stop_others()
        {
            _events.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
            _events.Subscribe<Ping>(_ => _log.Add("second"));

            _context.Simulation.Run(() => _events.Publish(new Ping()));

            Assert.That(_log, Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void Structural_changes_are_applied_before_events_are_delivered()
        {
            var entity = _world.CreateEntity();
            var seenComponent = false;
            _events.Subscribe<Ping>(_ => seenComponent = entity.Has<Marker>());

            _context.Simulation.Run(() =>
            {
                entity.Add(new Marker());
                Assert.That(entity.Has<Marker>(), Is.False, "inside the action the change is still buffered");
                _events.Publish(new Ping());
            });

            Assert.That(seenComponent, Is.True);
        }

        [Test]
        public void Destroyed_entities_are_announced()
        {
            var entity = _world.CreateEntity();
            var destroyed = new List<EntityId>();
            _events.Subscribe<EntityDestroyed>(e => destroyed.Add(e.Entity));

            _context.Simulation.Run(() => entity.Destroy());

            Assert.That(destroyed, Is.EqualTo(new[] { entity.Id }));
        }

        [Test]
        public void Endless_event_chain_is_cut_by_the_limit()
        {
            var events = new EventBus(NullLog.Instance, maxEventsPerDispatch: 100);
            var simulation = new Simulation(_world, events);
            var count = 0;
            events.Subscribe<Ping>(_ => { count++; events.Publish(new Ping()); });

            simulation.Run(() => events.Publish(new Ping()));

            Assert.That(count, Is.EqualTo(100));
            Assert.That(events.PendingCount, Is.Zero);
        }

        [Test]
        public void Second_handler_for_the_same_command_is_rejected()
        {
            Assert.Throws<InvalidOperationException>(() => _context.Commands.Register(new DoHandler()));
        }

        [Test]
        public void Rejected_result_carries_the_reason()
        {
            var result = CommandResult.Rejected(TestError.Nope);

            Assert.That(result.IsOk, Is.False);
            Assert.That(result.Reason, Is.EqualTo(TestError.Nope));
        }
    }
}
