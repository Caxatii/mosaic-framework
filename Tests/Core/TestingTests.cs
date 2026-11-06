using System.Linq;
using Mosaic.ArchitectureSamples.Tests;
using Mosaic.Testing;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class TestingTests
    {
        private static ArchitectureRules Rules() =>
            new ArchitectureRules(typeof(Counter).Assembly)
            {
                Exclude = type => type.Namespace != typeof(Counter).Namespace,
            };

        [Test]
        public void Scanner_sees_calls_inside_lambdas()
        {
            var usages = IlScanner.UsagesOf(typeof(CounterOperations)).ToList();

            Assert.That(usages.Any(usage => usage.Member.Name == "Publish" && usage.From.DeclaringType != typeof(CounterOperations)));
            Assert.That(Rules().ScannerSeesOperations(), Is.Empty);
        }

        [Test]
        public void Rules_report_each_broken_role()
        {
            var rules = Rules();

            Assert.That(rules.RulesDoNotPublishEvents(), Has.Exactly(1).Contains(nameof(LoudHandler)));
            Assert.That(rules.ReadersDoNotChangeState(), Has.Exactly(1).Contains(nameof(CounterEvaluator)));
            Assert.That(rules.ModelsAreChangedOnlyByOperations(), Has.Some.Contains(nameof(LoudHandler)));
            Assert.That(rules.ModelsAreChangedOnlyByOperations(), Has.None.Contains(nameof(CounterOperations)));
            Assert.That(rules.ModelsStayIsolated(), Has.Exactly(1).Contains(nameof(LeakyModel)));
            Assert.That(rules.CommandsAndEventsAreReadonlyStructs(), Has.Exactly(1).Contains(nameof(MutableEvent)));
            Assert.That(rules.CommandsCanBeRecorded(), Has.Exactly(1).Contains(nameof(ObjectCommand)));
            Assert.That(rules.DomainDoesNotReferenceUnity(), Is.Empty);
            Assert.That(rules.CheckAll(), Has.Count.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void Event_recorder_collects_until_disposed()
        {
            var context = new GameplayContext(throwOnSubscriberException: true);
            var operations = new CounterOperations(context.Events);
            var entity = default(Entity);
            context.Simulation.Run(() => entity = context.World.Spawn(1));
            context.Simulation.Run(() => context.World.Add(entity.Id, new Counter()));

            using (var recorder = new EventRecorder<Counted>(context.Events))
            {
                context.Simulation.Run(() => operations.Increment(entity));
                context.Simulation.Run(() => operations.Increment(entity));

                Assert.That(recorder.Count, Is.EqualTo(2));
                Assert.That(recorder.Last.Value, Is.EqualTo(2));
            }

            context.Simulation.Run(() => operations.Increment(entity));
            Assert.That(entity.Get<Counter>().Value, Is.EqualTo(3));
        }

        [Test]
        public void Tick_loop_covers_the_time_exactly()
        {
            var total = 0f;
            var ticks = TickLoop.Run(dt => total += dt, seconds: 1, step: 0.3f);

            Assert.That(ticks, Is.EqualTo(4));
            Assert.That(total, Is.EqualTo(1).Within(1e-5));
        }

        [Test]
        public void Test_log_keeps_messages()
        {
            var log = new TestLog();
            log.Warning("careful");
            log.Error("broken");

            Assert.That(log.Warnings, Is.EqualTo(new[] { "careful" }));
            Assert.That(log.Errors, Is.EqualTo(new[] { "broken" }));
        }
    }
}

namespace Mosaic.ArchitectureSamples.Tests
{
    using Mosaic.Core;

    internal sealed class Counter : IModel
    {
        public int Value { get; private set; }

        internal int Increment() => ++Value;
    }

    internal sealed class LeakyModel : IModel
    {
        public World World;
    }

    internal readonly struct Counted : IEvent
    {
        public Counted(int value) => Value = value;

        public int Value { get; }
    }

    internal struct MutableEvent : IEvent
    {
        public int Value;
    }

    internal readonly struct ObjectCommand : ICommand
    {
        public ObjectCommand(object payload) => Payload = payload;

        public object Payload { get; }
    }

    internal sealed class CounterOperations
    {
        private readonly IEventBus _events;

        public CounterOperations(IEventBus events) => _events = events;

        public void Increment(Entity entity)
        {
            var counter = entity.Get<Counter>();
            System.Action publish = () => _events.Publish(new Counted(counter.Increment()));
            publish();
        }
    }

    internal sealed class CounterEvaluator
    {
        private readonly World _world;

        public CounterEvaluator(World world) => _world = world;

        public void Reset(EntityId id) => _world.Remove<Counter>(id);
    }

    internal sealed class LoudHandler : CommandHandler
    {
        private readonly IEventBus _events;

        public LoudHandler(GameplayContext context, IEventBus events) : base(context) => _events = events;

        public void Shout(Entity entity) => _events.Publish(new Counted(entity.Get<Counter>().Increment()));
    }
}
