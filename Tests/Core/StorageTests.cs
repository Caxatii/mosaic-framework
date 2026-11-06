using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class StorageTests
    {
        public enum Mood { Calm, Angry }

        [Serializable]
        public sealed class Item
        {
            public int Id;
            public string Name;
        }

        [Serializable]
        public sealed class Document
        {
            public int Version = 2;
            public float Health = 0.1f;
            public ulong Seed;
            public Mood Mood;
            public string Title;
            public bool Flag;
            public Item Main;
            public Item[] Items;
            public List<int> Numbers = new List<int>();
            public Dictionary<string, int> Skipped = new Dictionary<string, int>();
            public readonly int Frozen = 1;

            public int Property { get; set; }
        }

        [Serializable] public sealed class SaveV0 { public int Gold; }
        [Serializable] public sealed class SaveV1 { public int Version = 1; public int Coins; }
        [Serializable] public sealed class SaveV2 { public int Version = 2; public int Coins; public int Gems; }

        private static SaveMigrations<SaveV2> Migrations() =>
            new SaveMigrations<SaveV2>(2)
                .Step<SaveV0, SaveV1>(0, old => new SaveV1 { Coins = old.Gold })
                .Step<SaveV1, SaveV2>(1, old => new SaveV2 { Coins = old.Coins, Gems = old.Coins / 10 });

        [Test]
        public void Serializer_follows_the_rules_of_JsonUtility()
        {
            var serializer = new JsonFieldSerializer();
            var document = new Document
            {
                Health = 0.3f, Seed = ulong.MaxValue, Mood = Mood.Angry, Flag = true, Property = 5,
                Items = new[] { new Item { Id = 1, Name = "a\"b\n" }, null }, Numbers = { 3, 4 },
            };
            document.Skipped["x"] = 1;

            var text = serializer.Write(document);
            var copy = serializer.Read<Document>(text);

            Assert.That(copy.Health, Is.EqualTo(0.3f));
            Assert.That(copy.Seed, Is.EqualTo(ulong.MaxValue));
            Assert.That(copy.Mood, Is.EqualTo(Mood.Angry));
            Assert.That(copy.Flag, Is.True);
            Assert.That(copy.Title, Is.EqualTo(""), "a null string comes back empty");
            Assert.That(copy.Main, Is.Not.Null, "a null nested object comes back as a default one");
            Assert.That(copy.Items[0].Name, Is.EqualTo("a\"b\n"));
            Assert.That(copy.Items[1], Is.Not.Null);
            Assert.That(copy.Numbers, Is.EqualTo(new[] { 3, 4 }));
            Assert.That(copy.Skipped, Is.Empty, "dictionaries are not stored");
            Assert.That(copy.Property, Is.EqualTo(0), "properties are not stored");
            Assert.That(text, Does.Not.Contain("Frozen"));
            Assert.That(text, Does.Contain("\"Mood\":1"));
        }

        [Test]
        public void Missing_fields_keep_their_initial_values()
        {
            var copy = new JsonFieldSerializer().Read<Document>("{\"Title\":\"t\",\"Unknown\":[1,{\"a\":null}]}");

            Assert.That(copy.Title, Is.EqualTo("t"));
            Assert.That(copy.Version, Is.EqualTo(2));
            Assert.That(copy.Health, Is.EqualTo(0.1f));
            Assert.Throws<FormatException>(() => new JsonFieldSerializer().Read<Document>("{\"Title\":"));
        }

        [Test]
        public void In_memory_storage_returns_a_copy()
        {
            var storage = new InMemoryDataStorage();
            var item = new Item { Id = 3 };
            storage.Save("item", item);
            item.Id = 4;

            Assert.That(storage.TryLoad<Item>("item", out var loaded), Is.True);
            Assert.That(loaded.Id, Is.EqualTo(3));
            Assert.That(loaded, Is.Not.SameAs(item));
            Assert.That(storage.Has("item"), Is.True);

            storage.Delete("item");
            Assert.That(storage.TryLoad<Item>("item", out _), Is.False);
        }

        [Test]
        public void Broken_document_is_logged_and_not_loaded()
        {
            var log = new ListLog();
            var store = new InMemoryTextStore();
            store.Write("item", "{oops");
            var storage = new DataStorage(store, new JsonFieldSerializer(), log);

            Assert.That(storage.TryLoad<Item>("item", out _), Is.False);
            Assert.That(log.Errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void File_store_keeps_the_previous_version_as_backup()
        {
            var directory = Path.Combine(Path.GetTempPath(), "mosaic-storage-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileTextStore(directory, "test_");
                store.Write("save", "one");
                store.Write("save", "two");

                Assert.That(File.ReadAllText(store.PathOf("save")), Is.EqualTo("two"));
                Assert.That(File.ReadAllText(store.PathOf("save") + ".bak"), Is.EqualTo("one"));
                Assert.That(File.Exists(store.PathOf("save") + ".tmp"), Is.False);

                File.Delete(store.PathOf("save"));
                Assert.That(store.Has("save"), Is.True);
                Assert.That(store.TryRead("save", out var text) ? text : null, Is.EqualTo("one"), "a lost document is read from its backup");

                store.Delete("save");
                Assert.That(store.Has("save"), Is.False);
                Assert.That(store.TryRead("save", out _), Is.False);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Old_documents_go_through_every_step()
        {
            var storage = new InMemoryDataStorage();
            storage.Save("save", new SaveV0 { Gold = 30 });

            var result = Migrations().Load(storage, "save");

            Assert.That(result.Status, Is.EqualTo(LoadStatus.Migrated));
            Assert.That(result.SavedVersion, Is.EqualTo(0));
            Assert.That(result.Data.Coins, Is.EqualTo(30));
            Assert.That(result.Data.Gems, Is.EqualTo(3));
            Assert.That(result.Data.Version, Is.EqualTo(2));
        }

        [Test]
        public void Current_missing_and_newer_documents()
        {
            var storage = new InMemoryDataStorage();
            var migrations = Migrations();

            Assert.That(migrations.Load(storage, "save").Status, Is.EqualTo(LoadStatus.Missing));

            storage.Save("save", new SaveV2 { Coins = 1, Gems = 2 });
            var current = migrations.Load(storage, "save");
            Assert.That(current.Status, Is.EqualTo(LoadStatus.Loaded));
            Assert.That(current.Data.Gems, Is.EqualTo(2));

            storage.Save("save", new SaveVersion { Version = 3 });
            var newer = migrations.Load(storage, "save");
            Assert.That(newer.Status, Is.EqualTo(LoadStatus.NewerVersion));
            Assert.That(newer.HasData, Is.False);
        }

        [Test]
        public void Gaps_and_failing_steps_are_reported()
        {
            var storage = new InMemoryDataStorage();
            storage.Save("save", new SaveV0 { Gold = 1 });

            var gap = new SaveMigrations<SaveV2>(2).Step<SaveV1, SaveV2>(1, old => new SaveV2()).Load(storage, "save");
            Assert.That(gap.Status, Is.EqualTo(LoadStatus.Failed));
            Assert.That(gap.Problem, Does.Contain("v0"));

            var failing = new SaveMigrations<SaveV2>(2)
                .Step<SaveV0, SaveV1>(0, _ => throw new InvalidOperationException("boom"))
                .Step<SaveV1, SaveV2>(1, old => new SaveV2())
                .Load(storage, "save");
            Assert.That(failing.Status, Is.EqualTo(LoadStatus.Failed));
            Assert.That(failing.Problem, Does.Contain("boom"));
        }

        [Test]
        public void Steps_that_do_not_fit_together_are_refused()
        {
            Assert.Throws<ArgumentException>(() => new SaveMigrations<SaveV2>(2).Step<SaveV1, SaveV1>(1, old => old));
            Assert.Throws<ArgumentException>(() => new SaveMigrations<SaveV2>(2)
                .Step<SaveV1, SaveV2>(1, _ => new SaveV2())
                .Step<SaveV0, SaveV0>(0, old => old));
            Assert.Throws<ArgumentException>(() => Migrations().Step<SaveV0, SaveV1>(0, _ => new SaveV1()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveMigrations<SaveV2>(2).Step<SaveV2, SaveV2>(2, old => old));
        }

        private sealed class ListLog : ILog
        {
            public readonly List<string> Errors = new List<string>();
            public void Info(string message) { }
            public void Warning(string message) { }
            public void Error(string message, Exception exception = null) => Errors.Add(message);
        }
    }
}
