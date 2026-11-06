# Mosaic Reference

Everything the package provides, grouped by area. For each type: what it is, when to use it, the members you will
actually call, and the things that trip people up.

This is a reference, not a tutorial. To learn Mosaic step by step, start with [Getting Started](GettingStarted.md).
For the ideas behind the design, see [Architecture](Architecture.md); for everyday decisions, the
[Design Guide](DesignGuide.md).

**Assemblies**

| Assembly | Unity? | Contains |
|---|---|---|
| `Mosaic.Core` | no | world, buses, simulation, state machines, settings, storage, logging, tracing, recording |
| `Mosaic.Unity` | yes | config assets, entity views, UI shell, file storage, Unity log |
| `Mosaic.Unity.Editor` | editor only | inspectors and the Simulation window |

**Contents**

1. [World and content](#1-world-and-content)
2. [Simulation, commands and events](#2-simulation-commands-and-events)
3. [State machines](#3-state-machines)
4. [Logging, storage and settings](#4-logging-storage-and-settings)
5. [Debugging and recording](#5-debugging-and-recording)
6. [Configs and entity views](#6-configs-and-entity-views)
7. [UI shell](#7-ui-shell)
8. [Editor tools](#8-editor-tools)

---

## 1. World and content

### `World`

Stores entities and their components. One world per match (or per test, or per replay); you get it from
`GameplayContext.World`.

| Member | What it does |
|---|---|
| `Spawn(EntityDefinition)` | creates an entity whose kind is the definition's `Id`, and adds the component of every part |
| `Spawn(int kind)` | creates an entity with only a `Kind` component; `kind` must not be 0 |
| `Get<T>(id)` | the component, or `null` if the entity has none or is not alive |
| `Has<T>(id)` | `Get<T>(id) != null` |
| `GetAll(id)` | all components of an entity (inspectors, debugging) |
| `Add(id, component)` / `Remove<T>(id)` | adds or removes a component; buffered inside an action (see below) |
| `Destroy(id)` | the entity becomes unavailable at once; its slot is released when the action ends |
| `Query<T>()`, `Query<T1, T2>()`, `Query<T1, T2, T3>()` | all entities that have these components, without allocations |
| `IsAlive(id)` | the id points to a living entity of the right generation |
| `TryGetEntity(id, out entity)` / `GetEntity(id)` | turns an id into an `Entity` if it is alive |
| `IsBuilding(id)` | the entity was spawned in the current action and is still being set up |
| `Entities` | ids of all living entities (inspectors only; rules use `Query`) |
| `Count` | number of living entities |
| `StaleReads`, `StaleRead` | the stale-read check and its warning event (see `StaleReadCheck`) |
| `Defer()`, `Flush()`, `DrainSpawned()`, `DrainDestroyed()` | used by `Simulation`; you rarely call them yourself. `Defer()` returns a `DeferScope`: dispose it (`using`) to end deferred mode |

**Visibility inside an action** (a command or a system tick, see `Simulation`):

| Done in the action | Visible to |
|---|---|
| `Spawn` and Add/Remove on the new entity | only `Get` on that entity; queries see it after the action |
| Add/Remove on an existing entity | nobody until the action ends |
| `Destroy` | everyone at once: the entity is gone |

**Watch out**
- Change the world inside an action (`Simulation.Run`, a system tick, a command handler). Outside an action changes
  apply immediately and iterating while changing throws.
- Only Operations of the owning feature should call `Add`, `Remove`, `Spawn` and `Destroy`.

```csharp
var enemy = world.Spawn(definitions[EnemyId]);
movementOperations.Place(enemy, spawnPoint);

foreach (var entity in world.Query<Health, Position>())
    if (entity.Get<Health>().Current <= 0) ...
```

### `Entity`

A small struct that pairs an `EntityId` with its `World`, for convenient calls inside one method:
`entity.Get<T>()`, `Has<T>()`, `Add(...)`, `Remove<T>()`, `Destroy()`, `IsAlive`, `IsBuilding`.

**Watch out**
- Unlike `World.Get<T>(id)`, every `Entity` method except `IsAlive` and `IsBuilding` **throws**
  `EntityNotAliveException` if the entity is not alive. That is on purpose: using a destroyed entity is a bug.
- Don't keep an `Entity` in fields, components, commands or events. Keep the `EntityId`.

### `EntityId`

The identity of an entity: `Index` (slot) and `Generation`. Plain data, safe to store anywhere, to put into commands
and events and to save. When a slot is reused the generation changes, so an old id never matches the new entity.

```csharp
if (world.TryGetEntity(homing.Target, out var target)) ... // the target may be gone by now
```

### `EntityNotAliveException`

Thrown when code uses an `Entity` (or calls `World.Add`, `Remove`, `Destroy`) for an entity that is not alive,
including one destroyed earlier in the same action. Treat it as a bug report with a stack trace, not as something to
catch.

### `IModel`

Marker interface for components. A component is a class with the data of one aspect of an entity, which protects
its own rules: fields with private setters, changes through `internal` methods.

```csharp
public sealed class Health : IModel
{
    public Health(float max) => Max = Current = max;

    public float Max { get; }
    public float Current { get; private set; }

    internal float TakeDamage(float amount) { ... }
}
```

Components must not reference `World`, buses, other components or other entities (except by `EntityId`).

### `Kind`

A component every entity has: the number of its config (`Kind.Id`), set by `World.Spawn`. Presentation uses it to
pick the prefab; saves can use it to recreate entities. Negative kinds are free for service entities that have no
config asset (for example a "wave state" entity).

### `IEntityPart`

The content-side description of one component: a `[Serializable]` class with public fields that designers edit,
and one method, `CreateModel()`, that creates the component when an entity spawns.

```csharp
[Serializable]
public sealed class HealthDefinition : IEntityPart
{
    public float Max = 30;
    public IModel CreateModel() => new Health(Max);
}
```

|  | `IEntityPart` | `IModel` |
|---|---|---|
| Lives in | a config asset | the world, on one entity |
| Changed by | designers in the inspector | Operations during play |
| Instances | one per kind of entity, shared | one per entity |
| Shape | public fields (Unity serializes them) | private setters and `internal` methods |

**Watch out.** Parts are shared by every entity of that kind. Never change a part at runtime; copy the value into the
component if it must change. Not every component needs a part: data known only at spawn time (a position, an owner)
is added by the code that spawns the entity.

### `EntityDefinition`

The domain-side description of one kind of entity: `Id` (its kind number), `Name` (for logs and tools) and `Parts`.
In the game it comes from an `EntityConfig` asset (`config.ToDefinition()`); in tests you create it in code.

```csharp
var coin = new EntityDefinition(2, "Coin", new IEntityPart[] { new CoinDefinition { Value = 5 } });
var weapon = definition.Get<WeaponDefinition>();   // a part of a given type, or null
```

### `EntityRef`

A serializable reference from one piece of content to another entity kind, by number: a weapon to its projectile,
a loot table to its items. In the inspector it is picked as an asset (see `EntityRefDrawer`). `IsSet` is false when
nothing is selected.

### `EntityCatalog`

All entity definitions of a match, by number. Its constructor checks that numbers are unique and that every
`EntityRef` in every part points to a defined entity, so broken content fails when the match is built, not when the
broken weapon is first fired.

```csharp
var catalog = new EntityCatalog(definitions);
var projectile = world.Spawn(catalog[weapon.Projectile]);
```

### `ContentRefs`

`ContentRefs.Collect<T>(root, path)` finds every value of type `T` (for example `EntityRef` or your own reference
struct) inside content objects: public fields, nested objects and lists. Returns each one with a readable path
(`Rifle.Parts[2].Projectile`). Use it to validate your own reference types at startup, the way `EntityCatalog`
validates `EntityRef`.

### `EntityQuery<…>` and `QueryEnumerator`

The return types of `World.Query`. Use them with `foreach`; they don't allocate. They also implement
`IEnumerable<Entity>`, so LINQ works, but with allocations (fine for tests and tools). Entities destroyed earlier in
the action are skipped.

### `StaleReadCheck`

What `World` does when code reads a component that has a buffered Add or Remove in the current action, which
means the code sees the old state:

| Value | Behaviour | Used by |
|---|---|---|
| `Off` | no check, no cost | release builds |
| `Warn` | raises `World.StaleRead`; `GameplayContext` logs a warning | editor and development builds |
| `Throw` | throws `InvalidOperationException` | tests (`throwOnSubscriberException: true`) |

---

## 2. Simulation, commands and events

### `GameplayContext`

The shared objects of one world, created together: `World`, `EventBus` (also as `Events`), `Simulation`,
`Commands` and `Log`. The composition root creates one and passes it to handlers, systems and reactions.

```csharp
var context = new GameplayContext(log, throwOnSubscriberException: strictForTests);
```

| Member | What it is |
|---|---|
| `World`, `Simulation`, `Commands`, `Events` / `EventBus`, `Log` | the parts of this world |
| `Trace` | set to a `SimulationTrace` to record what happens; `null` (default) costs nothing |

`throwOnSubscriberException: true` is **strict mode** for tests: an exception in an event subscriber and a stale
read throw instead of being logged. Without it, stale reads are reported as warnings in the editor and development
builds and not checked in release.

### `Simulation`

Runs **actions**. Every command and every system tick is one action:

1. the action runs with the world in deferred mode;
2. the world's buffer is applied, `EntitySpawned` and `EntityDestroyed` are published;
3. queued events are delivered in order; the buffer is applied again after each subscriber.

An action started inside another one (a command sent by AI from a system) runs immediately as part of the outer one.

| Member | Use |
|---|---|
| `Run(Action)` | setup and one-off changes outside commands and systems (spawning the level, test setup) |
| `Tick(ISystem, deltaTime)` | ticks one system as one action; the composition root calls it for each system in order |
| `IsRunning` | an action is in progress (a command sent now comes from inside the simulation) |
| `World`, `Events`, `Trace` | the world, event bus and trace of this simulation |

```csharp
context.Simulation.Run(() => field.SpawnPlayer());

foreach (var system in _systems)
    context.Simulation.Tick(system, deltaTime);
```

**Watch out.** Calling `system.Tick(deltaTime)` directly skips all of the above: changes are not buffered and events
are delivered late. Always go through `Simulation.Tick`.

### `ISystem` and `EntitySystem<…>`

`ISystem` is anything with `Tick(float deltaTime)`. `EntitySystem<T>`, `EntitySystem<T1, T2>` and
`EntitySystem<T1, T2, T3>` are base classes that call your `Tick` for every entity having those components, without
allocations and without looking the components up again.

```csharp
internal sealed class WeaponSystem : EntitySystem<Weapon>
{
    private readonly WeaponOperations _operations;

    public WeaponSystem(GameplayContext context, WeaponOperations operations) : base(context) => _operations = operations;

    protected override void Tick(Entity entity, Weapon weapon, float deltaTime) =>
        _operations.Advance(entity, deltaTime);
}
```

**Watch out**
- A system runs only if the composition root adds it to the tick list. Creating it is not enough.
- Implement `ISystem` directly only for systems that don't loop over entities (a wave timer, a spawner).

### `ICommand` and `IEntityCommand`

A command is a request from outside the simulation: player input, a UI button, an AI decision, a cheat, a network
message. It must be a `readonly struct` with only primitives, enums, strings, `EntityId` and structs of those, so
that it doesn't allocate and can be recorded.

`IEntityCommand` adds `EntityId Entity { get; }`: the entity the command is for. If that entity is not alive, the
command is rejected with `CommandError.EntityNotFound` and your handler is not called. Implement the property
explicitly and keep a meaningful public name:

```csharp
public readonly struct AttackCommand : IEntityCommand
{
    EntityId IEntityCommand.Entity => Attacker;

    public AttackCommand(EntityId attacker, Vector2 direction) { Attacker = attacker; Direction = direction; }

    public EntityId Attacker { get; }
    public Vector2 Direction { get; }
}
```

### `CommandBus` (`ICommandBus`)

Routes each command type to its one handler and runs it as an action. Available as `GameplayContext.Commands`.

| Member | Use |
|---|---|
| `Send<T>(command)` | sends a command; returns its `CommandResult`. Throws if no handler is registered for `T` |
| `Handles(Type)` | whether a handler exists (tests) |
| `Verbose` | log every command and its result |
| `Recording` | set to a `CommandRecording` to record external commands (see section 5) |
| `SendBoxed(ICommand)` | sends a command whose type is known only at runtime (replays, tools); slower |

There is no public `Register`: handlers register themselves through `CommandHandler.On`.

### `CommandResult` and `CommandError`

The answer to a command: `IsOk`, or rejected with a `Reason` (an enum value of the feature). `Rejected` caches the
boxed enum, so rejecting every frame (shooting while reloading) does not allocate.

```csharp
return CommandResult.Rejected(AttackError.Reloading);

var result = commands.Send(new AttackCommand(player, aim));
if (result.Reason is AttackError.NoAmmo)
    hud.FlashAmmo();
```

`CommandError.EntityNotFound` is the one reason defined by Mosaic itself.

### `CommandHandler`

Base class for a feature's command handlers. Register each command type with `On` in the constructor; the handler
checks the request and calls Operations.

| Member | Use |
|---|---|
| `On<T>(Func<T, CommandResult>)` | handle a command that is not about one entity |
| `On<T>(Func<Entity, T, CommandResult>)` | handle an `IEntityCommand`; the entity is looked up and checked for you |
| `World` | the world, for reading |

Has no access to the event bus: events are published by Operations. Each command type can have only one handler;
registering a second throws.

### `IEvent`, `EventBus` (`IEventBus`)

An event is a fact that already happened: a `readonly struct` named in the past tense, with ids and the values of
that moment. The bus queues events and delivers them after the current action, in publish order; events published
by subscribers go to the end of the queue. Publishing and delivering don't allocate.

| Member | Use |
|---|---|
| `Publish<T>(event)` | queue an event (Operations only) |
| `Subscribe<T>(handler)` | returns an `IDisposable`; dispose it when the subscriber goes away |
| `ThrowOnSubscriberException` | rethrow exceptions from subscribers (strict mode); otherwise they are logged and the other subscribers still run |
| `MaxEventsPerDispatch` | protection from endless chains of events: past the limit (10,000 per action) the queue is dropped and an error logged |
| `PendingCount`, `Dispatch` | used by `Simulation` |

```csharp
_subscription = events.Subscribe<DamageApplied>(e => ShowDamage(e.Target, e.Amount));
...
_subscription.Dispose();
```

**Watch out**
- A subscriber added during delivery receives only later events; one disposed during delivery is not called again.
- Events are delivered after the action, so a subscriber sees the world with the action's changes applied.

### `EntitySpawned` and `EntityDestroyed`

Published by `Simulation` for every entity, after the action that created or destroyed it. `EntitySpawned` carries
the entity's `Kind`, which presentation uses to pick a prefab. `ViewSpawner` listens to both.

### `Reaction`

Base class for "when this event happens in feature A, change something in feature B". Subscribe with `On<T>` in the
constructor; `Dispose` releases all subscriptions. Like handlers, reactions can't publish events.

```csharp
internal sealed class DropLootReaction : Reaction
{
    private readonly InventoryOperations _inventoryOperations;

    public DropLootReaction(GameplayContext context, InventoryOperations inventoryOperations) : base(context)
    {
        _inventoryOperations = inventoryOperations;
        On<EntityDied>(OnDied);
    }

    private void OnDied(EntityDied e)
    {
        if (World.Get<LootDrop>(e.Entity) is { } loot)
            _inventoryOperations.SpawnLoot(loot, World.Get<Position>(e.Entity).Value);
    }
}
```

---

## 3. State machines

### `StateMachine<TState>` and `IState`

A minimal state machine: it only switches states. `IState` has `Enter()`, `Exit()` and `Tick(deltaTime)`.

| Member | Use |
|---|---|
| `Change(next)` | exits the current state, enters `next`, raises `Changed` |
| `Current` | the active state |
| `Changed` | raised after a state was entered; the screen machine follows the domain through it |
| `Tick(deltaTime)` | ticks the current state |
| `Stop()` | exits the current state without entering a new one (shutdown) |

A `Change` requested while another change is in progress (a state that moves on in its `Enter`) runs right after
it, so `Changed` subscribers see the states in the order they happened.

### `FlowState<TSelf>`

Base class for the game's domain states: menu, loading, gameplay, results. Your game derives its own base once, adding
the shared context:

```csharp
public abstract class GameState : FlowState<GameState>
{
    protected GameState(GameFlowContext context) : base(context.States) => Context = context;
    protected GameFlowContext Context { get; }
}
```

| Member | Use |
|---|---|
| `Go(next)` | move to another state. **Does nothing if this state is no longer active**, so a late click on a screen that is fading out is safely ignored |
| `IsActive` | between `OnEnter` and `OnExit` |
| `OnEnter()`, `OnExit()`, `Tick(deltaTime)` | override to start and stop work and to run every frame |

**Watch out**
- Keep constructors to storing parameters; start work in `OnEnter` and release it in `OnExit`.
- Don't call `Go` from inside a simulation subscriber. Remember the fact and call `Go` in `Tick`, after the match
  has finished ticking.

The screen side of the same idea is `ScreenStateMachine` (section 7).

---

## 4. Logging, storage and settings

### `ILog`, `NullLog`, `UnityLog`

`ILog` has `Info`, `Warning` and `Error(message, exception)`. The domain logs only through it. `NullLog.Instance`
discards everything (the default when no log is passed); `UnityLog` (in `Mosaic.Unity`) writes to the Unity
console with a `[Mosaic]` prefix.

### `IDataStorage`, `InMemoryDataStorage`, `FileDataStorage`

Documents by key: `TryLoad<T>(key, out data)`, `Save<T>(key, data)`, `Delete(key)`. The domain decides **what** to
save and when; the implementation decides where and how.

| Implementation | Where | Notes |
|---|---|---|
| `InMemoryDataStorage` | memory | tests and headless runs; stores the object itself |
| `FileDataStorage(log, prefix = "mosaic_")` | `Application.persistentDataPath/{prefix}{key}.json` | uses `JsonUtility`, so saved types must be `[Serializable]` classes with public fields; read and write errors are logged, a failed read returns `false` |

**Watch out.** `JsonUtility` can't serialize `null` inside nested objects, properties or dictionaries. Keep save data
as plain serializable classes made for saving, and build them through Queries, not from components.

### `ISettings<T>` and `SettingsStore<T>`

Player settings with an edit buffer, ready for a settings window with "Apply", "Revert" and "Keep these display
settings?" behaviour.

Your settings class implements `ISettings<T>`: `Copy()`, `Normalize()` (clamp values into valid ranges) and
`Equals`. It must be `[Serializable]` for `FileDataStorage`.

| Member | Use |
|---|---|
| `Current` | the settings in effect |
| `Draft` | the copy the settings window edits |
| `IsDirty` | `Draft` differs from `Current` (enable the Apply button) |
| `Preview()` | normalize `Draft` and raise `Previewed` (live preview while editing) |
| `Apply()` | `Draft` becomes `Current`, is saved, `Applied` is raised |
| `Revert()` | discard `Draft` changes |
| `ResetToDefaults()` | `Draft` becomes a new default instance |
| `ApplyTrial()`, `KeepTrial()`, `RollbackTrial()`, `IsOnTrial` | apply without saving, then keep (save) or roll back. For "Keep this resolution? Reverting in 10 s" |
| `Applied`, `Previewed` | subscribe to apply settings to the game (volume, resolution) |

```csharp
[Serializable]
public sealed class SoundSettings : ISettings<SoundSettings>
{
    public const string Key = "sound";

    public float MasterVolume = 0.8f;

    public SoundSettings Copy() => (SoundSettings)MemberwiseClone();
    public void Normalize() => MasterVolume = Mathf.Clamp01(MasterVolume);
    public bool Equals(SoundSettings other) => other != null && Mathf.Approximately(MasterVolume, other.MasterVolume);
}

var sound = new SettingsStore<SoundSettings>(SoundSettings.Key, storage);
sound.Applied += s => AudioListener.volume = s.MasterVolume;

window.Volume.onValueChanged.AddListener(v => { sound.Draft.MasterVolume = v; sound.Preview(); });
window.Apply.onClick.AddListener(sound.Apply);
```

`ISettingsStore` is the non-generic part (`IsDirty`, `Apply`, `Revert` and the trial methods), so one settings
window can handle several stores at once.

---

## 5. Debugging and recording

### `SimulationTrace`, `TraceRecord`, `TraceLine`

A tree of what happened in the simulation: each command or system tick with the events it published, the
subscribers that reacted and what they did in turn. Off by default; enable it with
`context.Trace = new SimulationTrace(capacity)`. While on, it allocates strings for every action, so use it for
debugging only. The Simulation window turns it on and off with its Record button.

| Member | Use |
|---|---|
| `Recent` | the last `capacity` records (default 256), oldest first |
| `Recorded` | raised for each new record (stream it to a log or a file) |
| `IncludeQuiet` | also record ticks and subscribers that did nothing |
| `Clear()` | forget the recent records |

A `TraceRecord` has `Frame` (a running number of top-level actions), `Lines` (`TraceLine`: `Depth` and `Text`) and
`ToString()` with the indented tree:

```
AttackCommand(Attacker=Entity(0:0), Direction=<1, 0>) → Ok
  + WeaponFired(Shooter=Entity(0:0), Projectile=Entity(41:0))
  + EntitySpawned(Entity=Entity(41:0), Kind=50)
```

`TraceFormat.Of(value)` is the formatter behind these lines (`Type(Property=value, ...)`, culture-independent);
`TraceFormat.OwnerOf(delegate)` names the class a subscriber was written in, also for lambdas.

### `DebugWorlds`

The list of worlds that the Simulation window can show. Registering a world turns nothing on by itself.

```csharp
_debugWorld = DebugWorlds.Register("Match", context, kind => names[kind]);   // when the match is shown
_debugWorld.Dispose();                                                     // when it goes away
```

The optional third argument names entity kinds in the World tab ("Rifle" instead of "#11"). `All` and `Changed`
are for tools that list the worlds.

### `CommandRecording`

Records external input: commands sent from outside the simulation, their results, and time steps. Commands sent by
the simulation itself (AI from a system, reactions) are not recorded, because a replay produces them again.

| Member | Use |
|---|---|
| `CommandBus.Recording = recording` | start recording commands; set back to `null` to stop |
| `RecordTick(deltaTime)` | call in your tick loop before ticking systems (zero and negative steps are ignored) |
| `RecordCommand(command, result)` | record a command yourself (for input that doesn't go through this bus, for example from the network) |
| `Entries`, `TickCount` | what was recorded |
| `Header` | free key-value metadata (game version, difficulty, seed) |
| `Play(bus, tick)` | replay into a fresh world: commands through `bus`, steps through `tick`. Returns the first `ReplayDivergence`, or `null` if every command got the same result |

```csharp
var divergence = ReplayFormat.Read(text).Play(match.Context.Commands, match.Tick);
Assert.That(divergence, Is.Null, divergence?.ToString());
```

A `ReplayDivergence` says where the replay went another way: `Entry` (index), `Tick` (steps before it), `Command`,
`Expected` and `Actual` results.

**Watch out.** A match repeats only from the same starting point (content, seed, saved progress) in the same build.
Anything that changes the match without a command (Unity's `Time` or `Random` in rules, presentation calling
Operations) makes the replay diverge.

### `ReplayFormat`

Turns a `CommandRecording` into text and back: `Write(recording)`, `Read(text)`. One line per entry, floats stored
as bits so nothing is lost, readable and diffable. `CanWrite(commandType, out problem)` tells whether a command type
can be recorded; a good architecture test is "every command type of the game can be written".

### `WorldFingerprint`

`Of(world)` is a short hash of the whole world; `Describe(world)` is the text it is computed from: every entity with
the public properties of its components. Compare fingerprints to check that a replay ended in the same state, and
diff the descriptions to see where it went wrong.

---

## 6. Configs and entity views

### `NumberedConfig`

Base class for config assets that need a stable number: entity configs, and your own (buffs, abilities). `Id` is
assigned automatically by the editor when the asset is created (see `EntityIdAllocator`, section 8), never changes
when the asset is renamed or moved, and is never reused. Saves and replays can refer to it safely.

### `EntityConfig`

**Create > Mosaic > Entity.** The asset that describes one kind of entity:

| Field | What it is |
|---|---|
| `Id` | the kind number (inherited from `NumberedConfig`) |
| `Prefab` | what the entity looks like; its root must have an `EntityView` |
| `Parts` | `[SerializeReference]` list of `IEntityPart`; add parts with the **Add to Parts** button |

`ToDefinition()` turns it into an `EntityDefinition` for the domain; `Ref` gives an `EntityRef` to it.

### `EntityView`

The component on the root of an entity's prefab that connects the GameObject to an entity. It has no logic and no
subscriptions; `Id` is the entity it currently shows. Views are pooled and reused for other entities of the same kind.

`TryGet<T>(out part)` finds a component anywhere in the prefab (including inactive children) and caches it. Presenters
use it to find "their" part: a health bar presenter looks for `HealthBarView`, an aura presenter for `AuraView`.

### `ViewSpawner` and `EntityViews`

`ViewSpawner` keeps a view for every entity that should be visible: it listens to `EntitySpawned` and
`EntityDestroyed`, takes prefabs from a pool and returns them when entities go away. You never instantiate entity views
yourself.

```csharp
_spawner = new ViewSpawner(context.World, context.Events,
    kind => configs[kind].Prefab.GetComponent<EntityView>(),   // prefab per kind, null = invisible kind
    id => spatial.PositionOf(id) != null,                      // optional: is this entity visible right now
    _pool);

_spawner.Views.Created += OnViewCreated;   // presenters subscribe first...
_spawner.CatchUp();                        // ...then views for entities that already exist are created
```

| Member | Use |
|---|---|
| `CatchUp()` | create views for entities that existed before the spawner. Call once, after presenters subscribed to `Views.Created` |
| `Refresh(id)` | re-check one entity after its visibility changed (an item was picked up into a backpack, a hidden unit appeared) |
| `Views` | the `EntityViews` registry |
| `Dispose()` | stop listening; views stay in the pool (dispose the pool to destroy them) |

`EntityViews` is the registry presenters read: `TryGet(id, out view)`, `TryGet<TPart>(id, out part)`, `All`, and the
events `Created(id, kind, view)` (set the initial state of a part here) and `Removed(id, view)`.

**Watch out**
- The spawner only reacts to spawning and destroying. If something else changes whether an entity should be
  visible, call `Refresh(id)`.
- Positions are not synced automatically. A presenter moves views in `LateTick` from Queries, which keeps the domain
  free of transforms.

### `ViewPool`

A pool of prefab instances: `Get(prefab)` returns an inactive instance reactivated or a new one, `Release(instance)`
deactivates it for reuse, `Dispose()` destroys everything it ever created. One pool per screen is typical; dispose it
together with the spawner.

### `VectorConversions`

`ToUnity()` turns a `System.Numerics.Vector2` from the domain into a `UnityEngine.Vector3` (with an optional `z`);
`ToNumerics()` goes the other way. The domain uses `System.Numerics` because it has no Unity.

---

## 7. UI shell

Four layers, from bottom to top:

| Layer | Class | How many | Lives until |
|---|---|---|---|
| Screen | `UiScreen` | exactly one, mirrors the domain state | the domain state changes |
| Windows | `UiWindow` | any number, one has focus | closed, or the screen changes |
| Popups | `UiPopup` | a stack, the top one is modal | closed, its owner window closes, or the screen changes |
| Overlays | `UiOverlay` | any number, no input | removed (they survive screen changes) |

Screens, windows, popups and overlays are plain C# classes with the logic. Each one creates its view from a prefab
with `Show(prefab)`; the prefab holds only references to UI elements.

### `UiLayers` and `UiRootView`

`UiLayers` owns the layers and routes input between them. Create one at startup from the **UiRoot** prefab of the
package (`Runtime/Unity/Prefabs/UiRoot.prefab`), whose `UiRootView` has a `RectTransform` per layer:
`ScreenLayer`, `WindowLayer`, `PopupLayer`, `OverlayLayer`.

| Member | Use |
|---|---|
| `Open(window)` | open a window if the current screen allows it (`false` otherwise); an open window just gets focus |
| `Toggle(window)`, `Close(window)`, `Focus(window)` | as named |
| `Bind(Key, window)` | a hotkey that toggles the window |
| `Show(popup, owner = null)`, `Close(popup)` | popups; a popup with an owner window closes when that window closes |
| `Add(overlay)`, `Remove(overlay)` | overlays |
| `Tick(deltaTime)` | routes Escape and hotkeys, ticks windows, popups and overlays; call it first in `Update` |
| `Focused` | the window with focus |
| `KeyboardCaptured` | a popup is open or the focused window captures the keyboard: gameplay should not read keys |
| `PointerCaptured` | a popup is open or some window captures the pointer: gameplay should ignore clicks |
| `IsBlocked` | a popup or a modal window is open |

**Escape** goes to the top popup, otherwise to the focused window (`HandleEscape`), otherwise to the screen.
**Hotkeys** are ignored while a popup is open, while another window that captures the keyboard has focus, and, for
windows that are not open, while the UI is blocked.

### `UiScreen` and `ScreenStateMachine`

A screen shows one domain state and passes the player's actions to it. `ScreenStateMachine` creates the matching
screen every time the domain state changes:

```csharp
_screens = new ScreenStateMachine(_layers);
_screens.Map<MenuState>(state => new MenuScreen(state, prefabs));
_screens.Map<PlayState>(state => new PlayScreen(state, prefabs, content));
_states.Changed += _screens.FollowDomain;
```

`ScreenStateMachine`: `Map<TState>(create)`, `FollowDomain(state)`, `Tick`, `LateTick`, `Current`, `Dispose`.
Switching screens closes all windows and popups. A screen may lag behind the domain but never runs ahead: if the
domain moves through several states while a screen is still leaving, only the last one gets a screen.

`UiScreen`, override what you need:

| Member | Use |
|---|---|
| `Show(prefab)` | create the screen's view; destroyed automatically when the screen exits |
| `Enter()`, `Exit()` | create and dispose presenters and subscriptions |
| `Leave(done)` | the domain moved on; call `done` when ready to switch (after an exit animation). Default: at once |
| `Tick(deltaTime)`, `LateTick(deltaTime)` | input every frame; reading Queries and moving views after the simulation |
| `Allows(window)` | which windows may open on top of this screen (default: all) |
| `HandleEscape()` | Escape not taken by a popup or window, for example open the pause window |
| `PauseRequested(bool)` | open windows started or stopped asking for a pause; pass it to the domain state |
| `Layers` | the `UiLayers`, to open windows |

### `UiWindow`

A window on top of the screen: inventory, map, pause menu, settings.

| Member | Use |
|---|---|
| `Opened()`, `Closed()` | create the view with `Show(prefab)` and connect it; disconnect on close |
| `Tick(deltaTime)` | per-frame logic while open |
| `HandleEscape()` | default: close the window |
| `Close()`, `IsOpen`, `HasFocus` | as named |
| `IsModal` | blocks hotkeys of other windows and captures the pointer |
| `HoldsPause` | the game pauses while this window is open (see `UiScreen.PauseRequested`) |
| `CapturesKeyboard` | while focused, gameplay and other hotkeys don't read keys (text input) |
| `CapturesPointer` | gameplay ignores clicks while it is open (default: same as `IsModal`) |
| `TakesFocusOnOpen` | focus the window when it opens (default `true`) |
| `KeepsViewWhenClosed` | hide the view on close and reuse it next time, for windows opened often. The presenter must handle getting the same view again |

Clicking a window focuses it and brings it to the front.

```csharp
public sealed class PauseWindow : UiWindow
{
    public override bool IsModal => true;
    public override bool HoldsPause => true;

    protected override void Opened()
    {
        var view = Show(_prefab);
        view.Resume.onClick.AddListener(Close);
        view.Menu.onClick.AddListener(_state.BackToMenu);
    }
}
```

### `UiPopup`, `ConfirmPopup`, `ConfirmPopupView`

A popup is modal: while one is open, hotkeys don't work, the keyboard and pointer are captured, and Escape goes to
it. Override `Opened`, `Tick`, `HandleEscape` (default: ignore) and `HoldsPause` as for windows.

`ConfirmPopup` is a ready yes/no question built on the package's **ConfirmPopup** prefab (`ConfirmPopupView`: title,
two buttons and their labels):

```csharp
Layers.Show(new ConfirmPopup(prefab, "Apply unsaved changes?", apply => { if (apply) Save(); }), owner: this);

// with a countdown that answers "no" when it runs out, and your own texts
Layers.Show(new ConfirmPopup(prefab, "Keep these display settings?", keep => ...,
    yes: "Keep", no: "Revert", timeout: 10,
    countdown: (title, seconds) => $"{title}\n(reverting in {seconds} s)"));
```

Escape answers "no".

### `UiOverlay`, `ToastOverlay`, `ToastView`

Overlays are on top of everything and take no input: notifications, a "saving..." badge. Override `Opened` and
`Tick`, call `Remove()` when done. `ToastOverlay(prefab, text, duration = 2)` shows a message from the package's
**Toast** prefab (`ToastView`) and removes itself.

```csharp
Layers.Add(new ToastOverlay(toastPrefab, "Game saved"));
```

Overlays are **not** closed when the screen changes, so a toast started on one screen finishes on the next.

---

## 8. Editor tools

### Simulation window (`SimulationWindow`)

**Window > Mosaic > Simulation.** Shows worlds registered with `DebugWorlds` while the game runs.

- **Trace** tab: **Record** turns on the `SimulationTrace` of the selected world; **Quiet ticks** also shows actions
  that did nothing; **Pause** freezes the list to read it; **Clear** and **Copy** as named; the search field filters
  records by text (an entity id, an event name).
- **World** tab: all living entities with their kind names; select one to see its components and the values of their
  public properties. The search field filters by name or component type.

### Entity ids (`EntityIdAllocator`)

Runs automatically when assets are imported. Every `NumberedConfig` without a number, or with a number already used
by another asset (a duplicated asset), gets the next free number. Existing numbers never change, and numbers of
deleted assets are not reused, because the last issued number is kept in `ProjectSettings/MosaicEntityIds.txt`.

**Commit that file.** Without it, two people creating assets at the same time could get the same numbers.

### Parts inspector (`PartsEditor`)

The inspector of every `NumberedConfig`. Shows `Id` read-only and adds an **Add to …** button under every
`[SerializeReference]` list (for `EntityConfig`, **Add to Parts**). The button opens a menu of all `[Serializable]`
classes implementing the list's element type, grouped by namespace; types already in the list are not offered again.

### Reference fields (`EntityRefDrawer`, `ConfigRefDrawer<TConfig>`)

`EntityRef` fields are drawn as an asset field: you pick an `EntityConfig`, the field stores its number. A number
that no longer points to an asset is shown in red.

For your own references to numbered configs (buffs, abilities), make a struct with an `int Id` field and a drawer of
one line:

```csharp
[Serializable]
public struct BuffRef { public int Id; }

[CustomPropertyDrawer(typeof(BuffRef))]
public sealed class BuffRefDrawer : ConfigRefDrawer<BuffConfig> { }
```

Validate such references at startup with `ContentRefs.Collect<BuffRef>` (section 1).
