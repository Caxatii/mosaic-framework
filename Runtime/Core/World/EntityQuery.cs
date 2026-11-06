using System;
using System.Collections;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Allocation-free iteration over entities that have all the given components; the smallest store drives the loop.
    /// Changing components during iteration is allowed only inside a Simulation action.
    /// </summary>
    public readonly struct EntityQuery<T> : IEnumerable<Entity> where T : class, IModel
    {
        private readonly World _world;
        private readonly ComponentStore<T> _store;
        private readonly Exclusions _without;

        internal EntityQuery(World world, ComponentStore<T> store, Exclusions without = default)
        {
            _world = world;
            _store = store;
            _without = without;
        }

        /// <summary>Skips entities that have <typeparamref name="TExcluded"/>. Up to two exclusions per query.</summary>
        public EntityQuery<T> Without<TExcluded>() where TExcluded : class, IModel =>
            new EntityQuery<T>(_world, _store, _without.And(_world.StoreOf<TExcluded>()));

        public QueryEnumerator GetEnumerator() => new QueryEnumerator(_world, _store, null, null, _without);

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct EntityQuery<T1, T2> : IEnumerable<Entity>
        where T1 : class, IModel
        where T2 : class, IModel
    {
        private readonly World _world;
        private readonly ComponentStore _first;
        private readonly ComponentStore _second;
        private readonly Exclusions _without;

        internal EntityQuery(World world, ComponentStore first, ComponentStore second, Exclusions without = default)
        {
            _world = world;
            _first = first;
            _second = second;
            _without = without;
        }

        /// <summary>Skips entities that have <typeparamref name="TExcluded"/>. Up to two exclusions per query.</summary>
        public EntityQuery<T1, T2> Without<TExcluded>() where TExcluded : class, IModel =>
            new EntityQuery<T1, T2>(_world, _first, _second, _without.And(_world.StoreOf<TExcluded>()));

        public QueryEnumerator GetEnumerator() =>
            _first == null || _second == null ? default
            : _first.Count <= _second.Count ? new QueryEnumerator(_world, _first, _second, null, _without)
            : new QueryEnumerator(_world, _second, _first, null, _without);

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public readonly struct EntityQuery<T1, T2, T3> : IEnumerable<Entity>
        where T1 : class, IModel
        where T2 : class, IModel
        where T3 : class, IModel
    {
        private readonly World _world;
        private readonly ComponentStore _first;
        private readonly ComponentStore _second;
        private readonly ComponentStore _third;
        private readonly Exclusions _without;

        internal EntityQuery(World world, ComponentStore first, ComponentStore second, ComponentStore third, Exclusions without = default)
        {
            _world = world;
            _first = first;
            _second = second;
            _third = third;
            _without = without;
        }

        /// <summary>Skips entities that have <typeparamref name="TExcluded"/>. Up to two exclusions per query.</summary>
        public EntityQuery<T1, T2, T3> Without<TExcluded>() where TExcluded : class, IModel =>
            new EntityQuery<T1, T2, T3>(_world, _first, _second, _third, _without.And(_world.StoreOf<TExcluded>()));

        public QueryEnumerator GetEnumerator()
        {
            if (_first == null || _second == null || _third == null)
                return default;

            if (_first.Count <= _second.Count && _first.Count <= _third.Count)
                return new QueryEnumerator(_world, _first, _second, _third, _without);
            if (_second.Count <= _third.Count)
                return new QueryEnumerator(_world, _second, _first, _third, _without);
            return new QueryEnumerator(_world, _third, _first, _second, _without);
        }

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal readonly struct Exclusions
    {
        private readonly ComponentStore _first;
        private readonly ComponentStore _second;
        private readonly int _count;

        private Exclusions(ComponentStore first, ComponentStore second, int count)
        {
            _first = first;
            _second = second;
            _count = count;
        }

        public Exclusions And(ComponentStore store) => _count switch
        {
            0 => new Exclusions(store, null, 1),
            1 => new Exclusions(_first, store, 2),
            _ => throw new InvalidOperationException("A query supports up to two Without; filter the rest inside the loop"),
        };

        public bool Excludes(int entity) =>
            _first != null && _first.Has(entity) || _second != null && _second.Has(entity);
    }

    public struct QueryEnumerator : IEnumerator<Entity>
    {
        private readonly World _world;
        private readonly ComponentStore _driver;
        private readonly ComponentStore _also;
        private readonly ComponentStore _andAlso;
        private readonly Exclusions _without;
        private readonly int _version;
        private int _position;

        internal QueryEnumerator(World world, ComponentStore driver, ComponentStore also, ComponentStore andAlso, Exclusions without)
        {
            _world = world;
            _driver = driver;
            _also = also;
            _andAlso = andAlso;
            _without = without;
            _version = driver?.Version ?? 0;
            _position = -1;
        }

        public Entity Current => _world.EntityAt(_driver.OwnerAt(_position));

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_driver == null)
                return false;

            if (_driver.Version != _version)
                throw new InvalidOperationException(
                    "Components changed during iteration. Change them inside a Simulation action (deferred World mode).");

            while (++_position < _driver.Count)
            {
                var entity = _driver.OwnerAt(_position);
                if (_world.IsAliveAt(entity) && (_also == null || _also.Has(entity)) && (_andAlso == null || _andAlso.Has(entity)) &&
                    !_without.Excludes(entity))
                    return true;
            }

            return false;
        }

        public void Reset() => _position = -1;

        public void Dispose() { }
    }
}
