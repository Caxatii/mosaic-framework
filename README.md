# Mosaic Framework

Game architecture for Unity with the game logic in plain C#. The domain (world, rules, game flow) does not
reference Unity, so the whole game can be built and tested with regular .NET, and Unity is only the presentation.

- **Entities and components** in a `World` with sparse-set storage: allocation-free queries and systems.
- **Commands in, events out**: input from the player, AI, cheats or network goes through a `CommandBus`; every change
  is announced by an event on an allocation-free `EventBus`.
- **Strict roles**: Operations are the only code that changes components, Evaluators and Queries only read,
  Reactions connect features without making them depend on each other.
- **Predictable actions**: each command or system tick runs as one `Simulation` action with clear visibility rules,
  and reads that miss a buffered change are reported.
- **Debugging**: simulation trace and world inspector in the editor, match recording and exact replay.
- **UI shell**: screens that follow the domain state machine, windows, popups, input layers, entity views with pooling.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the roles, the life of an action, visibility rules and debugging tools.

## Installation

Unity 6000.3 or newer. In **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/Caxatii/mosaic-framework.git
```

Dependencies (`com.unity.ugui`, `com.unity.inputsystem`) are installed automatically.

## Quick start

```csharp
using Mosaic.Core;

public sealed class Health : IModel
{
    public Health(float max) => Max = Current = max;

    public float Max { get; }
    public float Current { get; private set; }

    internal float Take(float amount)
    {
        var dealt = System.Math.Min(amount, Current);
        Current -= dealt;
        return dealt;
    }
}

public readonly struct Damaged : IEvent
{
    public Damaged(EntityId target, float amount) { Target = target; Amount = amount; }

    public EntityId Target { get; }
    public float Amount { get; }
}

public readonly struct HitCommand : IEntityCommand
{
    EntityId IEntityCommand.Entity => Target;

    public HitCommand(EntityId target, float amount) { Target = target; Amount = amount; }

    public EntityId Target { get; }
    public float Amount { get; }
}

public enum HitError { NoHealth }

internal sealed class HealthOperations
{
    private readonly IEventBus _events;

    public HealthOperations(IEventBus events) => _events = events;

    public void Damage(Entity target, float amount)
    {
        var dealt = target.Get<Health>().Take(amount);
        if (dealt > 0)
            _events.Publish(new Damaged(target.Id, dealt));
    }
}

internal sealed class HealthCommandHandler : CommandHandler
{
    private readonly HealthOperations _health;

    public HealthCommandHandler(GameplayContext context, HealthOperations health) : base(context)
    {
        _health = health;
        On<HitCommand>(Hit);
    }

    private CommandResult Hit(Entity target, HitCommand command)
    {
        if (!target.Has<Health>())
            return CommandResult.Rejected(HitError.NoHealth);

        _health.Damage(target, command.Amount);
        return CommandResult.Ok;
    }
}
```

Assembling and using the world:

```csharp
var context = new GameplayContext();
var health = new HealthOperations(context.Events);
new HealthCommandHandler(context, health);

context.Events.Subscribe<Damaged>(e => UnityEngine.Debug.Log($"{e.Target} took {e.Amount}"));

var enemy = context.World.Spawn(kind: 1);
enemy.Add(new Health(30));

var result = context.Commands.Send(new HitCommand(enemy.Id, 10)); // Ok, logs "Entity(0:0) took 10"
```

In a real game the assembly lives in one composition root (a match class) that also owns the tick order of systems.

## Tests

Add the package to `"testables"` in `Packages/manifest.json` to run its tests in the Unity Test Runner.

## License

[MIT](LICENSE.md)
