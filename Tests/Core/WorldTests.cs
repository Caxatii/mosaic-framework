using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class WorldTests
    {
        private sealed class Marker : IModel { }
        private sealed class Other : IModel { }

        [Test]
        public void Destroyed_slot_is_reused_with_new_generation()
        {
            var world = new World();
            var first = world.CreateEntity();
            first.Destroy();

            var second = world.CreateEntity();

            Assert.That(second.Id.Index, Is.EqualTo(first.Id.Index));
            Assert.That(second.Id.Generation, Is.Not.EqualTo(first.Id.Generation));
            Assert.That(world.IsAlive(first.Id), Is.False);
            Assert.That(world.IsAlive(second.Id), Is.True);
        }

        [Test]
        public void Stale_id_does_not_see_components_of_new_entity()
        {
            var world = new World();
            var first = world.CreateEntity();
            first.Destroy();
            var second = world.CreateEntity();
            second.Add(new Marker());

            Assert.That(world.Get<Marker>(first.Id), Is.Null);
        }

        [Test]
        public void Destroying_twice_throws_and_does_not_duplicate_free_slot()
        {
            var world = new World();
            var entity = world.CreateEntity();
            entity.Destroy();

            Assert.Throws<EntityNotAliveException>(() => world.Destroy(entity.Id));

            var a = world.CreateEntity();
            var b = world.CreateEntity();
            Assert.That(a.Id.Index, Is.Not.EqualTo(b.Id.Index));
        }

        [Test]
        public void Deferred_changes_are_invisible_until_flush()
        {
            var world = new World();
            var entity = world.CreateEntity();

            using (world.Defer())
            {
                entity.Add(new Marker());
                Assert.That(entity.Has<Marker>(), Is.False);

                world.Flush();
                Assert.That(entity.Has<Marker>(), Is.True);

                entity.Destroy();
                Assert.That(entity.IsAlive, Is.True, "destruction is deferred until Flush");

                world.Flush();
                Assert.That(entity.IsAlive, Is.False);
            }
        }

        [Test]
        public void Destroying_during_deferred_query_is_safe()
        {
            var world = new World();
            for (var i = 0; i < 5; i++)
                world.CreateEntity().Add(new Marker());

            using (world.Defer())
            {
                foreach (var entity in world.Query<Marker>())
                    entity.Destroy();

                world.Flush();
            }

            Assert.That(world.Query<Marker>().Count(), Is.Zero);
            Assert.That(world.DrainDestroyed().Count, Is.EqualTo(5));
        }

        [Test]
        public void Query_with_two_components_returns_only_entities_having_both()
        {
            var world = new World();
            var both = world.CreateEntity();
            both.Add(new Marker(), new Other());
            world.CreateEntity().Add(new Marker());
            world.CreateEntity().Add(new Other());

            var result = world.Query<Marker, Other>().ToList();

            Assert.That(result, Is.EqualTo(new[] { both }));
        }
    }
}
