using System;
using System.Collections.Generic;
using Mosaic.Core;

namespace Mosaic.Ui
{
    /// <summary>
    /// Worlds visible in Window > Mosaic > Simulation. Registering enables nothing by itself.
    /// </summary>
    public static class DebugWorlds
    {
        private static readonly List<Entry> Items = new List<Entry>();

        public static event Action Changed;

        public static IReadOnlyList<Entry> All => Items;

        public static IDisposable Register(string name, GameplayContext context, Func<int, string> describeKind = null)
        {
            var entry = new Entry(name, context ?? throw new ArgumentNullException(nameof(context)), describeKind);
            Items.Add(entry);
            Changed?.Invoke();
            return new Registration(entry);
        }

        public sealed class Entry
        {
            internal Entry(string name, GameplayContext context, Func<int, string> describeKind)
            {
                Name = name;
                Context = context;
                DescribeKind = describeKind ?? (kind => $"#{kind}");
            }

            public string Name { get; }
            public GameplayContext Context { get; }
            public Func<int, string> DescribeKind { get; }
        }

        private sealed class Registration : IDisposable
        {
            private Entry _entry;

            public Registration(Entry entry) => _entry = entry;

            public void Dispose()
            {
                if (_entry == null)
                    return;

                Items.Remove(_entry);
                _entry = null;
                Changed?.Invoke();
            }
        }
    }
}
