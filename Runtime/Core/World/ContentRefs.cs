using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Mosaic.Core
{
    /// <summary>
    /// Finds values inside content objects (public fields, nested objects and lists) together with readable paths,
    /// to validate references and parts when a match is built.
    /// </summary>
    public static class ContentRefs
    {
        private const int MaxDepth = 5;

        public static List<(string Path, T Ref)> Collect<T>(object root, string path) where T : struct
        {
            var result = new List<(string, T)>();
            Walk(root, path, 0, result);
            return result;
        }

        /// <summary>
        /// Every object of type <typeparamref name="T"/> (a class or an interface, such as a kind of part) inside the
        /// content, with its path. Objects of that type are not searched further.
        /// </summary>
        public static List<(string Path, T Part)> FindAll<T>(object root, string path) where T : class
        {
            var result = new List<(string, T)>();
            Walk(root, path, 0, result);
            return result;
        }

        private static void Walk<T>(object value, string path, int depth, List<(string, T)> result)
        {
            if (value == null || depth > MaxDepth)
                return;

            if (value is T found)
            {
                result.Add((path, found));
                return;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string)
                return;

            if (value is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                    Walk(item, $"{path}[{index++}]", depth + 1, result);
                return;
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                Walk(field.GetValue(value), $"{path}.{field.Name}", depth + 1, result);
        }
    }
}
