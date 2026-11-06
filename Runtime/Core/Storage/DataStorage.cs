using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Documents by key. The domain decides what to save and when; the implementation decides where and how.
    /// </summary>
    public interface IDataStorage
    {
        bool Has(string key);
        bool TryLoad<T>(string key, out T data) where T : class;
        void Save<T>(string key, T data) where T : class;
        void Delete(string key);
    }

    /// <summary>
    /// Where the text of a document lives: a file, memory, a cloud slot.
    /// </summary>
    public interface ITextStore
    {
        bool Has(string key);
        bool TryRead(string key, out string text);
        void Write(string key, string text);
        void Delete(string key);
    }

    /// <summary>
    /// How a document becomes text and back. Read may throw on broken text.
    /// </summary>
    public interface ISerializer
    {
        string Write(object data);
        T Read<T>(string text) where T : class;
    }

    /// <summary>
    /// IDataStorage made of a text store and a serializer. Read and write errors are logged; a failed read returns false.
    /// </summary>
    public class DataStorage : IDataStorage
    {
        private readonly ITextStore _store;
        private readonly ISerializer _serializer;
        private readonly ILog _log;

        public DataStorage(ITextStore store, ISerializer serializer, ILog log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _log = log ?? NullLog.Instance;
        }

        public bool Has(string key) => _store.Has(key);

        public bool TryLoad<T>(string key, out T data) where T : class
        {
            data = null;
            if (!_store.TryRead(key, out var text))
                return false;

            try
            {
                data = _serializer.Read<T>(text);
                return data != null;
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to read '{key}' as {typeof(T).Name}", exception);
                return false;
            }
        }

        public void Save<T>(string key, T data) where T : class
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            try
            {
                _store.Write(key, _serializer.Write(data));
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to write '{key}'", exception);
            }
        }

        public void Delete(string key)
        {
            try
            {
                _store.Delete(key);
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to delete '{key}'", exception);
            }
        }
    }

    public sealed class InMemoryTextStore : ITextStore
    {
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Texts => _texts;

        public bool Has(string key) => _texts.ContainsKey(key);

        public bool TryRead(string key, out string text) => _texts.TryGetValue(key, out text);

        public void Write(string key, string text) => _texts[key] = text ?? throw new ArgumentNullException(nameof(text));

        public void Delete(string key) => _texts.Remove(key);
    }

    /// <summary>
    /// Storage in memory for tests and headless runs. Documents go through <see cref="JsonFieldSerializer"/>, so a
    /// loaded document is a copy and follows the same rules as JsonUtility in the game.
    /// </summary>
    public sealed class InMemoryDataStorage : DataStorage
    {
        public InMemoryDataStorage(ILog log = null) : this(new InMemoryTextStore(), log) { }

        private InMemoryDataStorage(InMemoryTextStore store, ILog log) : base(store, new JsonFieldSerializer(), log) =>
            Store = store;

        public InMemoryTextStore Store { get; }
    }
}
