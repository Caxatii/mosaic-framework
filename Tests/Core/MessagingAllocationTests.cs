using System;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class MessagingAllocationTests
    {
        private readonly struct Hit : IEvent
        {
            public Hit(EntityId target, float amount)
            {
                Target = target;
                Amount = amount;
            }

            public EntityId Target { get; }
            public float Amount { get; }
        }

        [Test]
        public void Publish_and_dispatch_do_not_allocate_after_warmup()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Allocation checks run in dotnet test only");
#else
            var bus = new EventBus(NullLog.Instance);
            var total = 0f;
            Action afterEach = () => { };
            bus.Subscribe<Hit>(m => total += m.Amount);
            bus.Subscribe<Hit>(m => total -= m.Amount / 2);

            void Frame()
            {
                for (var i = 0; i < 100; i++)
                    bus.Publish(new Hit(default, 1));
                bus.Dispatch(afterEach);
            }

            Frame();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++)
                Frame();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(total, Is.EqualTo(550).Within(1e-3));
#endif
        }

        private enum PokeError { Busy }

        private readonly struct Poke : ICommand
        {
            public Poke(EntityId target) => Target = target;

            public EntityId Target { get; }
        }

        private sealed class PokeHandler : ICommandHandler<Poke>
        {
            public int Accepted;

            public CommandResult Handle(Poke command) =>
                ++Accepted % 2 == 0 ? CommandResult.Ok : CommandResult.Rejected(PokeError.Busy);
        }

        [Test]
        public void Sending_commands_does_not_allocate_after_warmup()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Allocation checks run in dotnet test only");
#else
            var context = new GameplayContext();
            var handler = new PokeHandler();
            context.Commands.Register(handler);

            void Frame()
            {
                for (var i = 0; i < 100; i++)
                    context.Commands.Send(new Poke(default));
            }

            Frame();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++)
                Frame();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
            Assert.That(handler.Accepted, Is.EqualTo(1100));
#endif
        }

        [Test]
        public void Unsubscribing_during_delivery_is_safe()
        {
            var bus = new EventBus(NullLog.Instance) { ThrowOnSubscriberException = true };
            var calls = 0;
            IDisposable second = null;
            bus.Subscribe<Hit>(_ => { calls++; second.Dispose(); });
            second = bus.Subscribe<Hit>(_ => calls += 10);
            bus.Subscribe<Hit>(_ => calls += 100);

            bus.Publish(new Hit(default, 1));
            bus.Dispatch();
            bus.Publish(new Hit(default, 1));
            bus.Dispatch();

            Assert.That(calls, Is.EqualTo(202), "unsubscribed during delivery, not called again");
        }
    }
}
