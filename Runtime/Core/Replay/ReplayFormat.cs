using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Mosaic.Core
{
    /// <summary>
    /// Lossless line-based text format for CommandRecording.
    /// </summary>
    public static class ReplayFormat
    {
        private const string Signature = "mosaic-replay 1";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Write(CommandRecording recording)
        {
            var text = new StringBuilder();
            text.Append(Signature).Append('\n');

            foreach (var pair in recording.Header)
                text.Append("h ").Append(Escape(pair.Key)).Append('=').Append(Escape(pair.Value)).Append('\n');

            foreach (var entry in recording.Entries)
            {
                if (entry.IsTick)
                {
                    text.Append("t ").Append(FloatBits(entry.DeltaTime)).Append('\n');
                    continue;
                }

                var type = entry.Command.GetType();
                text.Append("c ").Append(TypeName(type)).Append('|');
                var values = new List<string>();
                Encode(entry.Command, type, "", values);
                text.Append(string.Join(";", values)).Append('|').Append(Escape(entry.Result)).Append('\n');
            }

            return text.ToString();
        }

        public static CommandRecording Read(string text)
        {
            var recording = new CommandRecording();
            using var reader = new StringReader(text ?? throw new ArgumentNullException(nameof(text)));

            if (reader.ReadLine() != Signature)
                throw new FormatException("Not a Mosaic recording (missing header '" + Signature + "')");

            string line;
            var number = 1;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                if (line.Length == 0)
                    continue;

                try
                {
                    switch (line[0])
                    {
                        case 'h':
                            var separator = line.IndexOf('=');
                            recording.Header[Unescape(line.Substring(2, separator - 2))] = Unescape(line.Substring(separator + 1));
                            break;
                        case 't':
                            recording.Add(CommandRecording.Entry.ForTick(ParseFloat(line.Substring(2))));
                            break;
                        case 'c':
                            var parts = line.Substring(2).Split('|');
                            var type = FindType(parts[0]);
                            var values = ParseValues(parts[1]);
                            var command = (ICommand)Decode(type, "", values);
                            recording.Add(CommandRecording.Entry.ForCommand(command, Unescape(parts[2])));
                            break;
                        default:
                            throw new FormatException($"unknown entry '{line[0]}'");
                    }
                }
                catch (Exception exception) when (!(exception is FormatException))
                {
                    throw new FormatException($"Line {number}: {exception.Message}", exception);
                }
            }

            return recording;
        }

        public static bool CanWrite(Type commandType, out string problem)
        {
            try
            {
                Encode(Activator.CreateInstance(commandType), commandType, "", new List<string>());
                problem = null;
                return true;
            }
            catch (NotSupportedException exception)
            {
                problem = exception.Message;
                return false;
            }
        }

        private static void Encode(object value, Type type, string path, List<string> values)
        {
            if (IsLeaf(type))
            {
                values.Add(path.TrimEnd('.') + "=" + EncodeLeaf(value, type));
                return;
            }

            if (!type.IsValueType)
                throw new NotSupportedException($"{path.TrimEnd('.')}: a field of type {type.Name} cannot be recorded (only primitives, enums, strings and structs of them)");

            foreach (var field in type.GetFields(Fields))
                Encode(field.GetValue(value), field.FieldType, path + field.Name + ".", values);
        }

        private static object Decode(Type type, string path, Dictionary<string, string> values)
        {
            if (IsLeaf(type))
                return values.TryGetValue(path.TrimEnd('.'), out var text) ? DecodeLeaf(text, type) : Default(type);

            var instance = Activator.CreateInstance(type);
            foreach (var field in type.GetFields(Fields))
                field.SetValue(instance, Decode(field.FieldType, path + field.Name + ".", values));
            return instance;
        }

        private static bool IsLeaf(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);

        private static object Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        private static string EncodeLeaf(object value, Type type)
        {
            if (type == typeof(float))
                return FloatBits((float)value);
            if (type == typeof(double))
                return BitConverter.DoubleToInt64Bits((double)value).ToString("x16");
            if (type == typeof(string))
                return value == null ? "\\0" : Escape((string)value);
            if (type.IsEnum)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static object DecodeLeaf(string text, Type type)
        {
            if (type == typeof(float))
                return ParseFloat(text);
            if (type == typeof(double))
                return BitConverter.Int64BitsToDouble(long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            if (type == typeof(string))
                return text == "\\0" ? null : Unescape(text);
            if (type.IsEnum)
                return Enum.ToObject(type, long.Parse(text, CultureInfo.InvariantCulture));
            return Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
        }

        private static string FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8");

        private static float ParseFloat(string text) =>
            BitConverter.ToSingle(BitConverter.GetBytes(int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture)), 0);

        private static Dictionary<string, string> ParseValues(string text)
        {
            var result = new Dictionary<string, string>();
            if (text.Length == 0)
                return result;

            foreach (var pair in text.Split(';'))
            {
                var separator = pair.IndexOf('=');
                result[pair.Substring(0, separator)] = pair.Substring(separator + 1);
            }

            return result;
        }

        private static string TypeName(Type type) => type.FullName + ", " + type.Assembly.GetName().Name;

        private static Type FindType(string name)
        {
            var type = Type.GetType(name);
            if (type != null)
                return type;

            var fullName = name.Split(',')[0].Trim();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType(fullName) is { } found)
                    return found;

            throw new FormatException($"Command type {name} not found");
        }

        private static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("|", "\\p").Replace(";", "\\s").Replace("=", "\\e");

        private static string Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\' || i + 1 >= text.Length)
                {
                    result.Append(text[i]);
                    continue;
                }

                switch (text[++i])
                {
                    case 'n': result.Append('\n'); break;
                    case 'p': result.Append('|'); break;
                    case 's': result.Append(';'); break;
                    case 'e': result.Append('='); break;
                    default: result.Append(text[i]); break;
                }
            }

            return result.ToString();
        }
    }
}
