# Scripting Workflow — 4-layer architecture

Locked-in by D-008. Every BrickBot automation follows this layer cake. **Pick the right layer
when adding a feature** — don't collapse layers (e.g. don't put detection logic in main scripts,
don't put orchestration in libraries).

## Layer 1 — Detection objects

**Source of truth:** SQLite `Detections` table at `data/profiles/{id}/profile.db`.

**Author:** Training wizard (`DetectionsView` tab → "Train new"). 5-step wizard captures samples,
labels them, derives config, saves a `DetectionDefinition` + linked training samples.

**Five kinds**:
| Kind | Output | When to use |
|---|---|---|
| `template` | bbox + confidence | Static UI element — buff icon, button, alert |
| `progressBar` | 0..1 fill ratio | HP / MP / cooldown bar |
| `colorPresence` | blob count + bboxes | Loot drops, glowing enemies, marked tiles |
| `effect` | bool (triggered) + diff | Flash / animation / buff appearing |
| `featureMatch` | bbox + confidence | Sprite at variable scales (UI scales differ across resolutions) |
| `region` | resolved ROI (no detection) | Reusable anchored rectangle other detections compose on |

**Read from scripts:** `const r = detect.run('hp-bar')` → `{ value, found, match, triggered, blobs, confidence }`.

## Layer 2 — Library scripts (`library/*.js`)

**Purpose:** perception + state. Watch the world via detections; expose the state via `ctx` /
events / actions.

**Patterns:**
```js
// hp-monitor.js — write detection result to ctx every tick
brickbot.on('tick', () => {
  ctx.set('hp', detect.run('hp-bar').value);
  if (ctx.get('hp') < 0.3) brickbot.emit('hp-low');
});

// fishing-actions.js — register UI-invokable named actions
brickbot.action('cast', () => input.key(0x46));
brickbot.action('reel', () => input.click(960, 540));
```

**Loading:** lazy via `require('./hp-monitor')` from main or another library. NOT alphabetically
pre-loaded (changed in D-007 follow-up). Each file is a CommonJS module — exports if you want.

## Layer 3 — Main script (`main/<name>.js`)

**Purpose:** orchestration. Compose libraries, read state, trigger actions, run the loop.

**Skeleton:**
```js
require('./library/hp-monitor');
require('./library/fishing-actions');

brickbot.when(() => ctx.get('hp') < 0.3, () => brickbot.invoke('drink-potion'),
  { cooldownMs: 5000 });

brickbot.on('fish-caught', () => brickbot.invoke('reel'));

brickbot.runForever({ tickMs: 50, autoDetect: true });
```

`runForever` pumps frames, drains queued UI invocations, runs all enabled detections (when
`autoDetect`), evaluates `when()` predicates, fires `'tick'`. Single main per Run.

## Layer 4 — Runner

**Owns:** the engine thread, cancellation token, stop conditions, status events to the UI.

**Stop conditions** (`RunRequest.StopWhen`, all optional, OR-combined):
- `timeoutMs` — C# `Task.Delay` watchdog. Trips even when the script blocks.
- `onEvent` — JS subscribes to the named brickbot event; first emit triggers stop.
- `ctxKey + ctxOp + ctxValue` — per-tick predicate inside `runForever`. Numeric ops via
  `parseFloat` with string-equality fallback.

**Stop API for scripts:** `brickbot.stop('reason?')` — sets `StopReason.Script` and the optional
detail string. First call wins.

**Stop reasons surfaced to UI** via `RunnerState.stoppedReason`:
`user / timeout / event / context / script / completed / faulted`.

## Wiring chain — adding a new stop condition

1. `BrickBot/Modules/Runner/Services/IRunnerService.cs` — extend `StopWhenOptions` record
2. `BrickBot/Modules/Runner/RunnerFacade.cs` — payload deserializes via Dapper-style record
3. `BrickBot/Modules/Runner/Services/RunnerService.cs` — wire C# monitor (timer/file) OR
4. `BrickBot/Modules/Script/Services/StdLib.cs` — wire JS-side check inside `runForever`
5. Always: call `host.RequestStop(reason, detail)`, NOT `cts.Cancel()`, so reason persists
6. `BrickBot.Client/src/modules/runner/types.ts` — extend `StopWhenOptions`
7. `BrickBot.Client/src/modules/runner/RunnerPage.tsx` — add UI control to set the field
8. `BrickBot/Modules/Runner/Models/RunnerStatus.cs` — add new `StopReason` enum value if needed
   (it serializes camelCase via `JsonStringEnumConverter`)

## Anti-patterns to avoid

- **Detection logic in scripts.** Use a Detection object instead — gets wizard-trained tuning,
  re-train support, and shows up in the Detections tab.
- **Orchestration in libraries.** Libraries should be passive observers / action registries.
  Scheduling and decision-making belong in main.
- **Cancelling without a reason.** Always `host.RequestStop(reason, detail)`. Plain
  `cts.Cancel()` produces "Idle" in the UI with no explanation.
- **`detect.run` in tight loops without `runForever`.** Each `detect.run` does a fresh capture
  unless a tick pumped a frame. Inside `runForever({ autoDetect: true })` all enabled
  detections share one frame per tick; outside, they each grab their own.
