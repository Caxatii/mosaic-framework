using System.Collections.Generic;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class SettingsStoreTests
    {
        private enum Level { Easy, Normal, Hard }

        private sealed class TestSettings : ISettings<TestSettings>
        {
            public Level Level = Level.Normal;

            public TestSettings Copy() => (TestSettings)MemberwiseClone();

            public void Normalize()
            {
                if (!System.Enum.IsDefined(typeof(Level), Level))
                    Level = Level.Normal;
            }

            public bool Equals(TestSettings other) => other != null && Level == other.Level;
        }

        private InMemoryDataStorage _storage;
        private SettingsStore<TestSettings> _store;
        private List<Level> _previewed;
        private List<Level> _applied;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemoryDataStorage();
            _store = new SettingsStore<TestSettings>("test", _storage);
            _previewed = new List<Level>();
            _applied = new List<Level>();
            _store.Previewed += s => _previewed.Add(s.Level);
            _store.Applied += s => _applied.Add(s.Level);
        }

        [Test]
        public void Draft_changes_are_previewed_but_not_applied()
        {
            _store.Draft.Level = Level.Hard;
            _store.Preview();

            Assert.That(_store.IsDirty, Is.True);
            Assert.That(_store.Current.Level, Is.EqualTo(Level.Normal));
            Assert.That(_previewed, Is.EqualTo(new[] { Level.Hard }));
            Assert.That(_applied, Is.Empty);
        }

        [Test]
        public void Apply_saves_and_revert_returns_to_applied()
        {
            _store.Draft.Level = Level.Hard;
            _store.Apply();

            _store.Draft.Level = Level.Easy;
            _store.Revert();

            Assert.That(_store.Draft.Level, Is.EqualTo(Level.Hard));
            Assert.That(_store.IsDirty, Is.False);
            Assert.That(new SettingsStore<TestSettings>("test", _storage).Current.Level, Is.EqualTo(Level.Hard));
        }

        [Test]
        public void Trial_is_not_saved_until_kept_and_rolls_back()
        {
            _store.Draft.Level = Level.Hard;
            _store.ApplyTrial();

            Assert.That(_store.Current.Level, Is.EqualTo(Level.Hard));
            Assert.That(_storage.TryLoad<TestSettings>("test", out _), Is.False);

            _store.RollbackTrial();

            Assert.That(_store.Current.Level, Is.EqualTo(Level.Normal));
            Assert.That(_store.Draft.Level, Is.EqualTo(Level.Normal));
            Assert.That(_applied, Is.EqualTo(new[] { Level.Hard, Level.Normal }));
        }

        [Test]
        public void Invalid_values_are_normalized()
        {
            _store.Draft.Level = (Level)42;
            _store.Apply();

            Assert.That(_store.Current.Level, Is.EqualTo(Level.Normal));
        }
    }
}
