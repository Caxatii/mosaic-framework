# Mosaic Architecture

This document is for people writing a game on Mosaic. It explains **where code goes and why**: what each role may do,
how an action runs, what the `World` visibility rules are and how to debug the simulation. Examples come from a
top-down arena shooter (combat, inventory, loot); your features will differ, the roles stay the same.

## 1. Layers

The package:

```
Runtime/Core    Mosaic.Core          World, command and event buses, Simulation, state machines,
                                     settings, match recording, tracing. No Unity.
Runtime/Unity   Mosaic.Unity         UI shell (screens, windows, popups), entity views and pooling,
                                     file storage, UnityLog, DebugWorlds.
Editor          Mosaic.Unity.Editor  content inspectors, Window > Mosaic > Simulation.
Tests           Mosaic.Core.Tests    core tests.
```

Recommended game layout:

```
Game            domain: Model + Rules + GameFlow     asmdef with "noEngineReferences": true
Presentation    screens, presenters, config assets   sees the domain only through Queries and commands
Boot            one MonoBehaviour with Update        the application composition root
```

The main rule: **the domain does not know about Unity.** The whole game logic then builds and runs its tests with
regular .NET in a fraction of a second, the same way a server would build it. Inside Unity this is enforced by
`noEngineReferences` in the asmdef; outside Unity, by a `dotnet test` project that compiles the domain folder.

## 2. Roles

| Role | Purpose | May | May not |
|---|---|---|---|
| **Model** (`IModel`) | data of one aspect of an entity (`Health`, `Weapon`) and its invariants | change its own fields through `internal` methods | access `World`, buses or other components |
| **Definition** (`IEntityPart`) | content description of a model, creates it | `CreateModel()` | anything else |
| **Command** (`ICommand`) | intent from outside: player, AI, cheats, network | — | be anything but a `readonly struct` of primitives, enums, strings and structs of them |
| **CommandHandler** | validates an intent and calls Operations | read, call Evaluators and Operations, return `Rejected(reason)` | publish events, change models directly |
| **Operations** | **the only** code that changes models of its feature and spawns or destroys entities | change models of its feature, publish events about what it changed | change models of another feature (use that feature's Operations) |
| **Evaluator** | calculations over state (`IsAlive`, `FindNearestHostile`) | read | change anything, call Operations, publish, send commands |
| **Queries** | read access for the outside world (presentation, saves), returns snapshots | read | same as Evaluator |
| **System** (`EntitySystem<…>`) | time-based logic: timers, movement, hits | call Operations, send commands (AI) | publish events |
| **Reaction** | "event of feature A → change through Operations of feature B" | subscribe with `On<T>`, call Operations | publish events |
| **Event** (`IEvent`) | a fact that already happened | — | be anything but a `readonly struct` |

Why:
- **The one who made a change announces it.** Events are published by Operations, not by handlers or reactions;
  otherwise an event could be announced without the change behind it.
- **Reactions connect features without coupling them.** Combat knows nothing about loot: without the
  "death → drop loot" reaction it works the same, just without drops.
- **Dependencies are visible in constructors.** There is no service locator or DI container: everything a class needs
  comes in as a parameter, and the compiler checks the assembly order.

The `CommandHandler` and `Reaction` base classes give no access to the event bus. The other rules rely on `internal`
and reviews; it is worth locking them with architecture tests in your game that check the compiled IL.

## 3. Life of an action

An action is a command or one system tick. `Simulation` runs it like this:

1. The action runs in **deferred mode**: Add, Remove and Destroy on existing entities are buffered.
2. When the action ends, the buffer is applied and `EntitySpawned` / `EntityDestroyed` are published.
3. Events are delivered in order. The buffer is applied again after each subscriber, so the next subscriber sees
   the consequences of the previous one.
4. A command sent from inside (AI in a system, a reaction) runs immediately within the current action.

This is how it looks in the trace (**Window > Mosaic > Simulation**, Trace tab, Record button):

```
AttackCommand(Attacker=Entity(0:0), Direction=<1, 0>) → Ok
  + WeaponFired(Shooter=Entity(0:0), Projectile=Entity(41:0))
  + EntitySpawned(Entity=Entity(41:0), Kind=50)
Tick ProjectileSystem
  + DamageApplied(Target=Entity(7:0), Source=Entity(0:0), ...)
  + EntityDied(Entity=Entity(7:0), Killer=Entity(0:0))
  + EntityDestroyed(Entity=Entity(41:0))
  EntityDied → DropLootReaction
    + EntitySpawned(Entity=Entity(42:0), Kind=16)
```

A `+` line is a published event, `A → B` means subscriber B received event A. Ticks and subscribers that did nothing
are hidden; the Quiet ticks button shows them.

## 4. Visibility inside an action

The single most important thing to know about `World`:

| Done in the action | Who sees it before the action ends |
|---|---|
| `Spawn` and Add/Remove on the new entity | **only `Get`**: the entity is being built (`IsBuilding`) and Operations finish it. It appears in `Query` after the action |
| Add/Remove on an existing entity | **nobody**: everyone reads the old state until the action ends |
| `Destroy` | nobody: the entity stays alive until the action ends |

Reading a component that has a buffered Add or Remove is almost always a bug: the code expects the new state and sees
the old one. `World` detects such reads (`StaleReads`): `GameplayContext` logs a warning in the editor and development
builds, and throws in strict mode (`throwOnSubscriberException: true`, for tests). In release builds the check is off
and costs nothing.

Changing components during a `foreach` over `Query` or inside an `EntitySystem` is allowed only inside an action
(`Simulation.Run`, `Simulation.Tick`, command handling). Outside an action the iteration throws
`InvalidOperationException`.

## 5. Assembling the world and tick order

The domain composition root (for example, a match class) creates a `GameplayContext`, then Operations, Evaluators,
systems, handlers and reactions bottom-up along the dependency graph, and defines the tick order of systems.

Two approaches are fine, and moving between them is mechanical:

- **One assembly class**: everything in one constructor. Fine while there are fewer than about ten features and the
  assembly fits on a screen.
- **Feature modules** (`CombatFeature`, `InventoryFeature`): each assembles its feature and exposes its Queries and
  systems; the match class only lists the modules. Fits when the assembly has grown or different people own features.

Either way, three rules hold:
1. **The public face of the match stays the same** (`match.Combat`, `match.Inventory`, `match.Context`), so
   presentation and tests do not notice the switch.
2. **Tick order is defined in one place**, never a mix of an array and self-registering systems.
3. **Dependencies stay explicit in constructors.** A module is a grouping, not a service locator.

## 6. Adding a feature

Using loot as an example:

1. **Model**: the component (`LootDrop : IModel`) and its description (`LootDefinition : IEntityPart`,
   `[Serializable]` so it can be added to an `EntityConfig` asset from the inspector).
2. **Operations**, if the feature changes its models or spawns entities. Every change comes with an event.
3. **What triggers the feature**:
   - the player or AI asks → a command and a `CommandHandler`, rejection reasons as the feature's enum;
   - logic over time → an `EntitySystem<…>`;
   - a response to another feature's event → a `Reaction` (loot: `EntityDied` → drop items).
4. **Reading from outside**: Queries that return snapshots (`readonly struct`), not components.
5. **Assembly**: create it in the composition root and put its systems into the tick order.
6. **Test**: build the world without Unity, run the action through a command or `Simulation.Run`, check events or
   Queries.
7. **Presentation**: a presenter subscribes to events and reads Queries, input goes out as commands.
   Presentation cannot see Operations: they are `internal`.

## 7. Debugging

| Tool | Where | What it gives |
|---|---|---|
| Trace (`SimulationTrace`) | Window > Mosaic > Simulation > Trace | "what happened and why" tree, filter, pause, copy |
| World inspector | same window > World | all entities, their components and property values, filter by name or component |
| Stale reads | editor log | a warning with the component type and entity (see section 4) |
| Match recording (`CommandRecording`) | game code | reproduce a bug from a file, regression tests |

A world shows up in the window once the game registers it, for example on the gameplay screen:

```csharp
_debugWorld = DebugWorlds.Register("Match", context, kind => names[kind]); // Dispose when the match ends
```

The window enables tracing while Record is pressed. From code: `context.Trace = new SimulationTrace()`.

### Recording and replay

Only **external input** is recorded: commands sent from outside the simulation, their results and time steps.
Commands sent by AI and reactions are not recorded, since the simulation sends them again on replay. So everything
that changes a match from outside must go **through commands**: a direct call to Operations from presentation is
invisible to the recording.

```csharp
// recording
var recording = new CommandRecording();
context.Commands.Recording = recording;   // external commands are recorded automatically
// in the match tick loop: recording.RecordTick(deltaTime) before ticking systems
var text = ReplayFormat.Write(recording);
var fingerprint = WorldFingerprint.Of(context.World);

// replay: a fresh world from the same starting point
var divergence = ReplayFormat.Read(text).Play(newContext.Commands, match.Tick);
Assert.That(divergence, Is.Null, divergence?.ToString());
Assert.That(WorldFingerprint.Of(newContext.World), Is.EqualTo(fingerprint));
```

`Play` compares the result of every command and reports the first one that differs. `WorldFingerprint` compares the
final world, and `WorldFingerprint.Describe` dumps it as text for a diff.

Limits: a match repeats **within the same build and content**; time steps are recorded as they were.

## 8. Tests and performance

- Core tests run in the Unity Test Runner (EditMode) once the package is listed in `"testables"` in
  `Packages/manifest.json`.
- `WorldBenchmark` (`[Explicit]`) measures systems, queries and `Get` on 5,000 and 50,000 entities. Allocation
  checks only work outside Unity: in the editor the GC counter depends on the runtime.
- Components live in **sparse sets** (`ComponentStore<T>`): `Get<T>` and `Has<T>` are O(1).
- `foreach` over `Query<…>` and `EntitySystem<…>` ticks **do not allocate**: the smallest store drives the loop,
  the others are checked by index.
- Reference numbers on .NET (Release): a two-component `EntitySystem` takes about 60 µs per frame for 5,000 entities
  and about 320 µs for 50,000. Mono in the editor is several times slower.
- Components are classes, so cache use is worse than in DOTS. The target is thousands to tens of thousands of
  entities, not hundreds of thousands.
- Command and event buses do not allocate per message. Tracing and recording do, so keep them for debugging.
