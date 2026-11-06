using System;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class QueryTests
    {
        private sealed class A : IModel { public int Value; }
        private sealed class B : IModel { }
        private sealed class C : IModel { }

        private sealed class SumSystem : EntitySystem<A, B>
        {
            public int Sum;
            public SumSystem(GameplayContext context) : base(context) { }
            protected override void Tick(Entity entity, A a, B b, float deltaTime) => Sum += a.Value;
        }

        private sealed class TripleSystem : EntitySystem<A, B, C>
        {
            public int Visited;
            public TripleSystem(GameplayContext context) : base(context) { }
            protected override void Tick(Entity entity, A a, B b, C c, float deltaTime) => Visited++;
        }

        private sealed class DestroyingSystem : EntitySystem<A>
        {
            public DestroyingSystem(GameplayContext context) : base(context) { }
            protected override void Tick(Entity entity, A a, float deltaTime) => entity.Destroy();
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
        public void Queries_find_entities_having_all_components()
        {
            var ab = Spawn(new A(), new B());
            var abc = Spawn(new A(), new B(), new C());
            Spawn(new A(), new C());
            Spawn(new B());

            Assert.That(_world.Query<A, B>().Select(e => e.Id), Is.EquivalentTo(new[] { ab.Id, abc.Id }));
            Assert.That(_world.Query<A, B, C>().Select(e => e.Id), Is.EqualTo(new[] { abc.Id }));
            Assert.That(_world.Query<C>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void Removing_from_the_middle_keeps_the_rest_reachable()
        {
            var entities = Enumerable.Range(0, 5).Select(i => Spawn(new A { Value = i })).ToList();

            entities[1].Destroy();
            entities[3].Remove<A>();

            Assert.That(_world.Query<A>().Select(e => e.Get<A>().Value), Is.EquivalentTo(new[] { 0, 2, 4 }));
            Assert.That(entities[4].Get<A>().Value, Is.EqualTo(4));
        }

        [Test]
        public void Reused_slot_does_not_inherit_components()
        {
            var old = Spawn(new A(), new B());
            old.Destroy();

            var fresh = Spawn(new C());

            Assert.That(fresh.Id.Index, Is.EqualTo(old.Id.Index), "slot is reused");
            Assert.That(fresh.Has<A>() || fresh.Has<B>(), Is.False);
            Assert.That(_world.IsAlive(old.Id), Is.False);
        }

        [Test]
        public void Systems_see_matching_entities_in_both_store_orders()
        {
            for (var i = 0; i < 10; i++)
                Spawn(new A { Value = 1 });
            for (var i = 0; i < 3; i++)
                Spawn(new A { Value = 10 }, new B());
            var system = new SumSystem(_context);

            _context.Simulation.Tick(system, 0);
            Assert.That(system.Sum, Is.EqualTo(30), "store B is smaller and drives");

            for (var i = 0; i < 20; i++)
                Spawn(new B());
            system.Sum = 0;
            _context.Simulation.Tick(system, 0);
            Assert.That(system.Sum, Is.EqualTo(30), "store A is smaller and drives");
        }

        [Test]
        public void Three_component_system_visits_only_full_sets()
        {
            Spawn(new A(), new B());
            Spawn(new A(), new B(), new C());
            Spawn(new B(), new C());
            var system = new TripleSystem(_context);

            _context.Simulation.Tick(system, 0);

            Assert.That(system.Visited, Is.EqualTo(1));
        }

        [Test]
        public void Changing_components_while_iterating_outside_an_action_throws()
        {
            Spawn(new A());
            Spawn(new A());

            Assert.Throws<InvalidOperationException>(() =>
            {
                foreach (var entity in _world.Query<A>())
                    entity.Destroy();
            });

            Assert.Throws<InvalidOperationException>(() => new DestroyingSystem(_context).Tick(0));
        }

        [Test]
        public void Changing_components_inside_an_action_is_safe()
        {
            for (var i = 0; i < 5; i++)
                Spawn(new A());

            _context.Simulation.Tick(new DestroyingSystem(_context), 0);

            Assert.That(_world.Query<A>(), Is.Empty);
            Assert.That(_world.Count, Is.Zero);
        }

        [Test]
        public void Queries_and_systems_do_not_allocate()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Allocation checks run in dotnet test only");
#else
            for (var i = 0; i < 100; i++)
                Spawn(new A { Value = 1 }, new B(), new C());
            var pair = new SumSystem(_context);
            var triple = new TripleSystem(_context);

            void Frame()
            {
                foreach (var entity in _world.Query<A, B, C>())
                    pair.Sum += entity.Get<A>().Value;
                _context.Simulation.Tick(pair, 0.1f);
                _context.Simulation.Tick(triple, 0.1f);
            }

            Frame();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++)
                Frame();

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
#endif
        }
    }
}
