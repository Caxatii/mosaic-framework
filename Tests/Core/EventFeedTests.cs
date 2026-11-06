using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class EventFeedTests
    {
        private readonly struct Hit : IEvent
        {
            public Hit(int amount) => Amount = amount;
            public int Amount { get; }
        }

        private readonly struct Died : IEvent { }

        private readonly struct Shoot : ICommand { }

        private readonly struct Cheer : ICommand { }

        private sealed class Handlers : CommandHandler
        {
            public int Cheers;

            public Handlers(GameplayContext context, IEventBus events) : base(context)
            {
                On<Shoot>(_ =>
                {
                    events.Publish(new Hit(5));
                    events.Publish(new Died());
                    return CommandResult.Ok;
                });
                On<Cheer>(_ =>
                {
                    Cheers++;
                    return CommandResult.Ok;
                });
            }
        }

        private GameplayContext _context;
        private Handlers _handlers;
        private EventFeed _feed;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext(throwOnSubscriberException: true);
            _handlers = new Handlers(_context, _context.Events);
            _feed = new EventFeed(_context);
        }

        [Test]
        public void Events_wait_for_Deliver_and_keep_their_order()
        {
            var received = new List<string>();
            _feed.Subscribe<Died>(_ => received.Add("died"));
            _feed.Subscribe<Hit>(e => received.Add("hit " + e.Amount));

            _context.Commands.Send(new Shoot());
            Assert.That(received, Is.Empty);
            Assert.That(_feed.PendingCount, Is.EqualTo(2));

            _feed.Deliver();
            Assert.That(received, Is.EqualTo(new[] { "hit 5", "died" }));
        }

        [Test]
        public void A_command_sent_by_a_feed_subscriber_is_external_and_recorded()
        {
            var recording = new CommandRecording();
            _context.Commands.Recording = recording;
            _feed.Subscribe<Died>(_ => _context.Commands.Send(new Cheer()));

            _context.Commands.Send(new Shoot());
            _feed.Deliver();

            Assert.That(_handlers.Cheers, Is.EqualTo(1));
            Assert.That(recording.Entries.Select(entry => entry.Command?.GetType()), Is.EqualTo(new[] { typeof(Shoot), typeof(Cheer) }));
        }

        [Test]
        public void A_command_sent_by_a_bus_subscriber_is_not_recorded()
        {
            var recording = new CommandRecording();
            _context.Commands.Recording = recording;
            _context.Events.Subscribe<Died>(_ => _context.Commands.Send(new Cheer()));

            _context.Commands.Send(new Shoot());

            Assert.That(recording.Entries.Count, Is.EqualTo(1), "why presentation must not subscribe to the bus itself");
        }

        [Test]
        public void Deliver_inside_an_action_throws()
        {
            Assert.Throws<InvalidOperationException>(() => _context.Simulation.Run(() => _feed.Deliver()));
        }

        [Test]
        public void Disposed_feed_stops_collecting()
        {
            var count = 0;
            _feed.Subscribe<Hit>(_ => count++);
            _context.Commands.Send(new Shoot());

            _feed.Dispose();
            _context.Commands.Send(new Shoot());
            _feed.Deliver();

            Assert.That(count, Is.Zero);
            Assert.That(_feed.PendingCount, Is.Zero);
        }
    }
}
