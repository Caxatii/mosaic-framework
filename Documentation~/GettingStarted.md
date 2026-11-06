# Getting Started with Mosaic

This tutorial takes you from an empty Unity project to a small, complete game built on Mosaic. You will learn the
framework by using it, one idea at a time. No previous experience with ECS or game architecture is needed;
if you can write a `MonoBehaviour`, you can follow along.

**What you will build: Coin Rush.** The player runs around a field and collects coins for 30 seconds. There is a
main menu, the game itself and a results screen with your score.

It is a tiny game on purpose. Every part of it shows one piece of Mosaic:

| Step | You build | You learn |
|---|---|---|
| 1 | the project layout | why game logic lives in its own assembly without Unity |
| 2 | movement | components, commands, command handlers, Operations, systems |
| 3 | coins | events, Evaluators, Queries, spawning entities |
| 4 | the match class | the composition root and the tick order |
| 5 | a test | testing game rules without Unity, in milliseconds |
| 6 | the scene | showing entities with views, sending input as commands |
| 7 | menu → game → results | domain states and screens |
| 8 | debugging | the trace and the world inspector |

Plan about two hours. Each step ends with something that compiles and runs, so you can stop at any point.

> When you want to know *why* something is done a certain way, see the [Design Guide](DesignGuide.md).
> For the full description of roles and rules, see [Architecture](Architecture.md); to look up a class, see the
> [Reference](Reference.md).

---

## Step 1. Install and set up the project

### 1.1 Install the package

You need Unity 2021.3 or newer. The package is tested on Unity 6000.3; if something doesn't work on an older
version, please open an issue on GitHub.

1. Open **Window > Package Manager**.
2. Click **+ > Add package from git URL**.
3. Paste `https://github.com/Caxatii/mosaic-framework.git#v0.1.1` and click **Add**.

Unity also installs the two packages Mosaic depends on: uGUI and the Input System. On Unity 2021 and 2022, make
sure **TextMeshPro** is installed too (it is in new projects by default; in Unity 6 it is part of uGUI). If Unity asks whether to enable
the new Input System, answer **Yes** (the editor restarts).

### 1.2 Create the folders

Create this structure under `Assets`:

```
Assets/CoinRush/
  Game/          the game rules: no Unity here
  Game/Tests/    tests for the game rules
  Unity/         everything the player sees: scene code, views, screens
  Prefabs/
  Content/       config assets
```

### 1.3 Create the assemblies

An assembly definition (`.asmdef`) splits your code into separate assemblies. We use them to make one rule
impossible to break by accident: **game rules must not use Unity.**

Why does that matter? Code that does not touch Unity can be tested in milliseconds without starting a scene, runs
the same in a replay or on a server, and cannot depend on frame rate or on which `MonoBehaviour` happens to update
first. Mosaic is built around that idea, and an assembly with `noEngineReferences` makes the compiler enforce it.

In each folder: **right click > Create > Scripting > Assembly Definition**, then set it up as below (you can also
paste the JSON into the file).

`Game/CoinRush.Game.asmdef`, the rules:

```json
{
    "name": "CoinRush.Game",
    "rootNamespace": "CoinRush",
    "references": [ "Mosaic.Core" ],
    "noEngineReferences": true
}
```

`Unity/CoinRush.Unity.asmdef`, the presentation:

```json
{
    "name": "CoinRush.Unity",
    "rootNamespace": "CoinRush",
    "references": [ "CoinRush.Game", "Mosaic.Core", "Mosaic.Unity", "Unity.InputSystem", "Unity.TextMeshPro" ]
}
```

`Game/Tests/CoinRush.Tests.asmdef`, the tests:

```json
{
    "name": "CoinRush.Tests",
    "rootNamespace": "CoinRush",
    "references": [ "CoinRush.Game", "Mosaic.Core", "UnityEngine.TestRunner", "UnityEditor.TestRunner" ],
    "includePlatforms": [ "Editor" ],
    "overrideReferences": true,
    "precompiledReferences": [ "nunit.framework.dll" ],
    "defineConstraints": [ "UNITY_INCLUDE_TESTS" ]
}
```

Finally, let the tests see `internal` classes of the game. Create `Game/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CoinRush.Tests")]
```

The arrows between assemblies now look like this, and the compiler will refuse anything else:

```
CoinRush.Unity ──► CoinRush.Game ──► Mosaic.Core
       │                                  ▲
       └──────────► Mosaic.Unity ─────────┘
CoinRush.Tests ──► CoinRush.Game
```

**Checkpoint.** The project compiles with no errors. If Unity complains about a missing reference, check the names
in `references`: they must match the `name` field of the referenced asmdef exactly.

---

## Step 2. Movement: your first feature

In Mosaic, a game object is not a class with fields and an `Update` method. It is an **entity**: just an id. What an
entity *is* comes from the **components** attached to it. A player is "something with a position, that can move,
that has a wallet". A coin is "something with a position, that is worth some money".

A **feature** is one area of the game (movement, coins, combat). Each feature has its components and the classes
that work with them. Let's build movement.

> **Know ECS already?** Entities, components and systems work the way you expect. The main differences: components
> are classes that guard their own rules, only one class per feature (its Operations) may change them, and input
> arrives as commands instead of input components. Why it is designed this way:
> [Architecture, section 2](Architecture.md#2-mosaic-and-ecs).

### 2.1 Components: the data

A component holds the data of one aspect of an entity. Its fields can only be changed through `internal` methods,
so code outside the game assembly (the UI, for example) can read them but never change them.

`Game/Movement/Movement.cs`

```csharp
using System;
using System.Numerics;
using Mosaic.Core;

namespace CoinRush.Movement
{
    /// <summary>Where the entity is on the field.</summary>
    public sealed class Position : IModel
    {
        public Position(Vector2 value) => Value = value;

        public Vector2 Value { get; private set; }

        internal void Set(Vector2 value) => Value = value;
    }

    /// <summary>How fast the entity moves and where it is heading.</summary>
    public sealed class Mover : IModel
    {
        public Mover(float speed) => Speed = speed;

        public float Speed { get; }
        public Vector2 Direction { get; private set; }

        internal void SetDirection(Vector2 direction) =>
            Direction = direction.LengthSquared() > 1 ? Vector2.Normalize(direction) : direction;
    }

    /// <summary>The designer-facing part of a config asset that creates a Mover.</summary>
    [Serializable]
    public sealed class MoverDefinition : IEntityPart
    {
        public float Speed = 5;

        public IModel CreateModel() => new Mover(Speed);
    }
}
```

Notice that we use `System.Numerics.Vector2`, not `UnityEngine.Vector2`. The game assembly has no Unity, remember?
The presentation converts between the two with `ToUnity()` and `ToNumerics()`.

`MoverDefinition` is what a designer edits in the inspector. When an entity spawns, every definition on its config
creates its component. There is no `PositionDefinition`: the position is not known until the moment of spawning,
so the code that spawns the entity adds it.

### 2.2 Operations: the only code that changes components

Every feature has one class that is allowed to change its components: its **Operations**. Having exactly one place
means the rules ("speed can't be negative", "a dead entity doesn't move") live in one place, and nothing can
bypass them.

`Game/Movement/MovementOperations.cs`

```csharp
using System.Numerics;
using Mosaic.Core;

namespace CoinRush.Movement
{
    internal sealed class MovementOperations
    {
        public void SetDirection(Entity entity, Vector2 direction) =>
            entity.Get<Mover>().SetDirection(direction);

        public void Place(Entity entity, Vector2 position)
        {
            if (entity.Get<Position>() is { } existing)
                existing.Set(position);
            else
                entity.Add(new Position(position));
        }

        internal void Advance(Mover mover, Position position, float deltaTime) =>
            position.Set(position.Value + mover.Direction * mover.Speed * deltaTime);
    }
}
```

Operations are `internal`: only the game assembly (and its tests) can call them. The UI will have to ask nicely,
with a command.

### 2.3 A command: how the outside world asks for something

The player presses a key. The game shouldn't move the player directly from the input code; instead, input sends a
**command**: a small message that says "this entity wants to move in this direction". A command can be accepted or
rejected, with a reason.

`Game/Movement/MoveCommand.cs`

```csharp
using System.Numerics;
using Mosaic.Core;

namespace CoinRush.Movement
{
    public readonly struct MoveCommand : IEntityCommand
    {
        EntityId IEntityCommand.Entity => Mover;

        public MoveCommand(EntityId mover, Vector2 direction)
        {
            Mover = mover;
            Direction = direction;
        }

        public EntityId Mover { get; }
        public Vector2 Direction { get; }
    }

    public enum MoveError
    {
        CannotMove,
    }

    internal sealed class MovementCommandHandler : CommandHandler
    {
        private readonly MovementOperations _operations;

        public MovementCommandHandler(GameplayContext context, MovementOperations operations) : base(context)
        {
            _operations = operations;
            On<MoveCommand>(Move);
        }

        private CommandResult Move(Entity entity, MoveCommand command)
        {
            if (!entity.Has<Mover>())
                return CommandResult.Rejected(MoveError.CannotMove);

            _operations.SetDirection(entity, command.Direction);
            return CommandResult.Ok;
        }
    }
}
```

A few things to notice:
- The command is a `readonly struct` with an `EntityId` and numbers. Commands are sent every frame, so they must
  not create garbage, and they must be simple enough to write to a file (for replays).
- `IEntityCommand` tells Mosaic which entity the command is for. If that entity doesn't exist anymore, the command
  is rejected automatically and your handler is not even called.
- The handler **checks** and **calls Operations**. It never changes components itself.
- `On<MoveCommand>(Move)` in the constructor registers the handler. Creating the handler is all it takes.

### 2.4 A system: logic that runs every frame

Moving happens by itself, every frame, for every entity that has a `Mover` and a `Position`. That is a **system**.

`Game/Movement/MovementSystem.cs`

```csharp
using Mosaic.Core;

namespace CoinRush.Movement
{
    internal sealed class MovementSystem : EntitySystem<Mover, Position>
    {
        private readonly MovementOperations _operations;

        public MovementSystem(GameplayContext context, MovementOperations operations) : base(context) =>
            _operations = operations;

        protected override void Tick(Entity entity, Mover mover, Position position, float deltaTime) =>
            _operations.Advance(mover, position, deltaTime);
    }
}
```

`EntitySystem<Mover, Position>` finds every entity with both components and calls your `Tick` for each, without
creating garbage. Unlike handlers, systems do **not** start working when created: you will add them to a tick list
in step 4.

### 2.5 Queries: how the outside world reads

The scene will need to know where each entity is, to place its sprite. It must not get the `Position` component
itself (it could hold on to it, or reach its `internal` methods). Instead, a **Queries** class answers with plain
values.

`Game/Movement/MovementQueries.cs`

```csharp
using System.Numerics;
using Mosaic.Core;

namespace CoinRush.Movement
{
    public sealed class MovementQueries
    {
        private readonly World _world;

        internal MovementQueries(World world) => _world = world;

        public Vector2? PositionOf(EntityId entity) => _world.Get<Position>(entity)?.Value;
    }
}
```

**Checkpoint.** Movement compiles. Nothing moves yet: we have the parts, but nobody has assembled them. That
comes in step 4, after the coins.

---

## Step 3. Coins: events, Evaluators and spawning

### 3.1 Components

`Game/Coins/Coins.cs`

```csharp
using System;
using Mosaic.Core;

namespace CoinRush.Coins
{
    /// <summary>A coin lying on the field.</summary>
    public sealed class Coin : IModel
    {
        public Coin(int value) => Value = value;

        public int Value { get; }
    }

    [Serializable]
    public sealed class CoinDefinition : IEntityPart
    {
        public int Value = 1;

        public IModel CreateModel() => new Coin(Value);
    }

    /// <summary>Money collected by an entity. Anything with a wallet picks up coins.</summary>
    public sealed class Wallet : IModel
    {
        public int Coins { get; private set; }

        internal void Add(int amount) => Coins += amount;
    }

    [Serializable]
    public sealed class WalletDefinition : IEntityPart
    {
        public IModel CreateModel() => new Wallet();
    }
}
```

Look at `Wallet`: we didn't write "the player picks up coins". We wrote "anything with a wallet picks up coins". Give
a wallet to an enemy later, and enemies will compete for coins with no new code.

### 3.2 An event: telling everyone what happened

When a coin is collected, several things should react: the score label, a sound, a particle effect. The coins
feature shouldn't know about any of them. It just announces the fact with an **event**, and whoever cares listens.

`Game/Coins/CoinCollected.cs`

```csharp
using Mosaic.Core;

namespace CoinRush.Coins
{
    public readonly struct CoinCollected : IEvent
    {
        public CoinCollected(EntityId collector, int value, int total)
        {
            Collector = collector;
            Value = value;
            Total = total;
        }

        public EntityId Collector { get; }
        public int Value { get; }
        public int Total { get; }
    }
}
```

Events are named in the past tense: something already happened. They carry the numbers of that moment (`Value`,
`Total`), so a listener can draw "+1" and "Coins: 7" without asking the game anything.

### 3.3 Operations: change, then announce

`Game/Coins/CoinOperations.cs`

```csharp
using System.Numerics;
using CoinRush.Movement;
using Mosaic.Core;

namespace CoinRush.Coins
{
    internal sealed class CoinOperations
    {
        private readonly World _world;
        private readonly IEventBus _events;
        private readonly MovementOperations _movementOperations;

        public CoinOperations(World world, IEventBus events, MovementOperations movementOperations)
        {
            _world = world;
            _events = events;
            _movementOperations = movementOperations;
        }

        public Entity SpawnCoin(EntityDefinition coin, Vector2 at)
        {
            var entity = _world.Spawn(coin);
            _movementOperations.Place(entity, at);
            return entity;
        }

        public void Collect(Entity collector, Entity coin)
        {
            var wallet = collector.Get<Wallet>();
            var value = coin.Get<Coin>().Value;

            wallet.Add(value);
            coin.Destroy();
            _events.Publish(new CoinCollected(collector.Id, value, wallet.Coins));
        }
    }
}
```

Three habits to copy:
- **The event is published right after the change, by the code that made it.** If `Collect` returns early for
  some reason, no event goes out, so nobody celebrates a coin that wasn't collected.
- **Ask "what if this happens twice?"** For example, two players touch the same coin in the same frame. Here Mosaic
  covers you: `coin.Destroy()` makes the coin unavailable immediately, so the second player's search doesn't find it,
  and using the destroyed coin by mistake throws an `EntityNotAliveException` instead of paying twice. You don't need
  an "already collected" flag. A test for exactly this case is in step 5.
- **Coins don't set positions themselves.** `Position` belongs to movement, so coins ask `MovementOperations` to
  place the new coin. Each feature only changes its own components.

`_world.Spawn(coin)` creates an entity from a definition: every part of the definition (here `CoinDefinition`)
creates its component.

### 3.4 An Evaluator: a question about the world

The pickup rule needs to ask "is there a coin close enough to this position?". Questions like that, which read the
world and change nothing, go into an **Evaluator**.

`Game/Coins/CoinEvaluator.cs`

```csharp
using System.Numerics;
using CoinRush.Movement;
using Mosaic.Core;

namespace CoinRush.Coins
{
    internal sealed class CoinEvaluator
    {
        public const float PickupRadius = 0.6f;

        private readonly World _world;

        public CoinEvaluator(World world) => _world = world;

        public Entity? FindCoinNear(Vector2 position)
        {
            foreach (var coin in _world.Query<Coin, Position>())
                if (Vector2.Distance(coin.Get<Position>().Value, position) <= PickupRadius)
                    return coin;

            return null;
        }
    }
}
```

`_world.Query<Coin, Position>()` goes over every entity that has both components. It doesn't create garbage, so it
is fine to call it every frame.

### 3.5 The pickup system

`Game/Coins/CoinPickupSystem.cs`

```csharp
using CoinRush.Movement;
using Mosaic.Core;

namespace CoinRush.Coins
{
    internal sealed class CoinPickupSystem : EntitySystem<Wallet, Position>
    {
        private readonly CoinOperations _operations;
        private readonly CoinEvaluator _evaluator;

        public CoinPickupSystem(GameplayContext context, CoinOperations operations, CoinEvaluator evaluator) : base(context)
        {
            _operations = operations;
            _evaluator = evaluator;
        }

        protected override void Tick(Entity collector, Wallet wallet, Position position, float deltaTime)
        {
            if (_evaluator.FindCoinNear(position.Value) is { } coin)
                _operations.Collect(collector, coin);
        }
    }
}
```

The system decides *when*, the Evaluator answers *whether*, the Operations *do it*. Each class has one job, so each
is easy to read and to test.

> **Why can we destroy a coin while the system is still looping?** Every system tick runs as one *action*. A
> destroyed entity disappears for everyone at once: `Get` returns `null`, queries and systems skip it, commands to it
> are rejected. Its memory is cleaned up only when the action ends, so the loops that are running don't break.
> Adding and removing components works differently: those changes wait for the end of the action. Section 3.4 of
> the Design Guide explains why.

### 3.6 Queries

`Game/Coins/CoinQueries.cs`

```csharp
using Mosaic.Core;

namespace CoinRush.Coins
{
    public sealed class CoinQueries
    {
        private readonly World _world;

        internal CoinQueries(World world) => _world = world;

        public int CoinsOf(EntityId entity) => _world.Get<Wallet>(entity)?.Coins ?? 0;

        public int CoinsLeft
        {
            get
            {
                var count = 0;
                foreach (var _ in _world.Query<Coin>())
                    count++;
                return count;
            }
        }
    }
}
```

**Checkpoint.** Everything compiles. We have two features and nothing connects them yet.

---

## Step 4. The match: putting it all together

So far we have parts. Something has to create them, connect them and tick the systems every frame. That something
is the **composition root**: one class where the whole game world is assembled. In Coin Rush it is the match.

### 4.1 Spawning the field

First, a small Operations class that sets up the field: it spawns the player and scatters coins.

`Game/FieldOperations.cs`

```csharp
using System;
using System.Numerics;
using CoinRush.Coins;
using CoinRush.Movement;
using Mosaic.Core;

namespace CoinRush
{
    internal sealed class FieldOperations
    {
        public const float Width = 16;
        public const float Height = 9;

        private readonly World _world;
        private readonly MovementOperations _movementOperations;
        private readonly CoinOperations _coinOperations;
        private readonly EntityDefinition _player;
        private readonly EntityDefinition _coin;
        private readonly GameRandom _random;

        public FieldOperations(World world, MovementOperations movementOperations, CoinOperations coinOperations,
            EntityDefinition player, EntityDefinition coin, int seed)
        {
            _world = world;
            _movementOperations = movementOperations;
            _coinOperations = coinOperations;
            _player = player;
            _coin = coin;
            _random = new GameRandom(seed);
        }

        public Entity SpawnPlayer()
        {
            var player = _world.Spawn(_player);
            _movementOperations.Place(player, Vector2.Zero);
            return player;
        }

        public Entity SpawnCoin(Vector2 at) => _coinOperations.SpawnCoin(_coin, at);

        public void ScatterCoins(int count)
        {
            for (var i = 0; i < count; i++)
                SpawnCoin(new Vector2(
                    ((float)_random.NextDouble() - 0.5f) * Width,
                    ((float)_random.NextDouble() - 0.5f) * Height));
        }
    }
}
```

The random generator (`GameRandom`) is created from a **seed** and owned by the game. Don't use `UnityEngine.Random`
here: the game assembly can't see Unity anyway, and a seeded generator gives the same coins every time you replay a
match. Its whole state is one number, so it can also be saved with the game (see `RandomStreams` in the Reference).

### 4.2 The match class

`Game/CoinRushMatch.cs`

```csharp
using CoinRush.Coins;
using CoinRush.Movement;
using Mosaic.Core;

namespace CoinRush
{
    public sealed class CoinRushMatch
    {
        private readonly ISystem[] _systems;

        public CoinRushMatch(EntityDefinition player, EntityDefinition coin, int seed, ILog log = null, bool strict = false)
        {
            Context = new GameplayContext(log, strict);
            var world = Context.World;
            var events = Context.Events;

            // Movement
            var movementOperations = new MovementOperations();
            new MovementCommandHandler(Context, movementOperations);
            var movementSystem = new MovementSystem(Context, movementOperations);

            // Coins
            var coinOperations = new CoinOperations(world, events, movementOperations);
            var pickupSystem = new CoinPickupSystem(Context, coinOperations, new CoinEvaluator(world));

            // Field
            Field = new FieldOperations(world, movementOperations, coinOperations, player, coin, seed);

            // Tick order: move first, then pick up coins at the new position.
            _systems = new ISystem[]
            {
                movementSystem,
                pickupSystem,
            };

            // What the outside world may read.
            Movement = new MovementQueries(world);
            Coins = new CoinQueries(world);
        }

        public GameplayContext Context { get; }
        public MovementQueries Movement { get; }
        public CoinQueries Coins { get; }

        public EntityId Player { get; private set; }
        public float Time { get; private set; }

        internal FieldOperations Field { get; }

        public void Start(int coinCount = 20) =>
            Context.Simulation.Run(() =>
            {
                Player = Field.SpawnPlayer().Id;
                Field.ScatterCoins(coinCount);
            });

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0)
                return;

            Time += deltaTime;
            foreach (var system in _systems)
                Context.Simulation.Tick(system, deltaTime);
        }
    }
}
```

This class is worth reading slowly, because every Mosaic game has one like it.

- **`GameplayContext`** holds the shared objects of one world: the `World` with all entities, the event bus, the
  command bus and the `Simulation` that runs actions. `strict: true` is for tests (more on that in step 5).
- **Order matters.** Things are created bottom-up: movement first, because coins need `MovementOperations`.
  If you get it wrong, the code doesn't compile, which is exactly what we want.
- **Handlers connect themselves.** `new MovementCommandHandler(...)` registers it on the command bus; we don't
  need to keep it.
- **Systems don't.** A system runs only if it is in `_systems`. Forgetting to add one is a silent bug: the game
  compiles and the system never runs.
- **The tick order is written down in one place,** with the reason. Pick up after moving, so the player collects a
  coin the same frame they reach it.
- **`Simulation.Run` and `Simulation.Tick`** wrap work into an *action*: changes are buffered while it runs, and
  events are delivered when it ends. Always change the world inside an action.
- **Only Queries are public.** The UI can read `Movement` and `Coins` and send commands through
  `Context.Commands`. Everything that changes the world is `internal`.

**Checkpoint.** The game exists. It has no picture yet, but it can already be played by code. Let's prove it.

---

## Step 5. Your first test

Because the game rules don't use Unity, you can test them like any other C# code: create a match, send commands,
tick, check the result. No scene, no prefabs, no waiting.

`Game/Tests/CoinRushTests.cs`

```csharp
using System.Collections.Generic;
using System.Numerics;
using CoinRush.Coins;
using CoinRush.Movement;
using Mosaic.Core;
using NUnit.Framework;

namespace CoinRush.Tests
{
    public sealed class CoinRushTests
    {
        private static readonly EntityDefinition Player = new EntityDefinition(1, "Player",
            new IEntityPart[] { new MoverDefinition { Speed = 4 }, new WalletDefinition() });

        private static readonly EntityDefinition Coin = new EntityDefinition(2, "Coin",
            new IEntityPart[] { new CoinDefinition { Value = 5 } });

        private CoinRushMatch _match;
        private List<CoinCollected> _collected;

        [SetUp]
        public void SetUp()
        {
            _match = new CoinRushMatch(Player, Coin, seed: 1, strict: true);
            _match.Start(coinCount: 0);

            _collected = new List<CoinCollected>();
            _match.Context.Events.Subscribe<CoinCollected>(_collected.Add);
        }

        private EntityId SpawnCoinAt(Vector2 at)
        {
            var id = default(EntityId);
            _match.Context.Simulation.Run(() => id = _match.Field.SpawnCoin(at).Id);
            return id;
        }

        private void Tick(float seconds)
        {
            for (var time = 0f; time < seconds; time += 0.02f)
                _match.Tick(0.02f);
        }

        [Test]
        public void Player_collects_a_coin_by_walking_into_it()
        {
            SpawnCoinAt(new Vector2(2, 0));

            var result = _match.Context.Commands.Send(new MoveCommand(_match.Player, Vector2.UnitX));
            Tick(1);

            Assert.That(result.IsOk);
            Assert.That(_collected, Has.Count.EqualTo(1));
            Assert.That(_collected[0].Total, Is.EqualTo(5));
            Assert.That(_match.Coins.CoinsOf(_match.Player), Is.EqualTo(5));
            Assert.That(_match.Coins.CoinsLeft, Is.Zero);
        }

        [Test]
        public void Standing_still_collects_nothing()
        {
            SpawnCoinAt(new Vector2(2, 0));

            Tick(1);

            Assert.That(_collected, Is.Empty);
            Assert.That(_match.Coins.CoinsLeft, Is.EqualTo(1));
        }

        [Test]
        public void Two_collectors_touching_one_coin_get_it_once()
        {
            var second = default(EntityId);
            _match.Context.Simulation.Run(() => second = _match.Field.SpawnPlayer().Id);
            SpawnCoinAt(new Vector2(0.3f, 0));

            Tick(0.02f);

            Assert.That(_collected, Has.Count.EqualTo(1));
            Assert.That(_match.Coins.CoinsOf(_match.Player) + _match.Coins.CoinsOf(second), Is.EqualTo(5));
        }

        [Test]
        public void A_coin_cannot_be_told_to_move()
        {
            var coin = SpawnCoinAt(new Vector2(2, 0));

            var result = _match.Context.Commands.Send(new MoveCommand(coin, Vector2.UnitX));

            Assert.That(result.Reason, Is.EqualTo(MoveError.CannotMove));
        }
    }
}
```

Run them in **Window > General > Test Runner > EditMode > Run All**. They finish in milliseconds.

Habits worth keeping from the start:
- **Use `strict: true` in tests.** In strict mode, mistakes that would only print a warning in the game (an
  exception in an event listener, reading a value that hasn't been updated yet) fail the test right away.
- **Act like the player.** The tests send `MoveCommand`, the same thing the keyboard will send. They don't call
  `MovementOperations` directly, so they test what the player actually experiences.
- **Check results the way the UI will:** through events (`CoinCollected`) and Queries (`CoinsOf`).
- **Setup is the exception.** Placing a coin at an exact spot is test setup, not a player action, so the test
  calls `Field.SpawnCoin` inside `Simulation.Run`. That works because the tests can see `internal` members.

**Checkpoint.** Three green tests. You have a working, tested game without opening a scene.

---

## Step 6. See it in Unity

Now the game gets a picture. Remember the rule: **the presentation reads and asks, it never changes the game.**
It reads positions through Queries, listens to events, and turns key presses into commands.

### 6.1 Content: config assets

Designers describe entities with **config assets**. An `EntityConfig` has a prefab (what it looks like) and a list
of parts (what components it gets).

First, a small asset that holds the configs of our game, so scripts have one thing to reference:

`Unity/ContentConfig.cs`

```csharp
using Mosaic.Ui;
using UnityEngine;

namespace CoinRush.View
{
    [CreateAssetMenu(menuName = "Coin Rush/Content")]
    public sealed class ContentConfig : ScriptableObject
    {
        public EntityConfig Player;
        public EntityConfig Coin;

        public EntityView PrefabOf(int kind) =>
            kind == Player.Id ? Player.Prefab.GetComponent<EntityView>()
            : kind == Coin.Id ? Coin.Prefab.GetComponent<EntityView>()
            : null;
    }
}
```

Now create the assets:

1. **Prefabs.** Make two sprites (a circle for the player, a small yellow circle for a coin), save them as prefabs
   in `Prefabs/`, and add the **Entity View** component to the root of each. `EntityView` is how Mosaic connects a
   GameObject to an entity.
2. **Entity configs.** In `Content/`, **right click > Create > Mosaic > Entity** twice: `Player` and `Coin`.
   Each gets a unique **Id** automatically; don't change it, saves and replays refer to it.
3. **Parts.** Select `Player`, set its **Prefab**, then click **Add to Parts** and pick
   **CoinRush.Movement > MoverDefinition**, then again **CoinRush.Coins > WalletDefinition**. Select `Coin`, set its
   prefab and add **CoinRush.Coins > CoinDefinition**. The menu lists every `[Serializable]` class that implements
   `IEntityPart`, grouped by namespace.
4. **Content.** **Create > Coin Rush > Content**, and drag `Player` and `Coin` into it.

That is all the data the game needs. Want a faster player? Change `Speed` on the player config. A coin worth 5?
Change `Value`. No code involved.

### 6.2 Views: showing the world

`Unity/MatchView.cs`

```csharp
using System;
using CoinRush.Coins;
using Mosaic.Core;
using Mosaic.Ui;
using TMPro;

namespace CoinRush.View
{
    public sealed class MatchView : IDisposable
    {
        private readonly CoinRushMatch _match;
        private readonly TMP_Text _score;
        private readonly ViewPool _pool = new ViewPool();
        private readonly EventFeed _events;
        private readonly ViewSpawner _spawner;
        private readonly IDisposable _coinCollected;

        public MatchView(CoinRushMatch match, ContentConfig content, TMP_Text score)
        {
            _match = match;
            _score = score;

            _events = new EventFeed(match.Context);
            _spawner = new ViewSpawner(match.Context.World, _events, content.PrefabOf, null, _pool);
            _coinCollected = _events.Subscribe<CoinCollected>(OnCoinCollected);

            _spawner.CatchUp();
            ShowScore(match.Coins.CoinsOf(match.Player));
        }

        public void LateTick()
        {
            _events.Deliver();

            foreach (var view in _spawner.Views.All)
                if (_match.Movement.PositionOf(view.Id) is { } position)
                    view.transform.position = position.ToUnity();
        }

        public void Dispose()
        {
            _coinCollected.Dispose();
            _spawner.Dispose();
            _events.Dispose();
            _pool.Dispose();
        }

        private void OnCoinCollected(CoinCollected e)
        {
            if (e.Collector == _match.Player)
                ShowScore(e.Total);
        }

        private void ShowScore(int coins) => _score.text = $"Coins: {coins}";
    }
}
```

What each part does:
- **`EventFeed`** collects the game's events while the match ticks and hands them out in `LateTick`, after the
  simulation. Presentation always listens through a feed, never to `Context.Events` directly: a command sent from a
  feed subscriber is ordinary input and gets into match recordings (*Design Guide 5.5*).
- **`ViewSpawner`** watches the world. When an entity appears, it takes the prefab for its kind from a pool and puts
  it in the scene; when the entity is destroyed, the view goes back to the pool. You never instantiate entity
  views yourself.
- **`CatchUp()`** creates views for entities that existed before the view was created. The match was started
  before `MatchView`, so without this call the player and the coins would be invisible.
- **`LateTick`** reads positions from Queries every frame and moves the views. Reading every frame is right for
  things that change continuously, like positions.
- **The score** listens to `CoinCollected`. Listening to an event is right for things that change at a moment.
  The event already has the total, so there is no need to ask the game.
- **`Dispose`** releases everything: the subscription, the spawner, the feed and the pool (which destroys the
  views).
  Every `Subscribe` needs someone who disposes it.

### 6.3 Input: from keys to commands

`Unity/PlayerInput.cs`

```csharp
using System.Numerics;
using CoinRush.Movement;
using UnityEngine.InputSystem;

namespace CoinRush.View
{
    public sealed class PlayerInput
    {
        private readonly CoinRushMatch _match;
        private Vector2 _sent;

        public PlayerInput(CoinRushMatch match) => _match = match;

        public void Tick()
        {
            if (Keyboard.current is not { } keyboard)
                return;

            var direction = Vector2.Zero;
            if (keyboard.aKey.isPressed) direction.X -= 1;
            if (keyboard.dKey.isPressed) direction.X += 1;
            if (keyboard.sKey.isPressed) direction.Y -= 1;
            if (keyboard.wKey.isPressed) direction.Y += 1;

            if (direction == _sent)
                return;

            _sent = direction;
            _match.Context.Commands.Send(new MoveCommand(_match.Player, direction));
        }
    }
}
```

The input doesn't move anything. It tells the game what the player wants and lets the game decide. That's why an
AI, a replay or a network player can control an entity exactly the same way: they all send `MoveCommand`.

It only sends a command when the direction changes. Sending one every frame would also work, but this keeps a
match recording short and readable.

### 6.4 Run it

A temporary `MonoBehaviour` to start everything. In step 7 we replace it with a proper game flow.

`Unity/QuickStart.cs`

```csharp
using System;
using Mosaic.Infrastructure;
using TMPro;
using UnityEngine;

namespace CoinRush.View
{
    public sealed class QuickStart : MonoBehaviour
    {
        [SerializeField] private ContentConfig _content;
        [SerializeField] private TMP_Text _score;

        private CoinRushMatch _match;
        private MatchView _view;
        private PlayerInput _input;

        private void Start()
        {
            _match = new CoinRushMatch(_content.Player.ToDefinition(), _content.Coin.ToDefinition(),
                seed: Environment.TickCount, log: new UnityLog());
            _match.Start();

            _view = new MatchView(_match, _content, _score);
            _input = new PlayerInput(_match);
        }

        private void Update()
        {
            _input.Tick();
            _match.Tick(Time.deltaTime);
        }

        private void LateUpdate() => _view.LateTick();

        private void OnDestroy() => _view?.Dispose();
    }
}
```

In the scene: create an empty GameObject with `QuickStart`, drag in the content asset, and add a
**UI > Text - TextMeshPro** for the score. Set the camera to orthographic with **Size** 5 so the field fits.

Press Play. Move with WASD, collect coins, watch the score go up.

Look at how little the `MonoBehaviour` does: it creates objects and forwards `Update` to them. **This is the only
place where Unity's update loop touches the game.** Everything else is plain C#.

**Checkpoint.** A playable game. Nothing ends it yet, there's no menu, and the coins are gone after a while. Let's
turn it into a real game.

---

## Step 7. Menu, game, results: the game flow

A real game has more than a match: a menu, the match, a results screen, and the transitions between them. Mosaic
splits this into two state machines that work together:

```
Domain (CoinRush.Game)                 Presentation (CoinRush.Unity)
  MenuState    ──Play()──►               MenuScreen
  PlayState    ──30 seconds──►           PlayScreen
  ResultState  ──PlayAgain()/BackToMenu  ResultScreen
        │                                      ▲
        └──── StateMachine.Changed ────────────┘  ScreenStateMachine follows the domain
```

- **Domain states** decide what the game is doing and when it moves on. They contain the rules of the flow ("a
  match lasts 30 seconds", "remember the best score") and work without Unity, so they can be tested.
- **Screens** show the current state and pass button clicks to it. A screen never decides where the game goes.

### 7.1 Domain states

`Game/Flow/GameFlowContext.cs`

```csharp
using System;
using Mosaic.Core;

namespace CoinRush.Flow
{
    /// <summary>What all states share: the state machine, the content and what outlives a match.</summary>
    public sealed class GameFlowContext
    {
        private readonly EntityDefinition _player;
        private readonly EntityDefinition _coin;
        private readonly ILog _log;

        public GameFlowContext(StateMachine<GameState> states, EntityDefinition player, EntityDefinition coin, ILog log = null)
        {
            States = states;
            _player = player;
            _coin = coin;
            _log = log;
        }

        public StateMachine<GameState> States { get; }
        public int BestScore { get; internal set; }

        internal CoinRushMatch CreateMatch() => new CoinRushMatch(_player, _coin, seed: Environment.TickCount, log: _log);
    }

    /// <summary>Base class of the game's states: gives every state access to the shared GameFlowContext.</summary>
    public abstract class GameState : FlowState<GameState>
    {
        protected GameState(GameFlowContext context) : base(context.States) => Context = context;

        protected GameFlowContext Context { get; }
    }
}
```

`Game/Flow/States.cs`

```csharp
using System;

namespace CoinRush.Flow
{
    public sealed class MenuState : GameState
    {
        public MenuState(GameFlowContext context) : base(context) { }

        public int BestScore => Context.BestScore;

        public void Play() => Go(new PlayState(Context, Context.CreateMatch()));
    }

    public sealed class PlayState : GameState
    {
        public const float Duration = 30;

        public PlayState(GameFlowContext context, CoinRushMatch match) : base(context) => Match = match;

        public CoinRushMatch Match { get; }
        public float TimeLeft => Math.Max(0, Duration - Match.Time);

        protected override void OnEnter() => Match.Start();

        protected override void Tick(float deltaTime)
        {
            Match.Tick(deltaTime);

            if (Match.Time < Duration)
                return;

            var coins = Match.Coins.CoinsOf(Match.Player);
            var isBest = coins > Context.BestScore;
            if (isBest)
                Context.BestScore = coins;

            Go(new ResultState(Context, coins, isBest));
        }
    }

    public sealed class ResultState : GameState
    {
        public ResultState(GameFlowContext context, int coins, bool isBest) : base(context)
        {
            Coins = coins;
            IsBest = isBest;
        }

        public int Coins { get; }
        public bool IsBest { get; }

        public void PlayAgain() => Go(new PlayState(Context, Context.CreateMatch()));

        public void BackToMenu() => Go(new MenuState(Context));
    }
}
```

Things to notice:
- **States move on with `Go(...)`.** `Go` does nothing if the state is no longer the current one. If a player
  double-clicks "Play again", the second click hits a state that's already gone and is safely ignored.
- **The match is ticked by `PlayState`**, not by a `MonoBehaviour`. When the game leaves `PlayState`, the match
  stops ticking. Nobody has to remember to stop it.
- **`PlayState.Tick` checks the time after ticking the match** and only then changes state. Changing state in the
  middle of a tick (for example, from an event listener) would end the match while the world is half-updated.
- **The match starts in `OnEnter`**, not in the constructor and not in a screen. The constructor only stores what it
  was given; a state does its work only while it is active.
- **The best score is a game rule,** so it lives in the domain (`GameFlowContext.BestScore`), not in the results screen.

### 7.2 Test the flow

Because the flow is domain code, you can test all of it without a scene. Add this to the tests:

`Game/Tests/GameFlowTests.cs`

```csharp
using CoinRush.Coins;
using CoinRush.Flow;
using CoinRush.Movement;
using Mosaic.Core;
using NUnit.Framework;

namespace CoinRush.Tests
{
    public sealed class GameFlowTests
    {
        private StateMachine<GameState> _states;

        [SetUp]
        public void SetUp()
        {
            _states = new StateMachine<GameState>();
            var player = new EntityDefinition(1, "Player", new IEntityPart[] { new MoverDefinition(), new WalletDefinition() });
            var coin = new EntityDefinition(2, "Coin", new IEntityPart[] { new CoinDefinition() });
            _states.Change(new MenuState(new GameFlowContext(_states, player, coin)));
        }

        private void Tick(float seconds)
        {
            for (var time = 0f; time < seconds; time += 0.1f)
                _states.Tick(0.1f);
        }

        [Test]
        public void A_match_ends_with_results_after_its_duration()
        {
            ((MenuState)_states.Current).Play();
            Assert.That(_states.Current, Is.TypeOf<PlayState>());

            Tick(PlayState.Duration + 0.5f);

            Assert.That(_states.Current, Is.TypeOf<ResultState>());
        }

        [Test]
        public void Clicking_play_twice_starts_one_match()
        {
            var menu = (MenuState)_states.Current;
            menu.Play();
            var play = _states.Current;

            menu.Play();

            Assert.That(_states.Current, Is.SameAs(play));
        }
    }
}
```

**Checkpoint.** The whole flow, menu to results, is tested in milliseconds.

### 7.3 Screen views

Each screen has a prefab with a small `MonoBehaviour` that only holds references to its UI elements. No logic.

`Unity/Screens/ScreenViews.cs`

```csharp
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CoinRush.View
{
    public sealed class MenuScreenView : MonoBehaviour
    {
        public Button Play;
        public TMP_Text Best;
    }

    public sealed class PlayScreenView : MonoBehaviour
    {
        public TMP_Text Score;
        public TMP_Text Timer;
    }

    public sealed class ResultScreenView : MonoBehaviour
    {
        public TMP_Text Coins;
        public Button PlayAgain;
        public Button Menu;
    }

    [Serializable]
    public sealed class ScreenPrefabs
    {
        public MenuScreenView Menu;
        public PlayScreenView Play;
        public ResultScreenView Result;
    }
}
```

Create three UI prefabs (a full-screen `RectTransform` with the buttons and texts), add the matching view component to
each root and fill in its fields.

> Unity requires each `MonoBehaviour` to live in a file with the same name, so in your project split this file into
> `MenuScreenView.cs`, `PlayScreenView.cs` and `ResultScreenView.cs`. They are shown together here to save space.

### 7.4 Screens

A screen is a plain class derived from `UiScreen`. `Show(prefab)` creates its view; the view is destroyed
automatically when the screen goes away.

`Unity/Screens/Screens.cs`

```csharp
using CoinRush.Flow;
using Mosaic.Ui;

namespace CoinRush.View
{
    public sealed class MenuScreen : UiScreen
    {
        private readonly MenuState _state;
        private readonly ScreenPrefabs _prefabs;

        public MenuScreen(MenuState state, ScreenPrefabs prefabs)
        {
            _state = state;
            _prefabs = prefabs;
        }

        protected override void Enter()
        {
            var view = Show(_prefabs.Menu);
            view.Best.text = $"Best: {_state.BestScore}";
            view.Play.onClick.AddListener(_state.Play);
        }
    }

    public sealed class PlayScreen : UiScreen
    {
        private readonly PlayState _state;
        private readonly ScreenPrefabs _prefabs;
        private readonly ContentConfig _content;

        private PlayScreenView _view;
        private MatchView _match;
        private PlayerInput _input;

        public PlayScreen(PlayState state, ScreenPrefabs prefabs, ContentConfig content)
        {
            _state = state;
            _prefabs = prefabs;
            _content = content;
        }

        protected override void Enter()
        {
            _view = Show(_prefabs.Play);
            _match = new MatchView(_state.Match, _content, _view.Score);
            _input = new PlayerInput(_state.Match);
        }

        protected override void Tick(float deltaTime) => _input.Tick();

        protected override void LateTick(float deltaTime)
        {
            _match.LateTick();
            _view.Timer.text = $"{_state.TimeLeft:0}";
        }

        protected override void Exit() => _match.Dispose();
    }

    public sealed class ResultScreen : UiScreen
    {
        private readonly ResultState _state;
        private readonly ScreenPrefabs _prefabs;

        public ResultScreen(ResultState state, ScreenPrefabs prefabs)
        {
            _state = state;
            _prefabs = prefabs;
        }

        protected override void Enter()
        {
            var view = Show(_prefabs.Result);
            view.Coins.text = _state.IsBest ? $"New best: {_state.Coins}!" : $"Coins: {_state.Coins}";
            view.PlayAgain.onClick.AddListener(_state.PlayAgain);
            view.Menu.onClick.AddListener(_state.BackToMenu);
        }
    }
}
```

Look at what the screens do and don't do:
- Buttons call **methods of the domain state** (`_state.Play`, `_state.PlayAgain`). The screen never creates a match
  or picks the next state.
- `PlayScreen` reuses `MatchView` and `PlayerInput` from step 6 unchanged. Moving from a quick prototype to a
  proper flow didn't touch the game logic or the views.
- What `Enter` creates, `Exit` disposes.

### 7.5 The bootstrap

Replace `QuickStart` with the real entry point of the game.

`Unity/GameBootstrap.cs`

```csharp
using CoinRush.Flow;
using Mosaic.Core;
using Mosaic.Infrastructure;
using Mosaic.Ui;
using UnityEngine;

namespace CoinRush.View
{
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private ContentConfig _content;
        [SerializeField] private UiRootView _uiRoot;
        [SerializeField] private ScreenPrefabs _screenPrefabs;

        private StateMachine<GameState> _states;
        private ScreenStateMachine _screens;
        private UiLayers _layers;

        private void Awake()
        {
            _layers = new UiLayers(_uiRoot);
            _states = new StateMachine<GameState>();
            var context = new GameFlowContext(_states, _content.Player.ToDefinition(), _content.Coin.ToDefinition(), new UnityLog());

            _screens = new ScreenStateMachine(_layers);
            _screens.Map<MenuState>(state => new MenuScreen(state, _screenPrefabs));
            _screens.Map<PlayState>(state => new PlayScreen(state, _screenPrefabs, _content));
            _screens.Map<ResultState>(state => new ResultScreen(state, _screenPrefabs));
            _states.Changed += _screens.FollowDomain;

            _states.Change(new MenuState(context));
        }

        private void Update()
        {
            _layers.Tick(Time.deltaTime);    // Escape, hotkeys, windows
            _screens.Tick(Time.deltaTime);   // input of the current screen becomes commands
            _states.Tick(Time.deltaTime);    // the game itself
        }

        private void LateUpdate() => _screens.LateTick(Time.deltaTime);

        private void OnDestroy()
        {
            _states?.Stop();
            _screens?.Dispose();
            _layers?.Dispose();
        }
    }
}
```

In the scene: delete `QuickStart` and the score text, add an empty GameObject with `GameBootstrap`, drag in the
content asset, the screen prefabs, and the **UiRoot** prefab from **Packages > Mosaic Framework > Runtime > Unity >
Prefabs**. `UiRoot` holds the canvas and the layers that screens, windows and popups are placed on.

How it fits together:
1. `Awake` builds the UI layers, the domain state machine and the screen state machine.
2. `Map<TState>` tells the screen machine which screen to show for each domain state.
3. `_states.Changed += _screens.FollowDomain` connects them: every time the domain changes state, the screen
   machine closes all windows and switches to the matching screen.
4. `_states.Change(new MenuState(context))` starts the game in the menu.
5. `Update` ticks UI input first, then the current screen (which turns keys into commands), then the game.

**Checkpoint.** Press Play: menu, 30 seconds of collecting coins, results, play again. That's a complete game.

---

## Step 8. Seeing inside the game

When something behaves strangely, you want to see what the game is doing, not guess. Mosaic has a window for that.

### 8.1 Register the world

The debug window shows worlds that the game registers. Add this to `PlayScreen`:

```csharp
private IDisposable _debugWorld;

protected override void Enter()
{
    // ...as before...
    _debugWorld = DebugWorlds.Register("Coin Rush", _state.Match.Context);
}

protected override void Exit()
{
    _debugWorld.Dispose();
    _match.Dispose();
}
```

(Add `using System;` at the top for `IDisposable`.)

### 8.2 Look around

Open **Window > Mosaic > Simulation** and press Play.

- **World tab.** Every entity in the match and its components with their current values. Select the player and
  watch `Position` change as you move; pick up a coin and watch `Wallet.Coins` go up. Type `Coin` in the filter to
  see only coins.
- **Trace tab.** Press **Record**. Every command and system tick that did something appears as a tree:

```
MoveCommand(Mover=Entity(0:0), Direction=<1, 0>) → Ok
Tick CoinPickupSystem
  + CoinCollected(Collector=Entity(0:0), Value=1, Total=3)
  + EntityDestroyed(Entity=Entity(7:0))
```

  This reads as: the player started moving right; later, during the pickup system's tick, a coin was collected
  (that's the `+` line, an event) and the coin entity was destroyed. Press **Pause** to stop the list and read it,
  and use the filter to find one entity or one event.

When you are not sure why something happened, start here, before reaching for breakpoints.

### 8.3 Warnings worth reading

Mosaic prints a few warnings in the console in the editor. They always point to a real bug:

- **"... was read while an Add/Remove for it is still buffered"**: code changed a component and read it back in
  the same action, so it saw the old value. See *Design Guide 3.4*.
- **"EventBus: subscriber ... failed on ..."**: an event listener threw an exception. The other listeners still ran,
  but this one didn't finish its job.

In tests (`strict: true`) both of these fail the test instead of printing a warning.

---

## What's next

You now know every part of a Mosaic game. A few ideas to practice, roughly from easy to harder:

1. **Keep the player on the field.** Clamp the position in `MovementOperations.Advance` to the field size. Write a
   test first: "a player walking left for 10 seconds stays inside the field".
2. **Spawn coins over time.** A `CoinSpawnSystem` with a timer that calls `FieldOperations.ScatterCoins(1)` every
   two seconds. Remember to add it to the tick list.
3. **A pause window.** A `UiWindow` with `HoldsPause => true`, opened with Escape from `PlayScreen.HandleEscape`.
   `PlayState` needs a `HoldPause()` method and should skip `Match.Tick` while paused. See *Design Guide 6.5*.
4. **Enemies that also collect coins.** Give an enemy a `Mover` and a `Wallet` in its config, and a system that sends
   it `MoveCommand`s toward the nearest coin. You will find that coins need no changes at all.
5. **Save the best score.** Use `FileDataStorage` from `Mosaic.Infrastructure` behind the `IDataStorage`
   interface, and let `GameFlowContext` load and save it. The domain only sees the interface. Give the saved class a
   `Version` field and load it through `SaveMigrations` from the start, so the format can change later.

Where to read more:
- [Design Guide](DesignGuide.md): everyday decisions with "don't / what goes wrong / do / why" examples. Read the
  cheat sheet at the top now, and the rest when a situation comes up.
- [Architecture](Architecture.md): the full description of roles, the life of an action and the visibility rules.

---

## FAQ

**Why can't I just put the logic in `MonoBehaviour.Update` like in other projects?**
You can, but you lose what Mosaic gives you: tests that run in milliseconds, replays, pause that really stops the
game, and logic that doesn't depend on which object Unity updates first. The first steps feel slower; by the
time the game has a dozen features, it's much faster.

**Do I need a command for everything?**
For everything that comes **from outside** the simulation: player input, UI buttons that change the game, AI
decisions, cheats. Things that happen **inside** (a system moving entities, a reaction to an event) call Operations
directly.

**When do I use an event and when a Query?**
Query to *read the current state* (where is the player, how many coins). Event to *learn that something happened*
(a coin was collected). The UI usually uses both: Queries every frame for things on screen all the time, events for
popups, sounds and effects.

**Why is everything `internal`?**
So the only way to change the game from outside is through commands. The compiler then guarantees that the UI
can't cheat its way around the rules, which is very hard to keep by discipline alone.

**My system doesn't run. Why?**
Most likely it isn't in the match's `_systems` list. Creating a system is not enough; see step 4.2.

**My command throws "has no handler".**
The handler wasn't created in the match constructor. `new MyCommandHandler(Context, ...)` is enough to register it.

**I changed a component and reading it right after gives the old value.**
Adding and removing components on an existing entity is applied at the end of the action. Use the value you already
have, or react to the event. See *Design Guide 3.4*.

**Can two features share a component?**
Many features can **read** any component. Only one feature **changes** it: the one whose Operations own it. If
another feature needs to change it, it calls the owner's Operations, like coins calling `MovementOperations.Place`.
