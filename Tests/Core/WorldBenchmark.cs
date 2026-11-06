using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    [Explicit]
    public sealed class WorldBenchmark
    {
        private sealed class Position : IModel { public float X; }
        private sealed class Velocity : IModel { public float X = 1; }
        private sealed class Health : IModel { public float Value = 10; }

        private sealed class MoveSystem : EntitySystem<Position, Velocity>
        {
            public MoveSystem(GameplayContext context) : base(context) { }

            protected override void Tick(Entity entity, Position position, Velocity velocity, float deltaTime) =>
                position.X += velocity.X * deltaTime;
        }

        [TestCase(5000)]
        [TestCase(50000)]
        public void Systems_queries_and_lookups(int count)
        {
            var context = new GameplayContext();
            var world = context.World;
            var ids = new EntityId[count];
            for (var i = 0; i < count; i++)
            {
                var entity = world.Spawn(1);
                entity.Add(new Position(), new Velocity());
                if (i % 2 == 0)
                    entity.Add(new Health());
                ids[i] = entity.Id;
            }

            var system = new MoveSystem(context);
            const int frames = 100;

            var tick = Measure(() => { for (var f = 0; f < frames; f++) context.Simulation.Tick(system, 0.016f); }, out var tickBytes);
            var query = Measure(() =>
            {
                var sum = 0f;
                for (var f = 0; f < frames; f++)
                    foreach (var entity in world.Query<Health, Position>())
                        sum += entity.Get<Health>().Value;
            }, out var queryBytes);
            var lookups = Measure(() =>
            {
                var sum = 0f;
                for (var f = 0; f < frames; f++)
                    foreach (var id in ids)
                        sum += world.Get<Position>(id).X;
            }, out _);

            TestContext.Out.WriteLine($"{count} entities, {frames} frames:");
            TestContext.Out.WriteLine($"  EntitySystem<2>        {tick,8:0.0} ms  ({tick * 1000 / frames:0} us/frame), allocated {tickBytes / 1024} KB");
            TestContext.Out.WriteLine($"  Query<2> + Get         {query,8:0.0} ms  ({query * 1000 / frames:0} us/frame), allocated {queryBytes / 1024} KB");
            TestContext.Out.WriteLine($"  Get<T> by id           {lookups,8:0.0} ms  ({lookups * 1000 / frames:0} us/frame)");
        }

        [Serializable]
        private sealed class PositionPart : IEntityPart { public IModel CreateModel() => new Position(); }

        [Serializable]
        private sealed class VelocityPart : IEntityPart { public IModel CreateModel() => new Velocity(); }

        [Serializable]
        private sealed class HealthPart : IEntityPart { public IModel CreateModel() => new Health(); }

        [Test]
        public void Spawning_projectiles()
        {
            const int perFrame = 50;
            const int frames = 200;
            var context = new GameplayContext();
            var world = context.World;
            var projectile = new EntityDefinition(1, "Projectile", new IEntityPart[] { new PositionPart(), new VelocityPart(), new HealthPart() });
            var spawned = new EntityId[perFrame];
            Action spawn = () =>
            {
                for (var i = 0; i < perFrame; i++)
                    spawned[i] = world.Spawn(projectile).Id;
            };
            Action destroy = () =>
            {
                for (var i = 0; i < perFrame; i++)
                    world.Destroy(spawned[i]);
            };

            var time = Measure(() =>
            {
                for (var f = 0; f < frames; f++)
                {
                    context.Simulation.Run(spawn);
                    context.Simulation.Run(destroy);
                }
            }, out var bytes);

            var entities = perFrame * frames;
            TestContext.Out.WriteLine($"{entities} projectiles spawned and destroyed in actions (3 parts each):");
            TestContext.Out.WriteLine($"  {time * 1000 / entities:0.00} us and {bytes / (double)entities:0} bytes per projectile " +
                                      "(the Kind and the three components themselves)");
        }

        private static double Measure(Action action, out long allocated)
        {
            action();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return watch.Elapsed.TotalMilliseconds;
        }
    }
}
