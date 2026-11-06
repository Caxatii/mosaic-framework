using System;
using System.IO;

namespace Mosaic.Core
{
    /// <summary>
    /// Text documents as files. A write goes to a temporary file first and then replaces the old one, which is kept
    /// as .bak, so a crash in the middle of saving never leaves a half-written document. If the document is missing
    /// but its backup exists, the backup is read.
    /// </summary>
    public sealed class FileTextStore : ITextStore
    {
        private readonly string _directory;
        private readonly string _prefix;

        public FileTextStore(string directory, string prefix = "")
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _prefix = prefix ?? "";
        }

        public string PathOf(string key) => Path.Combine(_directory, $"{_prefix}{key}.json");

        public bool Has(string key)
        {
            var path = PathOf(key);
            return File.Exists(path) || File.Exists(path + ".bak");
        }

        public bool TryRead(string key, out string text)
        {
            var path = PathOf(key);
            if (!File.Exists(path))
                path += ".bak";

            text = File.Exists(path) ? File.ReadAllText(path) : null;
            return text != null;
        }

        public void Write(string key, string text)
        {
            var path = PathOf(key);
            var temporary = path + ".tmp";
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temporary, text);

            if (!File.Exists(path))
            {
                File.Move(temporary, path);
                return;
            }

            try
            {
                File.Replace(temporary, path, path + ".bak");
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(path, path + ".bak", overwrite: true);
                File.Copy(temporary, path, overwrite: true);
                File.Delete(temporary);
            }
        }

        public void Delete(string key)
        {
            var path = PathOf(key);
            foreach (var file in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }
}
