using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class VisibilityTests
    {
        private sealed class Health : IModel { }
        private sealed class Shield : IModel { }

        private const int Kind = 1;

        private World _world;

        [SetUp]
        public void SetUp() => _world = new World { StaleReads = StaleReadCheck.Throw };

        [Test]
        public void Entity_being_built_sees_its_components_but_queries_do_not()
        {
            using (_world.Defer())
            {
                var entity = _world.Spawn(Kind);
                entity.Add(new Health());

                Assert.That(entity.IsBuilding);
                Assert.That(entity.Get<Health>(), Is.Not.Null, "the creator finishes building the entity");
                Assert.That(_world.Query<Health>(), Is.Empty, "everyone else sees it after the action");
            }

            _world.Flush();

            var built = _world.Query<Health>().Single();
            Assert.That(built.IsBuilding, Is.False);
        }

        [Test]
        public void Removing_from_entity_being_built_is_visible_at_once()
        {
            using (_world.Defer())
            {
                var entity = _world.Spawn(Kind);
                entity.Add(new Health());
                entity.Remove<Health>();

                Assert.That(entity.Has<Health>(), Is.False);
            }

            _world.Flush();
            Assert.That(_world.Query<Health>(), Is.Empty);
        }

        [Test]
        public void Reading_a_component_changed_in_the_same_action_is_reported()
        {
            var entity = _world.Spawn(Kind);

            using (_world.Defer())
            {
                entity.Add(new Shield());

                var error = Assert.Throws<InvalidOperationException>(() => entity.Get<Shield>());
                Assert.That(error.Message, Does.Contain("Shield"));
                Assert.DoesNotThrow(() => entity.Get<Health>(), "other components read without a warning");
            }

            _world.Flush();
            Assert.That(entity.Get<Shield>(), Is.Not.Null, "after the action the change is visible and not reported");
        }

        [Test]
        public void Warn_mode_reports_and_returns_the_old_state()
        {
            _world.StaleReads = StaleReadCheck.Warn;
            var reports = new List<Type>();
            _world.StaleRead += (_, type, _) => reports.Add(type);

            var entity = _world.Spawn(Kind);
            entity.Add(new Health());

            using (_world.Defer())
            {
                entity.Remove<Health>();
                Assert.That(entity.Get<Health>(), Is.Not.Null, "the old state until the action ends");
            }

            Assert.That(reports, Is.EqualTo(new[] { typeof(Health) }));
        }

        [Test]
        public void Off_mode_does_not_track_anything()
        {
            _world.StaleReads = StaleReadCheck.Off;
            var entity = _world.Spawn(Kind);

            using (_world.Defer())
            {
                entity.Add(new Shield());
                Assert.That(entity.Get<Shield>(), Is.Null);
            }
        }
    }
}
