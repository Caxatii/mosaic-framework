using System.Collections.Generic;

namespace Mosaic.Core
{
    public interface IDataStorage
    {
        bool TryLoad<T>(string key, out T data) where T : class;
        void Save<T>(string key, T data) where T : class;
        void Delete(string key);
    }

    public sealed class InMemoryDataStorage : IDataStorage
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public bool TryLoad<T>(string key, out T data) where T : class
        {
            data = _data.TryGetValue(key, out var value) ? value as T : null;
            return data != null;
        }

        public void Save<T>(string key, T data) where T : class => _data[key] = data;

        public void Delete(string key) => _data.Remove(key);
    }
}
