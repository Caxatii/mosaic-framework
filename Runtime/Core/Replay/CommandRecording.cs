using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Records external input: commands sent from outside the simulation, their results and time steps.
    /// Commands sent by the simulation itself (AI, reactions) are not recorded. Replaying from the same start in the same build repeats the match.
    /// </summary>
    public sealed class CommandRecording
    {
        private readonly List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public Dictionary<string, string> Header { get; } = new Dictionary<string, string>();

        public int TickCount { get; private set; }

        public void RecordTick(float deltaTime)
        {
            if (deltaTime <= 0)
                return;

            _entries.Add(Entry.ForTick(deltaTime));
            TickCount++;
        }

        public void RecordCommand(ICommand command, CommandResult result) =>
            _entries.Add(Entry.ForCommand(command, ResultText(result)));

        /// <summary>
        /// Replays the recording. Returns the first command whose result differs, or null.
        /// </summary>
        public ReplayDivergence Play(CommandBus bus, Action<float> tick)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));
            if (tick == null)
                throw new ArgumentNullException(nameof(tick));

            var ticks = 0;
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.IsTick)
                {
                    tick(entry.DeltaTime);
                    ticks++;
                    continue;
                }

                var actual = ResultText(bus.SendBoxed(entry.Command));
                if (actual != entry.Result)
                    return new ReplayDivergence(i, ticks, entry.Command, entry.Result, actual);
            }

            return null;
        }

        internal void Add(Entry entry)
        {
            _entries.Add(entry);
            if (entry.IsTick)
                TickCount++;
        }

        private static string ResultText(CommandResult result) => result.ToString();

        public readonly struct Entry
        {
            private Entry(float deltaTime, ICommand command, string result)
            {
                DeltaTime = deltaTime;
                Command = command;
                Result = result;
            }

            public float DeltaTime { get; }

            public ICommand Command { get; }

            public string Result { get; }

            public bool IsTick => Command == null;

            internal static Entry ForTick(float deltaTime) => new Entry(deltaTime, null, null);
            internal static Entry ForCommand(ICommand command, string result) => new Entry(0, command, result);
        }
    }

    public sealed class ReplayDivergence
    {
        internal ReplayDivergence(int entry, int tick, ICommand command, string expected, string actual)
        {
            Entry = entry;
            Tick = tick;
            Command = command;
            Expected = expected;
            Actual = actual;
        }

        public int Entry { get; }
        public int Tick { get; }
        public ICommand Command { get; }
        public string Expected { get; }
        public string Actual { get; }

        public override string ToString() =>
            $"Diverged at entry {Entry} (after {Tick} ticks): {TraceFormat.Of(Command)} → {Actual}, recorded {Expected}";
    }
}
