using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    public interface ISettings<T> : IEquatable<T> where T : ISettings<T>
    {
        T Copy();
        void Normalize();
    }

    public interface ISettingsStore
    {
        bool IsDirty { get; }
        bool IsOnTrial { get; }

        void Apply();
        void ApplyTrial();
        void KeepTrial();
        void RollbackTrial();
        void Revert();
        void ResetToDefaults();
    }

    public sealed class SettingsStore<T> : ISettingsStore where T : class, ISettings<T>, new()
    {
        private readonly string _key;
        private readonly IDataStorage _storage;
        private T _beforeTrial;

        public SettingsStore(string key, IDataStorage storage)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));

            Current = storage.TryLoad<T>(key, out var saved) ? saved : new T();
            Current.Normalize();
            Draft = Current.Copy();
        }

        public T Current { get; private set; }

        public T Draft { get; private set; }

        public bool IsDirty => !Draft.Equals(Current);
        public bool IsOnTrial => _beforeTrial != null;

        public event Action<T> Previewed;
        public event Action<T> Applied;

        public void Preview()
        {
            Draft.Normalize();
            Previewed?.Invoke(Draft);
        }

        public void Apply()
        {
            _beforeTrial = null;
            Commit();
            _storage.Save(_key, Current);
        }

        public void ApplyTrial()
        {
            _beforeTrial ??= Current;
            Commit();
        }

        public void KeepTrial()
        {
            if (!IsOnTrial)
                return;

            _beforeTrial = null;
            _storage.Save(_key, Current);
        }

        public void RollbackTrial()
        {
            if (!IsOnTrial)
                return;

            Current = _beforeTrial;
            _beforeTrial = null;
            Draft = Current.Copy();
            Applied?.Invoke(Current);
            Previewed?.Invoke(Draft);
        }

        public void Revert()
        {
            Draft = Current.Copy();
            Previewed?.Invoke(Draft);
        }

        public void ResetToDefaults()
        {
            Draft = new T();
            Preview();
        }

        private void Commit()
        {
            Draft.Normalize();
            Current = Draft.Copy();
            Applied?.Invoke(Current);
        }
    }
}
