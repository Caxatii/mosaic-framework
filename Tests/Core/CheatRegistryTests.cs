using System;
using System.Linq;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class CheatRegistryTests
    {
        private enum GiveError { Unknown }

        private readonly struct Give : ICommand
        {
            public Give(string item, int count) { Item = item; Count = count; }
            public string Item { get; }
            public int Count { get; }
        }

        private readonly struct Speed : ICommand
        {
            public Speed(float scale) => Scale = scale;
            public float Scale { get; }
        }

        private sealed class Handler : CommandHandler
        {
            public string Given;
            public float Scale;

            public Handler(GameplayContext context) : base(context)
            {
                On<Give>(command =>
                {
                    if (command.Item == "banana")
                        return CommandResult.Rejected(GiveError.Unknown);
                    Given = $"{command.Item} x{command.Count}";
                    return CommandResult.Ok;
                });
                On<Speed>(command =>
                {
                    Scale = command.Scale;
                    return CommandResult.Ok;
                });
            }
        }

        private GameplayContext _context;
        private Handler _handler;
        private CheatRegistry _cheats;

        [SetUp]
        public void SetUp()
        {
            _context = new GameplayContext(throwOnSubscriberException: true);
            _handler = new Handler(_context);
            _cheats = new CheatRegistry(() => _context.Commands);
            _cheats.Add("give", "give an item", args => new Give(args.Word(0), args.Int(1)))
                .Word("item", () => new[] { "medkit", "rifle", "rocket" })
                .Number("count", 1, 99, @default: 1, whole: true);
            _cheats.Add("speed", "game speed", args => new Speed(args.Number(0)))
                .Number("scale", 0.1f, 4, @default: 1);
            _cheats.Add("explode", "", _ => throw new CheatException("nothing to explode"));
        }

        [Test]
        public void Lines_are_parsed_with_optional_arguments_and_defaults()
        {
            Assert.That(_cheats.Execute("give medkit").IsOk, Is.True);
            Assert.That(_handler.Given, Is.EqualTo("medkit x1"));

            Assert.That(_cheats.Execute("GIVE rifle 3").IsOk, Is.True);
            Assert.That(_handler.Given, Is.EqualTo("rifle x3"));

            Assert.That(_cheats.Execute("speed 0.5").IsOk, Is.True);
            Assert.That(_handler.Scale, Is.EqualTo(0.5f));
        }

        [Test]
        public void Problems_come_back_as_messages()
        {
            Assert.That(_cheats.Execute("fly").Message, Does.Contain("Unknown cheat"));
            Assert.That(_cheats.Execute("give").Message, Does.Contain("Missing item"));
            Assert.That(_cheats.Execute("give medkit lots").Message, Does.Contain("not a number"));
            Assert.That(_cheats.Execute("speed 10").Message, Does.Contain("outside"));
            Assert.That(_cheats.Execute("give medkit 1 2").Message, Does.Contain("Too many"));
            Assert.That(_cheats.Execute("give banana").Message, Is.EqualTo("Rejected: Unknown"));
            Assert.That(_cheats.Execute("explode").Message, Is.EqualTo("nothing to explode"));
            Assert.That(new CheatRegistry(() => null).Execute("x").IsOk, Is.False);
        }

        [Test]
        public void Help_shows_usage_and_descriptions()
        {
            var help = _cheats.Execute("help").Message;

            Assert.That(help, Does.Contain("give <item> [count] — give an item"));
            Assert.That(help, Does.Contain("speed [scale] — game speed"));
        }

        [Test]
        public void Completion_covers_names_and_word_choices()
        {
            Assert.That(_cheats.Complete("sp"), Is.EqualTo(new[] { "speed" }));
            Assert.That(_cheats.Complete("give r"), Is.EqualTo(new[] { "rifle", "rocket" }));
            Assert.That(_cheats.Complete("give "), Has.Count.EqualTo(3));
            Assert.That(_cheats.Complete("speed "), Is.Empty, "numbers have no choices");
        }

        [Test]
        public void Window_gets_cheats_without_words_and_clamps_numbers()
        {
            Assert.That(_cheats.ForWindow.Select(c => c.Name), Is.EqualTo(new[] { "speed", "explode" }));

            var speed = _cheats.Find("speed");
            Assert.That(speed.Parameters[0].HasRange, Is.True);
            Assert.That(_cheats.Execute(speed, new[] { 9f }).IsOk, Is.True);
            Assert.That(_handler.Scale, Is.EqualTo(4));
            Assert.Throws<ArgumentException>(() => _cheats.Execute(_cheats.Find("give"), new float[0]));
        }

        [Test]
        public void Names_are_unique_words()
        {
            Assert.Throws<ArgumentException>(() => _cheats.Add("give", "", _ => null));
            Assert.Throws<ArgumentException>(() => _cheats.Add("two words", "", _ => null));
            Assert.Throws<ArgumentException>(() => _cheats.Add("help", "", _ => null));
        }
    }
}
