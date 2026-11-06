using System;
using System.Collections.Generic;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// The version field every versioned document has at its root: <c>public int Version = 3;</c>.
    /// A document saved before it had the field reads as version 0.
    /// </summary>
    [Serializable]
    public sealed class SaveVersion
    {
        public int Version;
    }

    public enum LoadStatus
    {
        /// <summary>Nothing is saved under the key.</summary>
        Missing,
        /// <summary>The document was in the current version.</summary>
        Loaded,
        /// <summary>The document was older and went through the migration steps.</summary>
        Migrated,
        /// <summary>The document was saved by a newer build; do not overwrite it.</summary>
        NewerVersion,
        /// <summary>The document is broken or no step migrates its version.</summary>
        Failed,
    }

    public readonly struct LoadResult<T> where T : class
    {
        public LoadResult(LoadStatus status, T data, int savedVersion, string problem = null)
        {
            Status = status;
            Data = data;
            SavedVersion = savedVersion;
            Problem = problem;
        }

        public LoadStatus Status { get; }

        /// <summary>The document in the current version; null unless Loaded or Migrated.</summary>
        public T Data { get; }

        public int SavedVersion { get; }
        public string Problem { get; }

        public bool HasData => Data != null;

        public override string ToString() =>
            Problem == null ? $"{Status} (saved v{SavedVersion})" : $"{Status} (saved v{SavedVersion}): {Problem}";
    }

    /// <summary>
    /// How older versions of a document become the current one: a chain of steps, each turning one version into the
    /// next. Old formats are kept as their own frozen classes (ProfileV1); a step is a plain function, so it is tested
    /// without Unity. Adding a field needs no step (it gets its default); renaming one or changing its meaning does.
    /// </summary>
    public sealed class SaveMigrations<T> where T : class
    {
        private readonly SortedDictionary<int, Migration> _steps = new SortedDictionary<int, Migration>();

        public SaveMigrations(int current)
        {
            if (current < 0)
                throw new ArgumentOutOfRangeException(nameof(current));

            Current = current;
        }

        public int Current { get; }

        /// <summary>Saved versions that can be loaded: the oldest step's version up to Current.</summary>
        public int Oldest => _steps.Count == 0 ? Current : _steps.Keys.First();

        /// <summary>
        /// Turns version <paramref name="from"/> (read as <typeparamref name="TFrom"/>) into version from + 1.
        /// The last step must produce <typeparamref name="T"/> and neighbouring steps must agree on their types; both
        /// are checked here. A gap in the chain is reported when a document of that version is loaded.
        /// </summary>
        public SaveMigrations<T> Step<TFrom, TTo>(int from, Func<TFrom, TTo> migrate)
            where TFrom : class
            where TTo : class
        {
            if (migrate == null)
                throw new ArgumentNullException(nameof(migrate));
            if (from < 0 || from >= Current)
                throw new ArgumentOutOfRangeException(nameof(from), $"A step migrates a version below the current {Current}");
            if (_steps.ContainsKey(from))
                throw new ArgumentException($"A step from version {from} is already registered", nameof(from));
            if (from == Current - 1 && typeof(TTo) != typeof(T))
                throw new ArgumentException($"The step from {from} must produce {typeof(T).Name}, not {typeof(TTo).Name}");
            if (_steps.TryGetValue(from + 1, out var next) && next.From != typeof(TTo))
                throw new ArgumentException($"The step from {from} produces {typeof(TTo).Name}, but the step from {from + 1} reads {next.From.Name}");
            if (_steps.TryGetValue(from - 1, out var previous) && previous.To != typeof(TFrom))
                throw new ArgumentException($"The step from {from} reads {typeof(TFrom).Name}, but the step from {from - 1} produces {previous.To.Name}");

            _steps.Add(from, new Migration(typeof(TFrom), typeof(TTo),
                (storage, key) => storage.TryLoad<TFrom>(key, out var data) ? data : null,
                data => migrate((TFrom)data)));
            return this;
        }

        public LoadResult<T> Load(IDataStorage storage, string key)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            if (!storage.Has(key))
                return new LoadResult<T>(LoadStatus.Missing, null, 0);

            if (!storage.TryLoad<SaveVersion>(key, out var header))
                return new LoadResult<T>(LoadStatus.Failed, null, 0, "the document can't be read");

            var version = header.Version;
            if (version > Current)
                return new LoadResult<T>(LoadStatus.NewerVersion, null, version, $"saved by a newer build (v{version}, this build reads up to v{Current})");

            if (version == Current)
                return storage.TryLoad<T>(key, out var current)
                    ? new LoadResult<T>(LoadStatus.Loaded, current, version)
                    : new LoadResult<T>(LoadStatus.Failed, null, version, $"can't be read as {typeof(T).Name}");

            if (!_steps.TryGetValue(version, out var first))
                return new LoadResult<T>(LoadStatus.Failed, null, version, $"no migration from v{version} (oldest supported is v{Oldest})");

            var data = first.Read(storage, key);
            if (data == null)
                return new LoadResult<T>(LoadStatus.Failed, null, version, $"can't be read as {first.From.Name}");

            for (var step = version; step < Current; step++)
            {
                if (!_steps.TryGetValue(step, out var migration))
                    return new LoadResult<T>(LoadStatus.Failed, null, version, $"no migration from v{step}");

                try
                {
                    data = migration.Migrate(data);
                }
                catch (Exception exception)
                {
                    return new LoadResult<T>(LoadStatus.Failed, null, version, $"the step from v{step} failed: {exception.Message}");
                }

                if (data == null)
                    return new LoadResult<T>(LoadStatus.Failed, null, version, $"the step from v{step} returned null");
            }

            return new LoadResult<T>(LoadStatus.Migrated, (T)data, version);
        }

        private sealed class Migration
        {
            public Migration(Type from, Type to, Func<IDataStorage, string, object> read, Func<object, object> migrate)
            {
                From = from;
                To = to;
                Read = read;
                Migrate = migrate;
            }

            public Type From { get; }
            public Type To { get; }
            public Func<IDataStorage, string, object> Read { get; }
            public Func<object, object> Migrate { get; }
        }
    }
}
