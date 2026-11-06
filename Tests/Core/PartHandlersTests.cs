using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Mosaic.Core.Tests
{
    public sealed class PartHandlersTests
    {
        private interface IEffectPart { }

        private sealed class Burn : IEffectPart { public float Damage = 1; }
        private sealed class Freeze : IEffectPart { }
        private sealed class Speed { public float Multiplier = 2; }

        private sealed class Spell
        {
            public string Name = "Fireball";
            public List<object> Parts = new List<object>();
        }

        private sealed class Handler<TPart> : IPartHandler
        {
            public Type Part => typeof(TPart);
        }

        [Test]
        public void Handlers_are_found_by_the_exact_part_type()
        {
            var burn = new Handler<Burn>();
            var handlers = new PartHandlers<Handler<Burn>>().Register(burn);

            Assert.That(handlers.Find(new Burn()), Is.SameAs(burn));
            Assert.That(handlers.TryFind(new Freeze(), out _), Is.False);
            Assert.That(handlers.Find(null), Is.Null);
            Assert.That(handlers.Handles(typeof(Burn)), Is.True);
            Assert.Throws<InvalidOperationException>(() => handlers.Register(new Handler<Burn>()));
        }

        [Test]
        public void Unhandled_lists_parts_that_need_a_handler_and_have_none()
        {
            var handlers = new PartHandlers<IPartHandler>().Register(new Handler<Burn>());
            var spell = new Spell { Parts = { new Burn(), new Speed(), new Freeze() } };

            var missing = handlers.Unhandled<IEffectPart>(spell, "Fireball");

            Assert.That(missing, Is.EqualTo(new[] { "Fireball.Parts[2]: no handler for Freeze" }));
        }
    }
}
