# Architecture Decisions

Key locked-in decisions for BrickBot. Update this file (don't lose history) when a decision changes.

## Reference project

`D:\Development\BrickBot` is the canonical template. When generating code or making architecture decisions, look there first for the equivalent pattern. BrickBot mirrors:
- Module layout: `Modules/<Name>/{<Name>Facade.cs, <Name>ServiceExtensions.cs, <Name>Events.cs, Services/, Entities/, Mappers/, Models/}`
- IPC: frontend `bridgeService` ↔ backend `BaseFacade` with JSON messages tagged by module + type. Push notifications via `IProfileEventBus` → frontend `eventBus`.
- Frontend state: Zustand store per module, hook wrapper (`use<Module>()`), separate `<module>Operations.ts` for async actions.
- i18n: `Languages/en.json` + `Languages/cn.json`, flat key-value, `errors.<CODE>` keys for `OperationException`.
- DI: `*ServiceExtensions.AddXxxServices()` per module, registered in `ApplicationBootstrapper`.

## Naming

- C# project / namespace: `BrickBot`
- Solution: `BrickBot.slnx`
- Projects: `BrickBot`, `BrickBot.Client`, `BrickBot.Tests`

## Scripting

- **Engine**: Jint (JavaScript, pure-C#, no native deps). Locked in by D-007 (supersedes D-002).
- **Script types per profile** (filename-based, picked up from `data/profiles/{id}/scripts/`):
  - `main/*.js` — top-level orchestrators. Runner picks ONE to execute.
  - `library/*.js` — helpers, monitors, skill defs. All preloaded BEFORE main runs.
- **Single Jint engine per Run**, single thread. Boot order: init → combat → libraries (alphabetical) → main.
- **Shared context (`ctx`)** — per-Run JSON-backed kv store on `ScriptContext`. Pattern: library monitor helpers write status (e.g. `ctx.set('hp', 70)`), main reads (`ctx.get('hp')`). Cleared each Run. Methods: `set / get / has / delete / keys / snapshot / inc`.
- **Two-layer host surface**: thin C# `HostApi` exposed as `__host`, plus a JS init script wrapping it into ergonomic globals. Init script + combat stdlib inlined in `Modules/Script/Services/StdLib.cs`.
- **Host API surface** (top-level globals, defined by init script):
  - `vision.find(path, { minConfidence?, roi? })` → `{x, y, w, h, cx, cy, confidence}` or `null`
  - `vision.waitFor(path, timeoutMs, { minConfidence? })` → match or `null`
  - `vision.colorAt(x, y)` → `{r, g, b}`
  - `input.click(x, y, button?)` — button defaults to `'left'`
  - `input.moveTo(x, y)` / `input.drag(x1, y1, x2, y2, button?)`
  - `input.key(vk)` / `input.keyDown(vk)` / `input.keyUp(vk)` — Win32 VK codes
  - `input.type(text)`
  - `wait(ms)` — cooperative; wakes early on Stop
  - `log(msg)` — surfaces to runner UI
  - `isCancelled()` / `now()`
- **Behavior-tree stdlib** (`combat.*` — encourage composition over imperative loops):
  - `Sequence`, `Selector`, `Inverter` — composites; nodes return `'success' | 'failure' | 'running'`
  - `Cooldown(ms, child)` — gates on time, resets only on success
  - `Action(fn)`, `Condition(predicate)` — leaf primitives
  - `SkillRotation([{ name, cooldown, cast, ready? }, ...])` — priority-ordered skill picker
  - `runTree(tree, { intervalMs?, limitMs? })` — tick loop, exits on Stop
- Scripts run in their own thread with cooperative cancellation. The Runner module enforces stop/pause.
- **Adding a new host primitive**: add a method to `HostApi.cs` (use camelCase to match JS), then expose/wrap in `StdLib.InitScript`. Don't expose `__host` methods directly to user scripts — always go through the init wrapper so we can change the C# signature without breaking user code.

## UI Tabs

Top-level navigation (`App.tsx`) — four tabs:
- **Runner** — pick window + main script, start/stop, log. No code editor here.
- **Scripts** — manage script files (Main + Library sections) with Monaco editor. Per-profile. Has a "Capture" toolbar button that opens the Capture & Templates panel as a Drawer (so users can author templates mid-edit).
- **Tools** — utility container. Sub-tabs: Profiles, Captures. Future: ROI picker, color sampler, log viewer.
- **Settings** — theme/language/log-level/window-state-reset (global).

Active profile dropdown lives in the header. Its "Manage profiles" link routes to the Tools tab (Profiles sub-tab).

## Capture & Templates Workflow

For authoring template PNGs that scripts reference via `vision.find('name.png')`:

- **One-shot screenshot** — `IScreenshotService.GrabPng(handle)` captures a single frame, encodes via `Cv2.ImEncode(".png", ...)`, returns bytes + dimensions. IPC: `CAPTURE.GRAB_PNG { windowHandle }` → `{ pngBase64, width, height }`. Distinct from the streaming capture pipeline used at Run time.
- **Template CRUD** — `ITemplateFileService` over `data/profiles/{id}/templates/*.png`. IPC module: `TEMPLATE` (`LIST`, `SAVE`, `DELETE`). Filename validation rejects path traversal and invalid chars.
- **Frontend `CapturePanel`** — canvas-based UI: pick window, capture, hover for `(x, y)` + `rgb(...)`, drag rectangle to crop, save crop as a PNG. Cropping is done client-side via a hidden Canvas + `toDataURL('image/png')` — backend just stores the bytes.
- Surfaced two ways: a Drawer button on the Scripts editor toolbar, and a "Captures" sub-tab on the Tools tab.

## Capture

- **Primary**: `Windows.Graphics.Capture` (WinRT). Hardware accelerated, supports DirectX games, hits 60+ FPS.
- **Fallback**: `BitBlt` for windowed/GDI games where WinRT is blocked.
- Capture pipeline exposes a **shared frame buffer** that vision modules read from at any frame, decoupled from script tick rate.
- Configurable target FPS (30 / 60 / 120). The frame buffer is single-writer, multi-reader with a frame counter.

## Vision

- **Library**: OpenCvSharp4 (managed wrapper over OpenCV).
- **Operations**: template matching (multi-scale optional), color thresholding, color-at-point, edge detection (later).
- All vision ops accept an optional ROI (region of interest) to keep latency low for action-game use.
- Templates are stored per-profile in `data/<profile>/templates/` (PNG).

## Input

- Use `SendInput` (Win32) — works in most games. Fall back to `keybd_event` / `mouse_event` if needed.
- Coordinates are by default **relative to the captured window**, not screen. The Input service translates to screen coords via the current capture target.

## Persistence

- **SQLite** per profile (data folder layout TBD; mirror BrickBot's profile-scoped data folders).
- **Dapper + FluentMigrator** as in reference.
- **Profile** = a saved game target (window match rule + capture settings + script + templates).
