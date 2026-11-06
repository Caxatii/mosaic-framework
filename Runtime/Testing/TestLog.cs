using System;
using System.Collections.Generic;
using Mosaic.Core;

namespace Mosaic.Testing
{
    /// <summary>
    /// An ILog that keeps every message, so a test can check that nothing was warned or logged as an error.
    /// </summary>
    public sealed class TestLog : ILog
    {
        private readonly List<string> _infos = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Infos => _infos;
        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyList<string> Errors => _errors;

        public void Info(string message) => _infos.Add(message);

        public void Warning(string message) => _warnings.Add(message);

        public void Error(string message, Exception exception = null) =>
            _errors.Add(exception == null ? message : $"{message}: {exception}");

        public void Clear()
        {
            _infos.Clear();
            _warnings.Clear();
            _errors.Clear();
        }
    }
}
