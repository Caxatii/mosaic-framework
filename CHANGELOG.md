# Changelog

## [Unreleased]

- **Behaviour change:** `Destroy` makes an entity unavailable at once, also inside an action: `IsAlive` is false,
  `Get` returns `null`, queries and systems skip it, commands to it are rejected, and using it throws
  `EntityNotAliveException`. The slot is still released and `EntityDestroyed` still published when the action ends.
  Before, a destroyed entity stayed fully usable until the end of the action, so it could be collected, hit or
  healed a second time in the same frame.
- Documentation: "Mosaic and ECS" section in Architecture; Design Guide 3.6 and naming rules for dependency fields.

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
