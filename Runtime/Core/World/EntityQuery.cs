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

        internal EntityQuery(World world, ComponentStore<T> store)
        {
            _world = world;
            _store = store;
        }

        public QueryEnumerator GetEnumerator() => new QueryEnumerator(_world, _store, null, null);

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

        internal EntityQuery(World world, ComponentStore first, ComponentStore second)
        {
            _world = world;
            _first = first;
            _second = second;
        }

        public QueryEnumerator GetEnumerator() =>
            _first == null || _second == null ? default
            : _first.Count <= _second.Count ? new QueryEnumerator(_world, _first, _second, null)
            : new QueryEnumerator(_world, _second, _first, null);

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

        internal EntityQuery(World world, ComponentStore first, ComponentStore second, ComponentStore third)
        {
            _world = world;
            _first = first;
            _second = second;
            _third = third;
        }

        public QueryEnumerator GetEnumerator()
        {
            if (_first == null || _second == null || _third == null)
                return default;

            if (_first.Count <= _second.Count && _first.Count <= _third.Count)
                return new QueryEnumerator(_world, _first, _second, _third);
            if (_second.Count <= _third.Count)
                return new QueryEnumerator(_world, _second, _first, _third);
            return new QueryEnumerator(_world, _third, _first, _second);
        }

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct QueryEnumerator : IEnumerator<Entity>
    {
        private readonly World _world;
        private readonly ComponentStore _driver;
        private readonly ComponentStore _also;
        private readonly ComponentStore _andAlso;
        private readonly int _version;
        private int _position;

        internal QueryEnumerator(World world, ComponentStore driver, ComponentStore also, ComponentStore andAlso)
        {
            _world = world;
            _driver = driver;
            _also = also;
            _andAlso = andAlso;
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
                if ((_also == null || _also.Has(entity)) && (_andAlso == null || _andAlso.Has(entity)))
                    return true;
            }

            return false;
        }

        public void Reset() => _position = -1;

        public void Dispose() { }
    }
}
