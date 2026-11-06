using System;

namespace Mosaic.Core
{
    public interface ILog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message, Exception exception = null);
    }

    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new NullLog();

        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception exception = null) { }
    }
}
