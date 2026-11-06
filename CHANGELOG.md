# Changelog

All notable changes of the package. The format follows [Keep a Changelog](https://keepachangelog.com),
versions follow the rules in the [README](README.md#versions).

## [Unreleased]

## [0.2.0] - 2026-11-07

### Breaking

- **Behaviour change:** `Destroy` makes an entity unavailable at once, also inside an action: `IsAlive` is false,
  `Get` returns `null`, queries and systems skip it, commands to it are rejected, and using it throws
  `EntityNotAliveException`. The slot is still released and `EntityDestroyed` still published when the action ends.
  Before, a destroyed entity stayed fully usable until the end of the action, so it could be collected, hit or
  healed a second time in the same frame.
- Storage is split into a text store and a serializer. `IDataStorage` gains `Has(key)`; `InMemoryDataStorage` now
  stores JSON (`JsonFieldSerializer`, the rules of `JsonUtility`) instead of the object itself, so a loaded document
  is a copy and tests catch what `JsonUtility` would lose. `FileDataStorage` keeps its constructor.
- `ViewSpawner` and presenters take `IEventSource` (the subscribe-only side of `IEventBus`); pass an `EventFeed`.
- `World.DrainSpawned` and `DrainDestroyed` reuse their lists: read them before the next call.

### Added

- `Mosaic.Testing` assembly: `ArchitectureRules` (the role rules checked against compiled code, with `IlScanner`),
  `EventRecorder<T>`, `TestLog`, `TickLoop`.
- `Query<…>().Without<T>()` (up to two per query, no allocations) and `World.TryGetSingle<T>` / `Single<T>`.
- `GameRandom` (seeded SplitMix64 whose state can be saved) and `RandomStreams` (one named stream per feature,
  `Capture` and `Restore` for saves).
- `FileTextStore` writes atomically (temporary file, then replace; the previous version stays as `.bak`).
- `SaveMigrations<T>`: versioned documents and a chain of migration steps; `Load` reports `Missing`, `Loaded`,
  `Migrated`, `NewerVersion` or `Failed`.
- `ILoadOperation` for loading over several frames, polled by a game state: `LoadOperation.Completed`, `Failed`, `Of`,
  `Then`, `Select`, `All`, `ManualLoadOperation`; `UnityLoadOperation.LoadScene`, `LoadResource` and `From` in
  `Mosaic.Unity`.
- `EventFeed`: domain events for presentation, delivered once a frame after the simulation.
- `PartHandlers<THandler>` and `IPartHandler`: extension points by part type, one handler per type, and
  `Unhandled<TPart>` to fail the match build when content has a part nobody serves. `ContentRefs.FindAll<T>` finds
  objects of a class or interface type inside content.
- `GameClock`: pause holds, slow motion and timed hit stops (`SlowDown(0, seconds)`), game and real steps.
- `Countdown`: an immutable timer for models (cooldowns, delays, repeating intervals that keep the overshoot).
- Profiling: `GameplayContext.Profiler` (`ISimulationProfiler`) times every system tick, command and subscriber;
  `SimulationStats` (no Unity, no allocations after warmup), `UnityProfiler` (Unity Profiler markers),
  `SimulationProfiler.Combine`; a Stats tab in the Simulation window.
- Cheats: `CheatRegistry` in Core (one registration, word and number arguments, help, completion);
  `CheatConsole` with the CheatConsole prefab, and `CheatPanel`, a window built in code with a button per cheat and
  a slider per number argument.
- `EventCues` and `Cue`: sounds and effect prefabs from domain events, pooled, paused with the game.
- Input: `UiLayers.Bind(InputAction, window)` and `UiLayers.EscapeAction`; `InputBindings` (binding overrides as
  settings), `Rebind` (interactive rebinding with conflict check), `ControlsList` (a controls page built in code).
- `UiWindow.ShowEmpty(name)`: a view built in code instead of a prefab.
- Documentation: "Mosaic and ECS" section in Architecture; Design Guide 3.6, 3.7, 4.6, 5.5, 7.5 and naming rules
  for dependency fields; Reference sections on saves, migrations, randomness, loading, cheats, input and testing;
  versioning rules in the README.

### Fixed

- A presenter subscribed to the domain bus that sent a command made replays diverge: the command ran inside the
  simulation and was not recorded. Presentation now listens through `EventFeed`.
- Spawning inside an action allocated a dictionary per entity.

## [0.1.1] - 2026-11-06

- Supports Unity 2021.3 and newer (was 6000.3). Dependencies: uGUI 1.0.0, Input System 1.7.0; Unity 2021 and 2022
  projects also need TextMeshPro.
- `UiWindow.KeepsViewWhenClosed`: a window can hide its view on close and reuse it on the next open.
- Documentation: Getting Started tutorial, Design Guide, Architecture moved to `Documentation~`.

## [0.1.0] - 2026-11-06

First public release.

- World with sparse-set component storage, allocation-free queries (up to three components) and entity systems.
- Command and event buses without per-message allocations; `Simulation` actions with deferred structural changes.
- Visibility rules for entities being built and stale-read detection.
- Simulation trace, world inspector (Window > Mosaic > Simulation) and `DebugWorlds`.
- Match recording and replay: `CommandRecording`, `ReplayFormat`, `WorldFingerprint`.
- Domain and screen state machines, settings store, data storage.
- UI shell: screens, windows, popups, input layers, entity views with pooling.
