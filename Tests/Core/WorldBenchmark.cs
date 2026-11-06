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
