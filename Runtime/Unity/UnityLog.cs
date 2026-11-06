using System;
using Mosaic.Core;
using UnityEngine;

namespace Mosaic.Infrastructure
{
    public sealed class UnityLog : ILog
    {
        public void Info(string message) => Debug.Log($"[Mosaic] {message}");

        public void Warning(string message) => Debug.LogWarning($"[Mosaic] {message}");

        public void Error(string message, Exception exception = null)
        {
            Debug.LogError($"[Mosaic] {message}");
            if (exception != null)
                Debug.LogException(exception);
        }
    }
}
