using System;

namespace Mosaic.Core
{
    public interface IEntityCommand : ICommand
    {
        EntityId Entity { get; }
    }

    public enum CommandError
    {
        EntityNotFound,
    }

    /// <summary>
    /// Validates commands of a feature and calls its Operations. Has no access to the event bus:
    /// facts are published by the Operations that made the change.
    /// </summary>
    public abstract class CommandHandler
    {
        private readonly CommandBus _commands;

        protected CommandHandler(GameplayContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            World = context.World;
            _commands = context.Commands;
        }

        protected World World { get; }

        protected void On<T>(Func<T, CommandResult> handle) where T : struct, ICommand =>
            _commands.Register(new Route<T>(handle ?? throw new ArgumentNullException(nameof(handle))));

        protected void On<T>(Func<Entity, T, CommandResult> handle) where T : struct, IEntityCommand =>
            _commands.Register(new EntityRoute<T>(World, handle ?? throw new ArgumentNullException(nameof(handle))));

        private sealed class Route<T> : ICommandHandler<T> where T : struct, ICommand
        {
            private readonly Func<T, CommandResult> _handle;

            public Route(Func<T, CommandResult> handle) => _handle = handle;

            public CommandResult Handle(T command) => _handle(command);
        }

        private sealed class EntityRoute<T> : ICommandHandler<T> where T : struct, IEntityCommand
        {
            private readonly World _world;
            private readonly Func<Entity, T, CommandResult> _handle;

            public EntityRoute(World world, Func<Entity, T, CommandResult> handle)
            {
                _world = world;
                _handle = handle;
            }

            public CommandResult Handle(T command) =>
                _world.TryGetEntity(command.Entity, out var entity)
                    ? _handle(entity, command)
                    : CommandResult.Rejected(CommandError.EntityNotFound);
        }
    }
}
