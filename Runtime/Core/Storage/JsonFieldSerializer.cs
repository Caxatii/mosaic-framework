using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Mosaic.Core
{
    /// <summary>
    /// JSON of fields with the rules of Unity's JsonUtility, without Unity: public fields and fields marked
    /// [SerializeField]; [Serializable] nested classes and structs, arrays and lists; enums as numbers; a null nested
    /// object or array is written empty, a null string as "". Types JsonUtility can't store (dictionaries, interfaces,
    /// object) are skipped. Tests and servers use it to see the same save data the game sees.
    /// </summary>
    public sealed class JsonFieldSerializer : ISerializer
    {
        private const int MaxDepth = 10;
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public string Write(object data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var json = new StringBuilder();
            WriteObject(json, data, data.GetType(), 0);
            return json.ToString();
        }

        public T Read<T>(string text) where T : class => (T)Read(text, typeof(T));

        public object Read(string text, Type type)
        {
            var parser = new Parser(text ?? throw new ArgumentNullException(nameof(text)));
            var value = parser.ParseDocument();
            if (!(value is Dictionary<string, object> fields))
                throw new FormatException("The document is not a JSON object");

            return ReadObject(fields, type, 0);
        }

        private static IEnumerable<FieldInfo> FieldsOf(Type type)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (var field in current.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                    if (!field.IsNotSerialized && !field.IsInitOnly && !field.IsLiteral &&
                        (field.IsPublic || HasAttribute(field, "SerializeField")) && IsSupported(field.FieldType))
                        yield return field;
        }

        private static bool HasAttribute(FieldInfo field, string name)
        {
            foreach (var attribute in field.GetCustomAttributes(true))
                if (attribute.GetType().Name == name + "Attribute" || attribute.GetType().Name == name)
                    return true;
            return false;
        }

        private static bool IsSupported(Type type) =>
            IsLeaf(type) ||
            ElementOf(type) is { } element && !(ElementOf(element) is { }) && IsSupported(element) ||
            IsSerializableObject(type);

        private static bool IsLeaf(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string);

        private static bool IsSerializableObject(Type type) =>
            !type.IsAbstract && !type.IsInterface && !type.IsPrimitive && type != typeof(string) &&
            (type.IsValueType || type.GetConstructor(Type.EmptyTypes) != null) &&
            type.IsSerializable && ElementOf(type) == null && !typeof(IDictionary).IsAssignableFrom(type);

        private static Type ElementOf(Type type)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
                return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return type.GetGenericArguments()[0];
            return null;
        }

        private static void WriteObject(StringBuilder json, object value, Type type, int depth)
        {
            if (value == null)
                value = Activator.CreateInstance(type);

            json.Append('{');
            var first = true;
            if (depth < MaxDepth)
                foreach (var field in FieldsOf(type))
                {
                    if (!first)
                        json.Append(',');
                    first = false;
                    WriteString(json, field.Name);
                    json.Append(':');
                    WriteValue(json, field.GetValue(value), field.FieldType, depth + 1);
                }
            json.Append('}');
        }

        private static void WriteValue(StringBuilder json, object value, Type type, int depth)
        {
            if (type == typeof(string))
                WriteString(json, (string)value ?? "");
            else if (type == typeof(bool))
                json.Append((bool)value ? "true" : "false");
            else if (type.IsEnum)
                json.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            else if (type == typeof(float))
                json.Append(((float)value).ToString("R", CultureInfo.InvariantCulture));
            else if (type == typeof(double))
                json.Append(((double)value).ToString("R", CultureInfo.InvariantCulture));
            else if (type == typeof(char))
                json.Append(((int)(char)value).ToString(CultureInfo.InvariantCulture));
            else if (type.IsPrimitive)
                json.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            else if (ElementOf(type) is { } element)
            {
                json.Append('[');
                if (value is IList list)
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                            json.Append(',');
                        WriteValue(json, list[i], element, depth);
                    }
                json.Append(']');
            }
            else
                WriteObject(json, value, type, depth);
        }

        private static void WriteString(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (var c in text)
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            json.Append('"');
        }

        private static object ReadObject(Dictionary<string, object> fields, Type type, int depth)
        {
            var instance = Activator.CreateInstance(type, nonPublic: true);
            if (depth >= MaxDepth)
                return instance;

            foreach (var field in FieldsOf(type))
                if (fields.TryGetValue(field.Name, out var value))
                    field.SetValue(instance, ReadValue(value, field.FieldType, depth + 1));
            return instance;
        }

        private static object ReadValue(object value, Type type, int depth)
        {
            if (type == typeof(string))
                return value as string ?? "";

            if (type == typeof(bool))
                return value is bool flag && flag;

            if (type.IsEnum)
                return Enum.ToObject(type, value is Number number ? long.Parse(number.Text, CultureInfo.InvariantCulture) : 0L);

            if (type.IsPrimitive)
            {
                if (!(value is Number number))
                    return Activator.CreateInstance(type);
                if (type == typeof(char))
                    return (char)int.Parse(number.Text, CultureInfo.InvariantCulture);
                if (type == typeof(float) || type == typeof(double))
                    return Convert.ChangeType(double.Parse(number.Text, CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
                return Convert.ChangeType(number.Text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
                    ? (object)decimal.Parse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture)
                    : number.Text, type, CultureInfo.InvariantCulture);
            }

            if (ElementOf(type) is { } element)
            {
                var items = value as List<object> ?? new List<object>();
                if (type.IsArray)
                {
                    var array = Array.CreateInstance(element, items.Count);
                    for (var i = 0; i < items.Count; i++)
                        array.SetValue(ReadValue(items[i], element, depth), i);
                    return array;
                }

                var list = (IList)Activator.CreateInstance(type);
                foreach (var item in items)
                    list.Add(ReadValue(item, element, depth));
                return list;
            }

            return value is Dictionary<string, object> fields
                ? ReadObject(fields, type, depth)
                : Activator.CreateInstance(type, nonPublic: true);
        }

        private sealed class Number
        {
            public Number(string text) => Text = text;
            public string Text { get; }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _position;

            public Parser(string text) => _text = text;

            public object ParseDocument()
            {
                var value = ParseValue();
                SkipSpace();
                if (_position != _text.Length)
                    throw Error("Unexpected text after the document");
                return value;
            }

            private object ParseValue()
            {
                SkipSpace();
                if (_position >= _text.Length)
                    throw Error("Unexpected end");

                var c = _text[_position];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || char.IsDigit(c) || c == 'N' || c == 'I')
                            return ParseNumber();
                        throw Error($"Unexpected '{c}'");
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>();
                _position++;
                SkipSpace();
                if (Peek('}'))
                    return result;

                while (true)
                {
                    SkipSpace();
                    var name = ParseString();
                    SkipSpace();
                    Take(':');
                    result[name] = ParseValue();
                    SkipSpace();
                    if (Peek('}'))
                        return result;
                    Take(',');
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _position++;
                SkipSpace();
                if (Peek(']'))
                    return result;

                while (true)
                {
                    result.Add(ParseValue());
                    SkipSpace();
                    if (Peek(']'))
                        return result;
                    Take(',');
                }
            }

            private string ParseString()
            {
                Take('"');
                var result = new StringBuilder();
                while (true)
                {
                    if (_position >= _text.Length)
                        throw Error("Unterminated string");

                    var c = _text[_position++];
                    if (c == '"')
                        return result.ToString();
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }

                    var escaped = _text[_position++];
                    switch (escaped)
                    {
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'u':
                            result.Append((char)int.Parse(_text.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _position += 4;
                            break;
                        default: result.Append(escaped); break;
                    }
                }
            }

            private Number ParseNumber()
            {
                var start = _position;
                while (_position < _text.Length && "+-0123456789.eEaNIinfty".IndexOf(_text[_position]) >= 0)
                    _position++;
                return new Number(_text.Substring(start, _position - start));
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0)
                    throw Error($"Expected {word}");
                _position += word.Length;
            }

            private bool Peek(char c)
            {
                if (_position < _text.Length && _text[_position] == c)
                {
                    _position++;
                    return true;
                }

                return false;
            }

            private void Take(char c)
            {
                if (!Peek(c))
                    throw Error($"Expected '{c}'");
            }

            private void SkipSpace()
            {
                while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
                    _position++;
            }

            private FormatException Error(string message) => new FormatException($"{message} at {_position}");
        }
    }
}
