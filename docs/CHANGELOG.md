# Changelog

## [Unreleased]

### Added
- **Detection v3 (3-tier split)**: training now produces a `DetectionModel` artifact distinct
  from the runtime `DetectionDefinition` and the raw `TrainingSample` inputs. Models live at
  `data/profiles/{id}/models/{detectionId}.model.json`. Existence of the model file drives the
  "Trained" / "Untrained" badge in the Detections list.
- **Per-sample object boxes**: every sample carries its own bbox annotation (instead of one
  shared ROI). Pattern positives can sit at different positions across frames; bar samples
  mark the bar at each fill level. New `AnnotationCanvas` component drives the wizard's new
  Annotate step.
- **IoU diagnostics**: pattern training reports per-sample IoU and the trained model exposes
  `meanIoU` + `meanError`. Diagnostic thumbs draw labeled (orange dashed) vs predicted
  (color by IoU) overlays at full size.
- **Composite detection kind** (`composite`): boolean AND/OR over other detections. New 5th
  kind. No samples / training — pick operands + op. `detect.runAll` schedules composites in
  a 3rd pass after their operands.
- **`inverse` flag** on detections: flips `result.found`. Saves `!detect.run(x).found`
  boilerplate in `brickbot.when()` predicates.
- **`maxHit` cap** on detections: auto-disable after N successful hits in the current Run.
  Mirrors MaaFramework's `max_hit`.
- **ROI offset modes**: `DetectionRoi.offsetMode = 'inset' | 'relative'` when chained off
  another detection. Inset (default, back-compat) shrinks parent bbox; Relative anchors a
  sub-region at offset + absolute size.
- **`vision.waitStable(roi, opts)`** host primitive: block until ROI contents stop changing
  for `stableMs` (default 250). Inspired by MaaFramework's `pre_wait_freezes`. Lets scripts
  wait out animations before sampling fragile detections.
- **Per-profile input delivery mode**: `ProfileConfiguration.input.mode` selects
  `sendInput` (OS-level, default) / `postMessage` (background-friendly) / `postMessageWithPos`
  (PostMessage + brief SetWindowPos kick). Editable in the profile dialog. Inspired by
  MaaFramework / MaaNTE.
- **`SendInputService` PostMessage path**: WM_KEYDOWN/UP with proper scancode + transition
  bits, WM_LBUTTONDOWN/UP with `MAKELPARAM(x,y)` after `ScreenToClient`, optional
  `SetWindowPos(SWP_NOMOVE|NOSIZE|...)` kick.
- **New IPC**: `DETECTION.GET_MODEL` / `SAVE_MODEL` / `DELETE_MODEL`. `DETECTION.LIST` now
  annotates each item with `hasModel`. `DETECTION.TEST` accepts an optional candidate model
  (used by training-panel diagnostics).
- **Migration `202604300001`**: adds `ObjectBoxJson` + `IsInit` columns to `TrainingSamples`.
- **Locked decisions**: D-008 expanded for v3 split; new D-009 for input delivery modes.

### Changed
- `DetectionDefinition` slimmed: large trainer artifacts (descriptors blob, init frame PNG,
  reference patch PNG) moved to `DetectionModel`. Definition now holds only runtime knobs +
  output bindings.
- `IDetectionRunner.Run(profileId, def, frame)` loads model from `IDetectionModelStore`.
  New `RunWithModel(...)` accepts an in-memory model for editor live preview.
- `RunnerService.Start` injects `IProfileService` + `IInputService`; reads
  `ProfileConfiguration.Input.Mode` and applies it to the input service before the engine
  boots.
- TrainingPanel step shape is now kind-dependent:
  - `tracker` / `text`   → setup → samples → annotate → save (4)
  - `bar`                → setup → samples → annotate → train → save (5)
  - `pattern`            → setup → samples → annotate → search → train → save (6)
  - `composite`          → setup → compose → save (3)

### Notes
- Existing trained detections from before v3 will report "Untrained" until re-trained
  (artifacts moved files; no auto-migration). Definitions + saved samples are preserved.

## 0.1 (2026-04-27)

### Added
- Initial project scaffold: BrickBot.slnx, BrickBot (WinForms+WebView2), BrickBot.Client (React+Vite), BrickBot.Tests (xUnit).
- Memory pattern: CLAUDE.md, `.claude/rules/`, `.claude/skills/`, `docs/`.
- Core dependencies: WebView2, Dapper, FluentMigrator, OpenCvSharp4, Jint, Costura.Fody.
- Locked architectural decisions (D-001 through D-007).
