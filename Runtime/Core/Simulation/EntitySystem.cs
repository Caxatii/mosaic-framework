using System;

namespace Mosaic.Core
{
    /// <summary>
    /// Ticks every entity that has the given components without allocations. Tick it through Simulation.Tick.
    /// </summary>
    public abstract class EntitySystem<T> : ISystem
        where T : class, IModel
    {
        protected EntitySystem(GameplayContext context) =>
            World = (context ?? throw new ArgumentNullException(nameof(context))).World;

        protected World World { get; }

        public void Tick(float deltaTime)
        {
            var store = World.StoreOf<T>();
            if (store == null)
                return;

            var version = store.Version;
            for (var i = 0; i < store.Count; i++)
            {
                var index = store.OwnerAt(i);
                if (World.IsAliveAt(index))
                    Tick(World.EntityAt(index), store.ItemAt(i), deltaTime);
                StoreGuard.Check(store, version);
            }
        }

        protected abstract void Tick(Entity entity, T component, float deltaTime);
    }

    public abstract class EntitySystem<T1, T2> : ISystem
        where T1 : class, IModel
        where T2 : class, IModel
    {
        protected EntitySystem(GameplayContext context) =>
            World = (context ?? throw new ArgumentNullException(nameof(context))).World;

        protected World World { get; }

        public void Tick(float deltaTime)
        {
            var first = World.StoreOf<T1>();
            var second = World.StoreOf<T2>();
            if (first == null || second == null)
                return;

            if (first.Count <= second.Count)
            {
                var version = first.Version;
                for (var i = 0; i < first.Count; i++)
                {
                    var index = first.OwnerAt(i);
                    if (World.IsAliveAt(index) && second.Get(index) is { } other)
                        Tick(World.EntityAt(index), first.ItemAt(i), other, deltaTime);
                    StoreGuard.Check(first, version);
                }
            }
            else
            {
                var version = second.Version;
                for (var i = 0; i < second.Count; i++)
                {
                    var index = second.OwnerAt(i);
                    if (World.IsAliveAt(index) && first.Get(index) is { } other)
                        Tick(World.EntityAt(index), other, second.ItemAt(i), deltaTime);
                    StoreGuard.Check(second, version);
                }
            }
        }

        protected abstract void Tick(Entity entity, T1 first, T2 second, float deltaTime);
    }

    public abstract class EntitySystem<T1, T2, T3> : ISystem
        where T1 : class, IModel
        where T2 : class, IModel
        where T3 : class, IModel
    {
        protected EntitySystem(GameplayContext context) =>
            World = (context ?? throw new ArgumentNullException(nameof(context))).World;

        protected World World { get; }

        public void Tick(float deltaTime)
        {
            var first = World.StoreOf<T1>();
            var second = World.StoreOf<T2>();
            var third = World.StoreOf<T3>();
            if (first == null || second == null || third == null)
                return;

            var version = first.Version;
            for (var i = 0; i < first.Count; i++)
            {
                var index = first.OwnerAt(i);
                if (World.IsAliveAt(index) && second.Get(index) is { } b && third.Get(index) is { } c)
                    Tick(World.EntityAt(index), first.ItemAt(i), b, c, deltaTime);
                StoreGuard.Check(first, version);
            }
        }

        protected abstract void Tick(Entity entity, T1 first, T2 second, T3 third, float deltaTime);
    }

    internal static class StoreGuard
    {
        public static void Check(ComponentStore store, int version)
        {
            if (store.Version != version)
                throw new InvalidOperationException(
                    "Components changed while the system was iterating. Tick systems through Simulation.Tick (deferred World mode).");
        }
    }
}
