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

## Mosaic and ECS

Mosaic uses the core ideas of ECS: entities are ids, behaviour comes from composing components, systems iterate
over component sets, and structural changes are deferred. It is **not** a performance-first ECS like DOTS. The
differences are deliberate:

- components are classes that protect their own rules, and only the Operations of the owning feature change them;
- input enters through commands that can be rejected with a reason, and changes are announced with events;
- every command and system tick is one action with simple visibility rules;
- the target is thousands to tens of thousands of entities on one thread, with readable and testable rules.

The full comparison, with the reason behind each difference, is in
[Architecture, section 2](Documentation~/Architecture.md#2-mosaic-and-ecs).

## Documentation

| Read | When |
|---|---|
| [Getting Started](Documentation~/GettingStarted.md) | first: a step-by-step tutorial that builds a small complete game |
| [Design Guide](Documentation~/DesignGuide.md) | every day: common decisions with "don't / what goes wrong / do / why" |
| [Architecture](Documentation~/Architecture.md) | for reference: roles, the life of an action, visibility rules, debugging tools |

## Installation

Unity 2021.3 or newer (tested on 6000.3; if something doesn't work on an older version, please open an issue).
In **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/Caxatii/mosaic-framework.git
```

Dependencies (`com.unity.ugui`, `com.unity.inputsystem`) are installed automatically. On Unity 2021 and 2022
the project also needs the **TextMeshPro** package (it is installed in the default project templates; in Unity 6
it is part of uGUI).

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
    private readonly HealthOperations _operations;

    public HealthCommandHandler(GameplayContext context, HealthOperations operations) : base(context)
    {
        _operations = operations;
        On<HitCommand>(Hit);
    }

    private CommandResult Hit(Entity target, HitCommand command)
    {
        if (!target.Has<Health>())
            return CommandResult.Rejected(HitError.NoHealth);

        _operations.Damage(target, command.Amount);
        return CommandResult.Ok;
    }
}
```

Assembling and using the world:

```csharp
var context = new GameplayContext();
var healthOperations = new HealthOperations(context.Events);
new HealthCommandHandler(context, healthOperations);

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
