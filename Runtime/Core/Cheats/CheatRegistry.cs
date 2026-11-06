using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// Cheats as named commands, registered once and used by the console (typed text) and the cheat window (buttons
    /// and sliders). A cheat only builds a command; the command goes through the bus like any input, so its handler
    /// calls the Operations of the features, the events and their consequences happen as in a normal game, and the
    /// command is recorded with the match.
    /// </summary>
    public sealed class CheatRegistry
    {
        public const string HelpCommand = "help";

        private readonly Func<CommandBus> _commands;
        private readonly List<Cheat> _cheats = new List<Cheat>();

        /// <param name="commands">The bus of the running match, or null when there is none (cheats then report it).</param>
        public CheatRegistry(Func<CommandBus> commands) =>
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));

        public IReadOnlyList<Cheat> All => _cheats;

        /// <summary>Cheats with no text arguments: the window shows them as buttons, numbers as sliders or fields.</summary>
        public IEnumerable<Cheat> ForWindow => _cheats.Where(cheat => cheat.FitsWindow);

        public Cheat Add(string name, string description, Func<CheatArgs, ICommand> create)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
                throw new ArgumentException("A cheat name is one word", nameof(name));
            if (Find(name) != null || string.Equals(name, HelpCommand, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Cheat '{name}' already exists", nameof(name));

            var cheat = new Cheat(name, description ?? "", create ?? throw new ArgumentNullException(nameof(create)));
            _cheats.Add(cheat);
            return cheat;
        }

        public Cheat Find(string name) =>
            _cheats.FirstOrDefault(cheat => string.Equals(cheat.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>One line per cheat: usage and description.</summary>
        public string Help() =>
            string.Join("\n", _cheats.Select(cheat => cheat.Description.Length > 0 ? $"{cheat.Usage} — {cheat.Description}" : cheat.Usage));

        /// <summary>Parses a console line and runs the cheat: <c>give medkit 3</c>.</summary>
        public CheatResult Execute(string line)
        {
            var words = Split(line);
            if (words.Count == 0 || string.Equals(words[0], HelpCommand, StringComparison.OrdinalIgnoreCase))
                return CheatResult.Info(Help());

            var cheat = Find(words[0]);
            if (cheat == null)
                return CheatResult.Failed($"Unknown cheat '{words[0]}'. Type {HelpCommand}.");

            var values = new object[cheat.Parameters.Count];
            for (var i = 0; i < cheat.Parameters.Count; i++)
            {
                var parameter = cheat.Parameters[i];
                var text = i + 1 < words.Count ? words[i + 1] : null;
                if (text == null)
                {
                    if (!parameter.IsOptional)
                        return CheatResult.Failed($"Missing {parameter.Name}. Usage: {cheat.Usage}");
                    values[i] = parameter.Default;
                    continue;
                }

                if (!parameter.TryParse(text, out values[i], out var problem))
                    return CheatResult.Failed($"{parameter.Name}: {problem}. Usage: {cheat.Usage}");
            }

            if (words.Count > cheat.Parameters.Count + 1)
                return CheatResult.Failed($"Too many arguments. Usage: {cheat.Usage}");

            return Run(cheat, values);
        }

        /// <summary>Runs a cheat of the window: one number per parameter, in order.</summary>
        public CheatResult Execute(Cheat cheat, IReadOnlyList<float> numbers)
        {
            if (cheat == null)
                throw new ArgumentNullException(nameof(cheat));
            if (!cheat.FitsWindow)
                throw new ArgumentException($"Cheat '{cheat.Name}' takes text and runs from the console only", nameof(cheat));

            var values = new object[cheat.Parameters.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var parameter = cheat.Parameters[i];
                var number = numbers != null && i < numbers.Count ? numbers[i] : parameter.Initial;
                values[i] = parameter.Clamp(number);
            }

            return Run(cheat, values);
        }

        /// <summary>Completions for the last word of a console line: cheat names, then the choices of the argument.</summary>
        public IReadOnlyList<string> Complete(string line)
        {
            line = line ?? "";
            var words = Split(line);
            var completingNew = line.Length == 0 || char.IsWhiteSpace(line[line.Length - 1]);
            var index = completingNew ? words.Count : words.Count - 1;
            var prefix = completingNew || words.Count == 0 ? "" : words[words.Count - 1];

            IEnumerable<string> candidates;
            if (index <= 0)
                candidates = _cheats.Select(cheat => cheat.Name).Append(HelpCommand);
            else if (Find(words[0]) is { } cheat && index - 1 < cheat.Parameters.Count)
                candidates = cheat.Parameters[index - 1].Choices?.Invoke() ?? Enumerable.Empty<string>();
            else
                candidates = Enumerable.Empty<string>();

            return candidates
                .Where(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private CheatResult Run(Cheat cheat, object[] values)
        {
            var commands = _commands();
            if (commands == null)
                return CheatResult.Failed("No game is running");

            ICommand command;
            try
            {
                command = cheat.Create(new CheatArgs(values));
            }
            catch (CheatException exception)
            {
                return CheatResult.Failed(exception.Message);
            }

            if (command == null)
                return CheatResult.Failed("Nothing to do");

            var result = commands.SendBoxed(command);
            return result.IsOk ? CheatResult.Done(cheat.Name) : CheatResult.Failed($"Rejected: {result.Reason}");
        }

        private static List<string> Split(string line) =>
            (line ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public sealed class Cheat
    {
        private readonly List<CheatParameter> _parameters = new List<CheatParameter>();

        internal Cheat(string name, string description, Func<CheatArgs, ICommand> create)
        {
            Name = name;
            Description = description;
            Create = create;
        }

        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<CheatParameter> Parameters => _parameters;
        internal Func<CheatArgs, ICommand> Create { get; }

        /// <summary>No text arguments: the window can run it.</summary>
        public bool FitsWindow => _parameters.All(parameter => parameter.IsNumber);

        /// <summary><c>give &lt;item&gt; [count]</c>: optional arguments in brackets.</summary>
        public string Usage =>
            string.Join(" ", new[] { Name }.Concat(_parameters.Select(p => p.IsOptional ? $"[{p.Name}]" : $"<{p.Name}>")));

        /// <summary>A word typed in the console (an item, a buff); <paramref name="choices"/> feed autocompletion.</summary>
        public Cheat Word(string name, Func<IEnumerable<string>> choices = null)
        {
            if (_parameters.Any(p => p.IsOptional))
                throw new InvalidOperationException("A required argument can't follow an optional one");

            _parameters.Add(new CheatParameter(name, isNumber: false, 0, 0, null, false, choices));
            return this;
        }

        /// <summary>
        /// A number in [min, max]: a slider in the window when the range is known, a field otherwise.
        /// With a default the argument is optional.
        /// </summary>
        public Cheat Number(string name, float min = float.MinValue, float max = float.MaxValue, float? @default = null, bool whole = false)
        {
            if (min > max)
                throw new ArgumentException("min is greater than max");
            if (@default == null && _parameters.Any(p => p.IsOptional))
                throw new InvalidOperationException("A required argument can't follow an optional one");

            var value = @default ?? Math.Max(min, Math.Min(max, 0));
            _parameters.Add(new CheatParameter(name, isNumber: true, min, max, @default == null ? (object)null : value, whole, null)
            {
                IsOptional = @default != null,
                Initial = value,
            });
            return this;
        }
    }

    public sealed class CheatParameter
    {
        internal CheatParameter(string name, bool isNumber, float min, float max, object @default, bool whole, Func<IEnumerable<string>> choices)
        {
            Name = name;
            IsNumber = isNumber;
            Min = min;
            Max = max;
            Default = @default;
            IsWhole = whole;
            Choices = choices;
        }

        public string Name { get; }
        public bool IsNumber { get; }
        public bool IsWhole { get; }
        public float Min { get; }
        public float Max { get; }
        public bool IsOptional { get; internal set; }
        public object Default { get; }

        /// <summary>The value the window starts with.</summary>
        public float Initial { get; internal set; }

        /// <summary>Both ends are known, so the window shows a slider.</summary>
        public bool HasRange => IsNumber && Min > float.MinValue && Max < float.MaxValue;

        public Func<IEnumerable<string>> Choices { get; }

        internal bool TryParse(string text, out object value, out string problem)
        {
            value = null;
            problem = null;
            if (!IsNumber)
            {
                value = text;
                return true;
            }

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                problem = $"'{text}' is not a number";
                return false;
            }

            if (number < Min || number > Max)
            {
                problem = $"{text} is outside {Min.ToString(CultureInfo.InvariantCulture)}..{Max.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            value = IsWhole ? (float)Math.Round(number) : number;
            return true;
        }

        internal float Clamp(float number)
        {
            number = Math.Max(Min, Math.Min(Max, number));
            return IsWhole ? (float)Math.Round(number) : number;
        }
    }

    /// <summary>The arguments of one run, in the order the parameters were declared.</summary>
    public readonly struct CheatArgs
    {
        private readonly object[] _values;

        internal CheatArgs(object[] values) => _values = values;

        public int Count => _values?.Length ?? 0;

        public string Word(int index) => _values[index] as string ?? "";

        public float Number(int index) => _values[index] is float number ? number : 0;

        public int Int(int index) => (int)Math.Round(Number(index));
    }

    /// <summary>Throw it from a cheat to reject the arguments with a message ("no item named 'medkt'").</summary>
    public sealed class CheatException : Exception
    {
        public CheatException(string message) : base(message) { }
    }

    public readonly struct CheatResult
    {
        private CheatResult(bool isOk, string message)
        {
            IsOk = isOk;
            Message = message;
        }

        public bool IsOk { get; }
        public string Message { get; }

        internal static CheatResult Done(string name) => new CheatResult(true, "ok");
        internal static CheatResult Info(string text) => new CheatResult(true, text);
        internal static CheatResult Failed(string message) => new CheatResult(false, message);

        public override string ToString() => Message;
    }
}
