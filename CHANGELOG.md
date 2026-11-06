# Changelog

## [0.1.0] - 2026-11-06

First public release.

- World with sparse-set component storage, allocation-free queries (up to three components) and entity systems.
- Command and event buses without per-message allocations; `Simulation` actions with deferred structural changes.
- Visibility rules for entities being built and stale-read detection.
- Simulation trace, world inspector (Window > Mosaic > Simulation) and `DebugWorlds`.
- Match recording and replay: `CommandRecording`, `ReplayFormat`, `WorldFingerprint`.
- Domain and screen state machines, settings store, data storage.
- UI shell: screens, windows, popups, input layers, entity views with pooling.
