using System;
using System.IO;
using Mosaic.Core;
using UnityEngine;

namespace Mosaic.Infrastructure
{
    public sealed class FileDataStorage : IDataStorage
    {
        private readonly ILog _log;
        private readonly string _prefix;

        public FileDataStorage(ILog log, string prefix = "mosaic_")
        {
            _log = log;
            _prefix = prefix;
        }

        public bool TryLoad<T>(string key, out T data) where T : class
        {
            data = null;
            var path = PathOf(key);
            if (!File.Exists(path))
                return false;

            try
            {
                data = JsonUtility.FromJson<T>(File.ReadAllText(path));
                return data != null;
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to read {path}", exception);
                return false;
            }
        }

        public void Save<T>(string key, T data) where T : class
        {
            var path = PathOf(key);
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(data, prettyPrint: true));
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to write {path}", exception);
            }
        }

        public void Delete(string key)
        {
            var path = PathOf(key);
            if (File.Exists(path))
                File.Delete(path);
        }

        private string PathOf(string key) => Path.Combine(Application.persistentDataPath, $"{_prefix}{key}.json");
    }
}
