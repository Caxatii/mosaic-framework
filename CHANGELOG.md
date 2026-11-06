# Changelog

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
