using Mosaic.Core;
using UnityEngine;

namespace Mosaic.Infrastructure
{
    /// <summary>
    /// Documents as JSON files in Application.persistentDataPath ({prefix}{key}.json), written with JsonUtility.
    /// </summary>
    public sealed class FileDataStorage : DataStorage
    {
        public FileDataStorage(ILog log, string prefix = "mosaic_")
            : base(new FileTextStore(Application.persistentDataPath, prefix), new JsonUtilitySerializer(), log)
        {
        }
    }

    /// <summary>
    /// ISerializer on top of JsonUtility: [Serializable] classes with public fields, no null nested objects.
    /// </summary>
    public sealed class JsonUtilitySerializer : ISerializer
    {
        private readonly bool _prettyPrint;

        public JsonUtilitySerializer(bool prettyPrint = true) => _prettyPrint = prettyPrint;

        public string Write(object data) => JsonUtility.ToJson(data, _prettyPrint);

        public T Read<T>(string text) where T : class => JsonUtility.FromJson<T>(text);
    }
}
