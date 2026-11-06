using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class DestroyTests
    {
        private sealed class Coin : IModel { }
        private sealed class Wallet : IModel { public int Coins; }

        private readonly struct Poke : IEntityCommand
        {
            public Poke(EntityId target) => Target = target;
            public EntityId Target { get; }
            EntityId IEntityCommand.Entity => Target;
        }

        private sealed class PokeHandler : CommandHandler
        {
            public int Calls;
            public PokeHandler(GameplayContext context) : base(context) => On<Poke>((_, _) => { Calls++; return CommandResult.Ok; });
        }

        private sealed class CollectSystem : EntitySystem<Wallet>
        {
            public CollectSystem(GameplayContext context) : base(context) { }

            protected override void Tick(Entity collector, Wallet wallet, float deltaTime)
            {
                foreach (var coin in World.Query<Coin>())
                {
                    wallet.Coins++;
                    coin.Destroy();
                }
            }
        }

        private GameplayContext _context;
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext(throwOnSubscriberException: true);
            _world = _context.World;
        }

        private Entity Spawn(params IModel[] components)
        {
            var entity = _world.Spawn(1);
            entity.Add(components);
            return entity;
        }

        [Test]
        public void A_destroyed_entity_is_unavailable_within_the_same_action()
        {
            var coin = Spawn(new Coin());

            _context.Simulation.Run(() =>
            {
                coin.Destroy();

                Assert.That(coin.IsAlive, Is.False);
                Assert.That(_world.Get<Coin>(coin.Id), Is.Null);
                Assert.That(_world.TryGetEntity(coin.Id, out _), Is.False);
                Assert.That(_world.Query<Coin>(), Is.Empty);
                Assert.Throws<EntityNotAliveException>(() => coin.Get<Coin>());
                Assert.Throws<EntityNotAliveException>(() => coin.Destroy(), "destroying twice is a bug, not a no-op");
            });
        }

        [Test]
        public void Two_collectors_cannot_take_the_same_coin_in_one_tick()
        {
            var first = Spawn(new Wallet());
            var second = Spawn(new Wallet());
            Spawn(new Coin());

            _context.Simulation.Tick(new CollectSystem(_context), 0.1f);

            Assert.That(first.Get<Wallet>().Coins + second.Get<Wallet>().Coins, Is.EqualTo(1));
        }

        [Test]
        public void Systems_skip_entities_destroyed_earlier_in_the_tick()
        {
            var visited = new List<EntityId>();
            var a = Spawn(new Wallet());
            var b = Spawn(new Wallet());

            _context.Simulation.Run(() =>
            {
                b.Destroy();
                foreach (var entity in _world.Query<Wallet>())
                    visited.Add(entity.Id);
            });

            Assert.That(visited, Is.EqualTo(new[] { a.Id }));
        }

        [Test]
        public void Commands_to_a_destroyed_entity_are_rejected()
        {
            var handler = new PokeHandler(_context);
            var target = Spawn(new Coin());
            var result = default(CommandResult);

            _context.Simulation.Run(() =>
            {
                target.Destroy();
                result = _context.Commands.Send(new Poke(target.Id));
            });

            Assert.That(result.Reason, Is.EqualTo(CommandError.EntityNotFound));
            Assert.That(handler.Calls, Is.Zero);
        }

        [Test]
        public void The_slot_and_the_event_wait_for_the_end_of_the_action()
        {
            var destroyed = new List<EntityId>();
            _context.Events.Subscribe<EntityDestroyed>(e => destroyed.Add(e.Entity));
            var coin = Spawn(new Coin());

            _context.Simulation.Run(() =>
            {
                coin.Destroy();
                var fresh = _world.Spawn(1);

                Assert.That(fresh.Id.Index, Is.Not.EqualTo(coin.Id.Index), "the slot is not reused inside the action");
                Assert.That(destroyed, Is.Empty, "EntityDestroyed comes after the action");
            });

            Assert.That(destroyed, Is.EqualTo(new[] { coin.Id }));
            Assert.That(_world.Spawn(1).Id.Index, Is.EqualTo(coin.Id.Index), "after the action the slot is free again");
        }

        [Test]
        public void Changes_buffered_for_a_destroyed_entity_are_dropped()
        {
            var entity = Spawn(new Wallet());

            _context.Simulation.Run(() =>
            {
                entity.Add(new Coin());
                entity.Destroy();
            });

            Assert.That(_world.Query<Coin>().Count(), Is.Zero);
        }
    }
}
