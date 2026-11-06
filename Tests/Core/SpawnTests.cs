using System.Collections.Generic;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class SpawnTests
    {
        private sealed class Marker : IModel
        {
        }

        private const int Grunt = 7;

        private GameplayContext _context;
        private List<EntitySpawned> _spawned;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext(throwOnSubscriberException: true);
            _spawned = new List<EntitySpawned>();
            _context.Events.Subscribe<EntitySpawned>(_spawned.Add);
        }

        [Test]
        public void Spawned_entity_has_its_kind()
        {
            var entity = _context.World.Spawn(Grunt);

            Assert.That(entity.Get<Kind>().Id, Is.EqualTo(Grunt));
        }

        [Test]
        public void Spawn_is_announced_after_components_are_visible()
        {
            var seenMarker = false;
            _context.Events.Subscribe<EntitySpawned>(m => seenMarker = _context.World.Has<Marker>(m.Entity));

            Entity entity = default;
            _context.Simulation.Run(() =>
            {
                entity = _context.World.Spawn(Grunt);
                entity.Add(new Marker());
                Assert.That(_spawned, Is.Empty, "not yet inside the action");
            });

            Assert.That(_spawned, Has.Count.EqualTo(1));
            Assert.That(_spawned[0].Entity, Is.EqualTo(entity.Id));
            Assert.That(_spawned[0].Kind, Is.EqualTo(Grunt));
            Assert.That(seenMarker, Is.True);
        }

        [Test]
        public void Entity_destroyed_in_the_same_action_is_not_announced()
        {
            _context.Simulation.Run(() => _context.World.Spawn(Grunt).Destroy());

            Assert.That(_spawned, Is.Empty);
        }

        private sealed class Health : IModel
        {
            public Health(float max) => Max = max;

            public float Max { get; }
        }

        private sealed class HealthDefinition : IEntityPart
        {
            public float Max = 30;

            public IModel CreateModel() => new Health(Max);
        }

        [Test]
        public void Definition_spawns_entity_with_a_model_per_part()
        {
            var grunt = new EntityDefinition(Grunt, "grunt", new IEntityPart[] { new HealthDefinition { Max = 30 } });

            var first = _context.World.Spawn(grunt);
            var second = _context.World.Spawn(grunt);

            Assert.That(first.Get<Kind>().Id, Is.EqualTo(Grunt));
            Assert.That(first.Get<Health>().Max, Is.EqualTo(30));
            Assert.That(first.Get<Health>(), Is.Not.SameAs(second.Get<Health>()), "each entity gets its own model");
            Assert.That(grunt.Get<HealthDefinition>(), Is.Not.Null);
        }

        [Test]
        public void Kind_is_required()
        {
            Assert.Throws<System.ArgumentException>(() => _context.World.Spawn(0));
        }
    }
}
