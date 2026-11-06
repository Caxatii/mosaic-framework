using System;
using Mosaic.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mosaic.Infrastructure
{
    /// <summary>
    /// ILoadOperation adapters for Unity's asynchronous loading: scenes and Resources.
    /// </summary>
    public static class UnityLoadOperation
    {
        /// <summary>
        /// Loads a scene. Unity reports 0.9 until the scene activates; the progress here goes to 1.
        /// With <paramref name="unloadOnDispose"/> an additive scene is unloaded when the operation is disposed.
        /// </summary>
        public static ILoadOperation<Scene> LoadScene(string name, LoadSceneMode mode = LoadSceneMode.Single, bool unloadOnDispose = false)
        {
            var operation = SceneManager.LoadSceneAsync(name, mode);
            if (operation == null)
                return LoadOperation.Failed<Scene>(new ArgumentException($"Scene '{name}' is not in the build settings", nameof(name)));

            return new AsyncOperationAdapter<Scene>(operation, () => FindScene(name),
                unload: unloadOnDispose && mode == LoadSceneMode.Additive ? scene => SceneManager.UnloadSceneAsync(scene) : (Action<Scene>)null,
                fullProgressAt: 0.9f);
        }

        /// <summary>Loads an asset from a Resources folder; fails if there is none at the path.</summary>
        public static ILoadOperation<T> LoadResource<T>(string path) where T : UnityEngine.Object
        {
            var request = Resources.LoadAsync<T>(path);
            return new AsyncOperationAdapter<T>(request, () =>
                request.asset is T asset ? asset : throw new ArgumentException($"No {typeof(T).Name} at Resources/{path}", nameof(path)));
        }

        /// <summary>Wraps any AsyncOperation; <paramref name="result"/> is read once it is done.</summary>
        public static ILoadOperation<T> From<T>(AsyncOperation operation, Func<T> result) =>
            new AsyncOperationAdapter<T>(operation ?? throw new ArgumentNullException(nameof(operation)), result ?? throw new ArgumentNullException(nameof(result)));

        private static Scene FindScene(string nameOrPath)
        {
            var scene = SceneManager.GetSceneByPath(nameOrPath);
            if (!scene.IsValid())
                scene = SceneManager.GetSceneByName(nameOrPath);
            return scene.IsValid() ? scene : throw new InvalidOperationException($"Scene '{nameOrPath}' was loaded but is not found");
        }

        private sealed class AsyncOperationAdapter<T> : ILoadOperation<T>
        {
            private readonly AsyncOperation _operation;
            private readonly Func<T> _readResult;
            private readonly Action<T> _unload;
            private readonly float _fullProgressAt;
            private bool _hasResult;
            private T _result;
            private Exception _error;
            private bool _disposed;

            public AsyncOperationAdapter(AsyncOperation operation, Func<T> readResult, Action<T> unload = null, float fullProgressAt = 1)
            {
                _operation = operation;
                _readResult = readResult;
                _unload = unload;
                _fullProgressAt = fullProgressAt;
            }

            public float Progress => IsDone ? 1 : Mathf.Clamp01(_operation.progress / _fullProgressAt);

            public bool IsDone
            {
                get
                {
                    if (!_operation.isDone)
                        return false;

                    ReadResult();
                    return true;
                }
            }

            public Exception Error => IsDone ? _error : null;

            public T Result => IsDone && _error == null
                ? _result
                : throw new InvalidOperationException(_error != null ? "The operation failed: " + _error.Message : "The operation is not done yet", _error);

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_unload != null && _operation.isDone && _error == null)
                {
                    ReadResult();
                    _unload(_result);
                }
            }

            private void ReadResult()
            {
                if (_hasResult)
                    return;

                _hasResult = true;
                try
                {
                    _result = _readResult();
                }
                catch (Exception exception)
                {
                    _error = exception;
                }
            }
        }
    }
}
