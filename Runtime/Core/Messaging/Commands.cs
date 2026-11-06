using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    public interface ICommand
    {
    }

    internal interface ICommandHandler<TCommand> where TCommand : struct, ICommand
    {
        CommandResult Handle(TCommand command);
    }

    public interface ICommandBus
    {
        CommandResult Send<TCommand>(in TCommand command) where TCommand : struct, ICommand;
    }

    public readonly struct CommandResult
    {
        private CommandResult(bool isOk, Enum reason)
        {
            IsOk = isOk;
            Reason = reason;
        }

        public static CommandResult Ok => new CommandResult(true, null);

        public bool IsOk { get; }

        public Enum Reason { get; }

        public static CommandResult Rejected<TReason>(TReason reason) where TReason : struct, Enum =>
            new CommandResult(false, BoxedReason<TReason>.Of(reason));

        public static CommandResult Rejected(Enum reason) =>
            new CommandResult(false, reason ?? throw new ArgumentNullException(nameof(reason)));

        public override string ToString() => IsOk ? "Ok" : $"Rejected({Reason})";

        private static class BoxedReason<TReason> where TReason : struct, Enum
        {
            private static readonly Dictionary<TReason, Enum> Cache = new Dictionary<TReason, Enum>();

            public static Enum Of(TReason reason)
            {
                if (!Cache.TryGetValue(reason, out var boxed))
                    Cache.Add(reason, boxed = reason);
                return boxed;
            }
        }
    }

    /// <summary>
    /// Routes each command type to its single handler and runs it as one Simulation action.
    /// </summary>
    public sealed class CommandBus : ICommandBus
    {
        private readonly Dictionary<Type, object> _routes = new Dictionary<Type, object>();
        private readonly Simulation _simulation;
        private readonly ILog _log;

        public CommandBus(Simulation simulation, ILog log)
        {
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _log = log ?? NullLog.Instance;
        }

        public bool Verbose { get; set; }

        public CommandRecording Recording { get; set; }

        internal void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : struct, ICommand
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_routes.ContainsKey(typeof(TCommand)))
                throw new InvalidOperationException($"Command {typeof(TCommand).Name} already has a handler");

            _routes.Add(typeof(TCommand), handler);
        }

        public bool Handles(Type commandType) => _routes.ContainsKey(commandType);

        public CommandResult Send<TCommand>(in TCommand command) where TCommand : struct, ICommand
        {
            if (!_routes.TryGetValue(typeof(TCommand), out var handler))
                throw new InvalidOperationException($"Command {typeof(TCommand).Name} has no handler");

            var external = !_simulation.IsRunning;
            var trace = _simulation.Trace;
            trace?.Enter(TraceFormat.Of(command), quiet: false);

            CommandResult result;
            try
            {
                result = _simulation.Run(Route<TCommand>.Execute, ((ICommandHandler<TCommand>)handler, command));
            }
            catch
            {
                trace?.Exit(" → exception");
                throw;
            }

            trace?.Exit(" → " + result);

            if (external)
                Recording?.RecordCommand(command, result);

            if (Verbose)
                _log.Info($"{typeof(TCommand).Name} → {result}");

            return result;
        }

        public CommandResult SendBoxed(ICommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            var type = command.GetType();
            if (!BoxedSenders.TryGetValue(type, out var send))
            {
                var method = typeof(CommandBus).GetMethod(nameof(SendAs), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .MakeGenericMethod(type);
                BoxedSenders.Add(type, send = (Func<CommandBus, ICommand, CommandResult>)Delegate.CreateDelegate(
                    typeof(Func<CommandBus, ICommand, CommandResult>), method));
            }

            return send(this, command);
        }

        private static readonly Dictionary<Type, Func<CommandBus, ICommand, CommandResult>> BoxedSenders =
            new Dictionary<Type, Func<CommandBus, ICommand, CommandResult>>();

        private static CommandResult SendAs<TCommand>(CommandBus bus, ICommand command) where TCommand : struct, ICommand =>
            bus.Send((TCommand)command);

        private static class Route<TCommand> where TCommand : struct, ICommand
        {
            public static readonly Func<(ICommandHandler<TCommand> Handler, TCommand Command), CommandResult> Execute =
                state => state.Handler.Handle(state.Command);
        }
    }
}
