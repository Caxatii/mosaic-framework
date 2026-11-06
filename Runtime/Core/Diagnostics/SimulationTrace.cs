using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Mosaic.Core
{
    public readonly struct TraceLine
    {
        public TraceLine(int depth, string text)
        {
            Depth = depth;
            Text = text;
        }

        public int Depth { get; }
        public string Text { get; }
    }

    public sealed class TraceRecord
    {
        internal TraceRecord(long frame, TraceLine[] lines)
        {
            Frame = frame;
            Lines = lines;
        }

        public long Frame { get; }

        public IReadOnlyList<TraceLine> Lines { get; }

        public override string ToString()
        {
            var text = new StringBuilder();
            foreach (var line in Lines)
                text.Append(' ', line.Depth * 2).AppendLine(line.Text);
            return text.ToString();
        }
    }

    /// <summary>
    /// Debug tree of what happened in the simulation: command, published events, subscribers and their consequences.
    /// Off by default (GameplayContext.Trace is null) and free when off.
    /// </summary>
    public sealed class SimulationTrace
    {
        private readonly int _capacity;
        private readonly Queue<TraceRecord> _recent = new Queue<TraceRecord>();
        private readonly List<Frame> _stack = new List<Frame>();
        private readonly List<TraceLine> _lines = new List<TraceLine>();
        private long _frame;

        public SimulationTrace(int capacity = 256) => _capacity = Math.Max(1, capacity);

        public event Action<TraceRecord> Recorded;

        public IReadOnlyCollection<TraceRecord> Recent => _recent;

        public bool IncludeQuiet { get; set; }

        public void Clear() => _recent.Clear();

        internal void Enter(string label, bool quiet)
        {
            _stack.Add(new Frame(label, -1, quiet));
            if (!quiet || IncludeQuiet)
                Materialize();
        }

        internal void Exit(string suffix = null)
        {
            var frame = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);

            if (suffix != null && frame.Line >= 0)
            {
                var line = _lines[frame.Line];
                _lines[frame.Line] = new TraceLine(line.Depth, line.Text + suffix);
            }

            if (_stack.Count == 0)
                Complete();
        }

        internal void Line(string text)
        {
            Materialize();
            _lines.Add(new TraceLine(_stack.Count, text));
            if (_stack.Count == 0)
                Complete();
        }

        private void Materialize()
        {
            for (var depth = 0; depth < _stack.Count; depth++)
            {
                var frame = _stack[depth];
                if (frame.Line >= 0)
                    continue;

                _stack[depth] = new Frame(frame.Label, _lines.Count, frame.Quiet);
                _lines.Add(new TraceLine(depth, frame.Label));
            }
        }

        private void Complete()
        {
            _frame++;
            if (_lines.Count == 0)
                return;

            var record = new TraceRecord(_frame, _lines.ToArray());
            _lines.Clear();

            _recent.Enqueue(record);
            while (_recent.Count > _capacity)
                _recent.Dequeue();

            Recorded?.Invoke(record);
        }

        private readonly struct Frame
        {
            public Frame(string label, int line, bool quiet)
            {
                Label = label;
                Line = line;
                Quiet = quiet;
            }

            public string Label { get; }
            public int Line { get; }
            public bool Quiet { get; }
        }
    }

    public static class TraceFormat
    {
        private static readonly Dictionary<Type, MemberInfo[]> Members = new Dictionary<Type, MemberInfo[]>();

        public static string Of(object value)
        {
            if (value == null)
                return "null";

            var type = value.GetType();
            if (!Members.TryGetValue(type, out var members))
                Members.Add(type, members = Collect(type));

            var text = new StringBuilder(type.Name);
            if (members.Length == 0)
                return text.ToString();

            text.Append('(');
            for (var i = 0; i < members.Length; i++)
            {
                if (i > 0)
                    text.Append(", ");

                var member = members[i];
                var memberValue = member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value);
                text.Append(member.Name).Append('=').Append(Convert.ToString(memberValue, CultureInfo.InvariantCulture));
            }

            return text.Append(')').ToString();
        }

        public static string OwnerOf(Delegate handler)
        {
            var type = handler.Method.DeclaringType;
            while (type != null && type.Name.StartsWith("<") && type.DeclaringType != null)
                type = type.DeclaringType;
            return type?.Name ?? "?";
        }

        private static MemberInfo[] Collect(Type type)
        {
            var result = new List<MemberInfo>();
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                    result.Add(property);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                result.Add(field);
            return result.ToArray();
        }
    }
}
