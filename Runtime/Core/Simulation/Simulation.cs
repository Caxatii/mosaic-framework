using System;

namespace Mosaic.Core
{
    /// <summary>
    /// Runs one action (a command or a system tick): executes it in deferred World mode, flushes the buffer,
    /// then delivers queued events, flushing again after each subscriber. Nested actions run immediately inside the outer one.
    /// </summary>
    public sealed class Simulation
    {
        private readonly World _world;
        private readonly EventBus _events;
        private readonly Action _flushWorld;
        private int _depth;

        public Simulation(World world, EventBus events)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _flushWorld = FlushWorld;
        }

        public World World => _world;
        public IEventBus Events => _events;

        public SimulationTrace Trace { get; internal set; }

        public bool IsRunning => _depth > 0;

        public void Run(Action action)
        {
            if (_depth > 0)
            {
                action();
                return;
            }

            Trace?.Enter("Run " + TraceFormat.OwnerOf(action), quiet: true);
            _depth++;
            try
            {
                using (_world.Defer())
                {
                    action();
                    Complete();
                }
            }
            finally
            {
                _depth--;
                Trace?.Exit();
            }
        }

        public TResult Run<TState, TResult>(Func<TState, TResult> action, TState state)
        {
            if (_depth > 0)
                return action(state);

            _depth++;
            try
            {
                using (_world.Defer())
                {
                    var result = action(state);
                    Complete();
                    return result;
                }
            }
            finally
            {
                _depth--;
            }
        }

        public void Tick(ISystem system, float deltaTime)
        {
            if (_depth > 0)
            {
                system.Tick(deltaTime);
                return;
            }

            Trace?.Enter(SystemLabel(system), quiet: true);
            _depth++;
            try
            {
                using (_world.Defer())
                {
                    system.Tick(deltaTime);
                    Complete();
                }
            }
            finally
            {
                _depth--;
                Trace?.Exit();
            }
        }

        private static string SystemLabel(ISystem system) => "Tick " + system.GetType().Name;

        private void Complete()
        {
            FlushWorld();
            _events.Dispatch(_flushWorld);
        }

        private void FlushWorld()
        {
            _world.Flush();

            var spawned = _world.DrainSpawned();
            for (var i = 0; i < spawned.Count; i++)
                _events.Publish(new EntitySpawned(spawned[i], _world.Get<Kind>(spawned[i]).Id));

            var destroyed = _world.DrainDestroyed();
            for (var i = 0; i < destroyed.Count; i++)
                _events.Publish(new EntityDestroyed(destroyed[i]));
        }
    }
}
