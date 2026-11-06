using System;
using System.Collections.Generic;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// A handler of one type of content part: the code that makes a part do something, written by the feature whose
    /// state it changes (poison damage is Combat's, though the poison part sits in a buff).
    /// </summary>
    public interface IPartHandler
    {
        Type Part { get; }
    }

    /// <summary>
    /// Handlers by the exact type of the part they serve. One handler per part type. <see cref="Unhandled{TPart}"/>
    /// finds parts in the content that need a handler and have none, so a forgotten registration fails when the
    /// match is built instead of doing nothing in the game.
    /// </summary>
    public sealed class PartHandlers<THandler> where THandler : class, IPartHandler
    {
        private readonly Dictionary<Type, THandler> _handlers = new Dictionary<Type, THandler>();

        public IReadOnlyCollection<Type> Parts => _handlers.Keys;

        public PartHandlers<THandler> Register(THandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (handler.Part == null)
                throw new ArgumentException($"{handler.GetType().Name} names no part type", nameof(handler));
            if (_handlers.TryGetValue(handler.Part, out var existing))
                throw new InvalidOperationException(
                    $"{handler.Part.Name} already has a handler ({existing.GetType().Name}); {handler.GetType().Name} can't be the second one");

            _handlers.Add(handler.Part, handler);
            return this;
        }

        public bool Handles(Type part) => part != null && _handlers.ContainsKey(part);

        public bool TryFind(object part, out THandler handler)
        {
            handler = null;
            return part != null && _handlers.TryGetValue(part.GetType(), out handler);
        }

        public THandler Find(object part) => TryFind(part, out var handler) ? handler : null;

        /// <summary>
        /// Every part of type <typeparamref name="TPart"/> inside <paramref name="content"/> (public fields, lists,
        /// nested objects) that has no handler, as readable lines: <c>Poison.Effects[0]: no handler for DamageOverTime</c>.
        /// </summary>
        public IReadOnlyList<string> Unhandled<TPart>(object content, string path) where TPart : class =>
            ContentRefs.FindAll<TPart>(content, path)
                .Where(found => !Handles(found.Part.GetType()))
                .Select(found => $"{found.Path}: no handler for {found.Part.GetType().Name}")
                .ToList();
    }
}
