using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// Receives the time of every piece of simulation work: "Tick WeaponSystem", "AttackCommand",
    /// "DamageApplied → HitFeedbackPresenter". Set it on <see cref="GameplayContext.Profiler"/>; null costs nothing.
    /// Names are the same string instances every time, so an implementation can cache by them without allocating.
    /// </summary>
    public interface ISimulationProfiler
    {
        void Begin(string name);
        void End();
    }

    public static class SimulationProfiler
    {
        /// <summary>Both profilers receive every measurement, for example the Unity Profiler and SimulationStats.</summary>
        public static ISimulationProfiler Combine(ISimulationProfiler first, ISimulationProfiler second) =>
            first == null ? second : second == null ? first : new Pair(first, second);

        /// <summary>The profiler without <paramref name="removed"/>, undoing <see cref="Combine"/>.</summary>
        public static ISimulationProfiler Without(ISimulationProfiler profiler, ISimulationProfiler removed)
        {
            if (profiler == null || profiler == removed)
                return null;
            if (profiler is Pair pair)
                return Combine(Without(pair.First, removed), Without(pair.Second, removed));
            return profiler;
        }

        private sealed class Pair : ISimulationProfiler
        {
            public Pair(ISimulationProfiler first, ISimulationProfiler second)
            {
                First = first;
                Second = second;
            }

            public ISimulationProfiler First { get; }
            public ISimulationProfiler Second { get; }

            public void Begin(string name)
            {
                First.Begin(name);
                Second.Begin(name);
            }

            public void End()
            {
                Second.End();
                First.End();
            }
        }
    }

    /// <summary>
    /// Time per system, command and subscriber, without Unity: calls, total, average and maximum. Times are inclusive
    /// (a command sent by a system is counted in both). Allocation-free once every name has been seen.
    /// </summary>
    public sealed class SimulationStats : ISimulationProfiler
    {
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly Stack<(Entry Entry, long Start)> _open = new Stack<(Entry, long)>();

        public void Begin(string name)
        {
            if (!_entries.TryGetValue(name, out var entry))
                _entries.Add(name, entry = new Entry(name));
            _open.Push((entry, Stopwatch.GetTimestamp()));
        }

        public void End()
        {
            if (_open.Count == 0)
                return;

            var (entry, start) = _open.Pop();
            var elapsed = Stopwatch.GetTimestamp() - start;
            entry.Calls++;
            entry.Total += elapsed;
            if (elapsed > entry.Max)
                entry.Max = elapsed;
        }

        /// <summary>Everything measured so far, the most expensive first.</summary>
        public IReadOnlyList<ProfileEntry> Snapshot() =>
            _entries.Values
                .Select(entry => new ProfileEntry(entry.Name, entry.Calls, ToMilliseconds(entry.Total), ToMilliseconds(entry.Max)))
                .OrderByDescending(entry => entry.TotalMilliseconds)
                .ToList();

        public void Reset()
        {
            _entries.Clear();
            _open.Clear();
        }

        private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private sealed class Entry
        {
            public Entry(string name) => Name = name;

            public string Name { get; }
            public long Calls;
            public long Total;
            public long Max;
        }
    }

    public readonly struct ProfileEntry
    {
        public ProfileEntry(string name, long calls, double totalMilliseconds, double maxMilliseconds)
        {
            Name = name;
            Calls = calls;
            TotalMilliseconds = totalMilliseconds;
            MaxMilliseconds = maxMilliseconds;
        }

        public string Name { get; }
        public long Calls { get; }
        public double TotalMilliseconds { get; }
        public double MaxMilliseconds { get; }
        public double AverageMilliseconds => Calls == 0 ? 0 : TotalMilliseconds / Calls;

        public override string ToString() =>
            $"{Name}: {Calls} calls, {TotalMilliseconds:0.###} ms total, {AverageMilliseconds * 1000:0.#} µs average, {MaxMilliseconds * 1000:0.#} µs max";
    }
}
