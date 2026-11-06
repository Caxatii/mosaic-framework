using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mosaic.Ui
{
    public sealed class ViewPool : IDisposable
    {
        private readonly Dictionary<Component, Stack<Component>> _free = new Dictionary<Component, Stack<Component>>();
        private readonly Dictionary<Component, Component> _prefabOf = new Dictionary<Component, Component>();

        public T Get<T>(T prefab) where T : Component
        {
            if (!_free.TryGetValue(prefab, out var free))
                _free.Add(prefab, free = new Stack<Component>());

            if (free.Count > 0)
            {
                var reused = (T)free.Pop();
                reused.gameObject.SetActive(true);
                return reused;
            }

            var created = UnityEngine.Object.Instantiate(prefab);
            _prefabOf.Add(created, prefab);
            return created;
        }

        public void Release(Component instance)
        {
            instance.gameObject.SetActive(false);
            _free[_prefabOf[instance]].Push(instance);
        }

        public void Dispose()
        {
            foreach (var instance in _prefabOf.Keys)
                if (instance != null)
                    UnityEngine.Object.Destroy(instance.gameObject);

            _prefabOf.Clear();
            _free.Clear();
        }
    }
}
