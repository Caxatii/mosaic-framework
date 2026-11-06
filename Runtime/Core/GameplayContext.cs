using System;

namespace Mosaic.Core
{
    /// <summary>
    /// Shared objects of one world: World, buses, Simulation and log.
    /// throwOnSubscriberException enables strict mode for tests: subscriber exceptions and stale reads throw.
    /// </summary>
    public sealed class GameplayContext
    {
        public GameplayContext(ILog log = null, bool throwOnSubscriberException = false)
        {
            Log = log ?? NullLog.Instance;
            World = new World { StaleReads = throwOnSubscriberException ? StaleReadCheck.Throw : DefaultStaleReads };
            World.StaleRead += (_, _, message) => Log.Warning(message);
            EventBus = new EventBus(Log) { ThrowOnSubscriberException = throwOnSubscriberException };
            Simulation = new Simulation(World, EventBus);
            Commands = new CommandBus(Simulation, Log);
        }

#if DEBUG || UNITY_EDITOR || DEVELOPMENT_BUILD
        private const StaleReadCheck DefaultStaleReads = StaleReadCheck.Warn;
#else
        private const StaleReadCheck DefaultStaleReads = StaleReadCheck.Off;
#endif

        public World World { get; }
        public EventBus EventBus { get; }
        public IEventBus Events => EventBus;
        public Simulation Simulation { get; }
        public CommandBus Commands { get; }
        public ILog Log { get; }

        /// <summary>Receives the time of every system tick, command and event subscriber; null (default) costs nothing.</summary>
        public ISimulationProfiler Profiler
        {
            get => Simulation.Profiler;
            set
            {
                Simulation.Profiler = value;
                EventBus.Profiler = value;
            }
        }

        public SimulationTrace Trace
        {
            get => Simulation.Trace;
            set
            {
                Simulation.Trace = value;
                EventBus.Trace = value;
            }
        }
    }
}
