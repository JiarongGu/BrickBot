# Design Decisions

Architectural decisions for BrickBot. **Don't delete entries** when reversing a decision — supersede with a new entry and link back.

---

## D-001: WinForms + WebView2 host with module-pattern backend

**Date:** 2026-04-27
**Status:** Accepted

Architectural backbone:
- .NET 10 WinForms host with WebView2-embedded React UI
- Module pattern: `Modules/<Name>/{Facade, Services, Repository, Entities, Mappers, Models}`
- DI via Microsoft.Extensions.DependencyInjection, registered through `<Module>ServiceExtensions`
- IPC: JSON messages over `chrome.webview.postMessage`, routed by `BaseFacade`
- Persistence: SQLite + Dapper + FluentMigrator
- Push events: services emit via `IProfileEventBus`, facade NEVER emits

**Why:** WinForms gives us native Win32 access for input/capture (the hot path), and WebView2 keeps the UI on a modern web stack. The module pattern keeps each capability (capture / vision / input / script / runner / profile / setting) self-contained and discoverable. SQLite + Dapper is the smallest DB stack that still gives us migrations and typed queries.

---

## D-002: Lua via MoonSharp for user scripts

**Date:** 2026-04-27
**Status:** Superseded by **D-007**

Initial choice: Lua via MoonSharp. Reversed once the action-game combat use case made it clear we needed (a) a scripting layer that's already familiar to web-tooling users, and (b) a behavior-tree library above raw scripting so non-coders can reuse combat patterns without authoring entire scripts. See D-007.

Original rationale (kept for history):
- Game-industry familiarity, MoonSharp is pure C#, MoonSharp coroutines for cooperative cancellation, Lua syntax simpler than JS.

---

## D-003: Capture pipeline — Windows.Graphics.Capture primary, BitBlt fallback

**Date:** 2026-04-27
**Status:** Accepted

**Primary:** `Windows.Graphics.Capture` (WinRT). Reads the target window's framebuffer directly through DWM, so capture is unaffected by occluding windows, works for GPU-composited / DirectX-windowed apps, and never leaks desktop-wallpaper redirection bitmaps. Implemented in `WinRtCaptureService` with raw COM/D3D11 interop (no SharpDX/Vortice dep). Per-window cached `Direct3D11CaptureFramePool` + `GraphicsCaptureSession`; reuses across `Grab()` calls.
**Fallback:** `BitBltCaptureService` (PrintWindow with `PW_CLIENTONLY | PW_RENDERFULLCONTENT` + screen-DC BitBlt) for Windows < 10 1809 or apps that opt out of WinRT capture via `WDA_EXCLUDEFROMCAPTURE`.

The capture pipeline exposes a **shared frame buffer** (single-writer, multi-reader, frame counter) that vision modules read at any frame. Capture rate is decoupled from script tick rate so vision can sample frames as fast as needed without blocking script execution.

Configurable target FPS (30 / 60 / 120).

**Why:** User's forward requirement is action-game support. WinRT GraphicsCapture is the only path that captures occluded / GPU-composited windows reliably. BitBlt-from-window-DC was leaking cached desktop pixels for any DWM-redirected window — that's why earlier capture attempts showed wallpaper instead of the target. Two strategies inside the GDI fallback (PrintWindow + screen-region BitBlt) cover the legacy paths without that bug.

**How to apply:** New host primitive that needs current-frame access — go through `IFrameBuffer.Snapshot()` from the script tick loop (already pumped via `host.pumpFrame()`). Don't call `ICaptureService.Grab` from inside vision ops; the frame buffer is the choke point. To opt a frame source out of the WinRT path (e.g. for testing), inject `BitBltCaptureService` directly — it's still a registered concrete service.

---

## D-004: OpenCvSharp4 for vision

**Date:** 2026-04-27
**Status:** Accepted
**Alternatives considered:** SixLabors.ImageSharp only, Emgu.CV

Use [OpenCvSharp4](https://github.com/shimat/opencvsharp) (managed wrapper over native OpenCV) for all vision operations.

**Why:** Real OpenCV (template matching with `MatchTemplate`, color thresholding, contour detection, OCR via Tesseract integration if needed later). ImageSharp alone is too slow for real-time matching. Emgu.CV has a more restrictive license.

All vision ops accept an optional ROI (region of interest) to keep latency low for action-game use.

---

## D-006: Profile = a saved game-automation target

**Date:** 2026-04-27
**Status:** Accepted

A `Profile` represents one game-automation target (e.g. "WoW fishing", "Diablo combat"). Profile mechanics:
- Folder per profile under `data/profiles/{id}/{config.json, scripts/, templates/, temp/}`
- Switchable via the header dropdown; only one profile is "active" at a time
- Events on create/update/delete/switch (`PROFILE.{CREATED,UPDATED,DELETED,SWITCHED}`) routed through `IProfileEventBus`
- IPC via `BaseFacade` like every other module

`ProfileConfiguration` body:
- `WindowMatchRule` (how to find the target game window — title regex / class / process)
- `CaptureSettings` (winRT vs bitBlt, target FPS, default ROI)
- `ScriptSettings` (entry main script name, autostart, tick interval)
- `UiHints` (free-form per-profile UI state)

**Why:** Per CLAUDE.md a profile is "a saved game target (window match rule + capture settings + script + templates)". Each profile owns its own scripts and templates so a single user can keep multiple game configurations isolated.

**How to apply:** When adding profile-scoped state (window positions, recent items, last-selected-X), put it on `ProfileConfiguration` — not in `GlobalSettings` or scattered files.

---

## D-007: JavaScript via Jint with a behavior-tree stdlib

**Date:** 2026-04-27
**Status:** Accepted
**Supersedes:** D-002 (Lua/MoonSharp)
**Alternatives considered:** ClearScript+V8 (faster but ships native binaries), YantraJS (less battle-tested), keep Lua, full visual-config builder

Embedded scripting language is **JavaScript**, evaluated by [Jint](https://github.com/sebastienros/jint) (pure-C#, no native deps). Action-game combat is composed using a thin **behavior-tree stdlib** shipped on `combat.*`.

**Why:**
- Action-game combat (the forward use case) needs reusable patterns — skill rotations with cooldowns, dodge windows, conditional heals — and authoring each profile's combat as a hand-rolled imperative loop scales badly. BTs are how AAA action games structure NPC behavior; the same primitives compose neatly for player automation.
- JS has the largest pool of "I can read this" users, plus Monaco gives us autocomplete/syntax-checking out of the box.
- Jint matches MoonSharp's "pure C#, no native deps" property — packaging story stays identical (Costura embeds it).
- Hybrid: stdlib gives non-experts copy-pasteable building blocks (the floor); the underlying JS gives experts the full ceiling. We don't have to invest in a visual config builder yet.

**Architecture:**
- `Modules/Script/Services/HostApi.cs` — single C# class exposed as the JS global `__host`. Methods take primitive types only; no `JsValue` / `ObjectInstance` parsing in C#.
- `Modules/Script/Services/StdLib.cs` — inlined as `const string`. Two scripts run before every user script:
  - **InitScript** wraps `__host` into the user-facing globals: `vision`, `input`, `log`, `wait`, `isCancelled`, `now`.
  - **CombatScript** ships behavior-tree primitives on `combat.*`: `Sequence`, `Selector`, `Inverter`, `Cooldown`, `Action`, `Condition`, `SkillRotation`, `runTree`.
- `Modules/Script/Services/JintScriptEngine.cs` — boots the engine with `AllowClr()` + `Strict()`, sets `__host`, then executes init → combat → user.

**User-facing surface (defined by InitScript):**
- `vision.find(path, { minConfidence?, roi? })` → `{x, y, w, h, cx, cy, confidence}` or `null`
- `vision.waitFor(path, timeoutMs, { minConfidence? })` → match or `null`
- `vision.colorAt(x, y)` → `{r, g, b}`
- `input.click(x, y, button?)` (button defaults to `'left'`)
- `input.moveTo(x, y)` / `input.drag(x1, y1, x2, y2, button?)`
- `input.key(vk)` / `input.keyDown(vk)` / `input.keyUp(vk)` (Win32 VK codes)
- `input.type(text)`
- `log(msg)` / `wait(ms)` / `isCancelled()` / `now()`

**Behavior-tree stdlib (`combat.*`):**
- Each node is a function `(ctx) => 'success' | 'failure' | 'running'`.
- `Sequence(...children)` — first non-success short-circuits.
- `Selector(...children)` — first non-failure short-circuits.
- `Inverter(child)` — flips success ↔ failure.
- `Cooldown(ms, child)` — gates by per-instance cooldown; resets on success only.
- `Action(fn)` — side-effect leaf; always succeeds.
- `Condition(predicate)` — leaf, success iff `predicate(ctx)` truthy.
- `SkillRotation([{ name, cooldown, cast, ready? }])` — priority-ordered skill picker (Selector + per-skill Cooldown).
- `runTree(tree, { intervalMs?, limitMs? })` — tick loop; exits on `isCancelled()` or `limitMs` deadline.

**Script types & shared context (added 2026-04-27 follow-up):**
- Two script kinds, filename-based, picked up from `data/profiles/{id}/scripts/`:
  - `main/*.js` — top-level orchestrators. Runner picks ONE to execute.
  - `library/*.js` — helpers / monitors / skill defs. All preloaded BEFORE main runs.
- **Single Jint engine per Run**, single thread. Boot order: init → combat → libraries (alphabetical) → main.
- **Per-Run shared context** via `ctx` global, backed by `ScriptContext` (`ConcurrentDictionary<string, string>` of JSON-encoded values). Library monitor helpers write status (e.g. `ctx.set('hp', 70)`), main reads (`ctx.get('hp', 100)`). Cleared at the start of every Run. Methods: `set / get / has / delete / keys / snapshot / inc`.
- This enables a perception → state → action loop without multi-threading: monitor functions in `library/*.js` update `ctx`, main script reads from `ctx` each tick.

**IPC** (SCRIPT module): `LIST` / `GET` / `SAVE` / `DELETE`, all keyed by `{ profileId, kind, name }`. The Runner's `START` message takes `{ windowHandle, profileId, mainName, templateRoot }` — backend resolves scripts off disk.

**UI nav**: top-level tabs are `Runner | Scripts | Tools | Settings`. The Scripts tab is the only place script files are authored. Profile management lives under the Tools tab (header dropdown's "Manage" link routes there).

**Capture & Templates authoring (added 2026-04-27 follow-up):**
- Two distinct capture paths kept separate:
  - **Streaming (Run time)** — used by `vision.*` while scripts run.
  - **One-shot (authoring time)** — `IScreenshotService.GrabPng(handle)` captures a single frame and PNG-encodes via `Cv2.ImEncode`. IPC: `CAPTURE.GRAB_PNG { windowHandle }` → `{ pngBase64, width, height }`.
- New `TEMPLATE` IPC module (`LIST` / `SAVE` / `DELETE`) backed by `ITemplateFileService` over `data/profiles/{id}/templates/*.png`. Filename validation rejects path traversal. Same dir scripts read from at Run time, so `vision.find('foo.png')` picks up new templates immediately.
- The `CapturePanel` React component does cropping client-side (Canvas + `toDataURL('image/png')`) so the backend never holds in-flight frames — the only state is the persisted file.
- Surfaced two ways: Drawer button on the Scripts editor toolbar (mid-edit authoring) and "Captures" sub-tab on the Tools tab (standalone).

**How to apply:**
- Adding new host primitives: extend `HostApi.cs` (camelCase methods), then expose/wrap in InitScript. Don't expose `__host` directly to user scripts. **Also update `Modules/Script/Resources/brickbot.d.ts`** so Monaco offers autocomplete for the new symbol.
- Adding combat patterns: extend CombatScript. Behavior trees compose cleanly — prefer adding a new composite/decorator to writing imperative helpers.
- Adding shared-state shapes: just `ctx.set(key, value)` — no schema needed; values are JSON-serializable. If a key needs to be discoverable by other scripts, document the convention in `library/<thing>.js` near where it's set.

**Authoring language (updated 2026-04-27 follow-up — supersedes "TypeScript not supported"):** scripts are now authored in **TypeScript**. Frontend transpiles via Monaco's bundled TS language service (`module: CommonJS`, `target: ES2020`); backend `ScriptFacade.SAVE` accepts both `tsSource` + `jsSource`, `ScriptFileService` writes them side-by-side. Runner only ever loads the compiled `.js`. Library imports work via `require()`/CommonJS — `JintScriptEngine` installs a `require` global that resolves `'brickbot'` to a synthetic host module and every other id to a profile library through a `LibraryResolver` callback. Libraries are loaded lazily (on first `require`), no longer alphabetically pre-loaded. See `.claude/rules/script-save-chain.md` for the full wiring chain.

**Event / action / trigger surface (added 2026-04-27 follow-up):** `brickbot.{on, off, emit, action, invoke, listActions, when, runForever}` exposes a single-threaded JS event bus + named-action registry + declarative trigger model. `brickbot.runForever({ tickMs })` is the new main loop — each tick pumps a frame into the shared `IFrameBuffer`, drains queued action invocations from the UI (via `IScriptDispatcher`), evaluates registered triggers, fires `'tick'`. Built-in events: `start`, `frame`, `tick`, `stop`, `error`. Action invocation crosses threads through `IScriptDispatcher` (lock-free queue) — UI calls `RUNNER.INVOKE_ACTION` → dispatcher.Enqueue → engine tick dequeues → `brickbot.invoke(name)`. Push event `SCRIPT.ACTIONS_CHANGED` keeps the UI's Tools tab in sync without polling. `vision.*` reads from the buffered frame when a tick has pumped one (consistent within a tick), falls back to on-demand `Grab` for legacy procedural mains.

---

## D-008: Detection-first authoring + stop conditions

**Date:** 2026-04-28
**Status:** Accepted

Locked-in **4-layer automation workflow**. Features go on the right layer; do not collapse them.

1. **Detection objects** — typed, named vision rules. Authored by the training wizard
   (`DetectionsView` → `TrainingPanel`) and persisted in the per-profile SQLite (the `Detections`
   table). Five kinds: `template`, `progressBar`, `colorPresence`, `effect`, `featureMatch`,
   `region`. Scripts read by name: `detect.run('hp-bar').value`.
2. **Library scripts** — perception + state. Run detections, write to `ctx`, emit `brickbot`
   events, register named `brickbot.action()`s. Loaded **lazily** via `require()` (the legacy
   alphabetical pre-load is gone). Each library file is a CommonJS module emit.
3. **Main script** — orchestrator. Reads `ctx`, listens via `brickbot.on()`, declares
   `brickbot.when()` triggers, runs `brickbot.runForever({ tickMs, autoDetect })`. The Runner
   executes ONE main per Run.
4. **Runner** — picks window + main, optionally configures stop conditions, drives the engine
   thread with cancellation.

**Stop conditions** (`RunRequest.StopWhen`):
- `TimeoutMs` — C# `Task.Delay` watchdog so even blocking scripts (long vision call) trip out.
- `OnEvent` — JS-side `brickbot.on()` subscription requesting stop on any matching `emit()`.
- `CtxKey + CtxOp + CtxValue` — JS-side per-tick predicate check inside `runForever`.

All conditions OR-combined; manual Stop wins over everything. The first stop trigger wins so
"user clicked Stop" never gets overwritten by a stale timeout completing right after.

`RunnerState.StoppedReason` (`user / timeout / event / context / script / completed / faulted`)
is surfaced in the UI so users know **why** a run ended. Scripts request shutdown with
`brickbot.stop('reason')` (becomes `Script` reason) — useful for goal-driven flows.

**Why:** Without explicit stop conditions, the only way to end a run was to babysit it. Long
training runs and goal-driven automations (e.g. "fish until you have 100 items") need the
runner to self-terminate. The split between C# (timeout watchdog) and JS (event/ctx checks)
keeps each kind on the layer that owns the data — JS owns the event bus and ctx, C# owns
the cancellation token.

**How to apply:** New stop conditions → add a field to `StopWhenOptions`, plumb through
`RunnerFacade.Start`, then either:
  - Implement in C# if it can be checked off the engine thread (timer, file-watch, etc.).
  - Implement in JS-side `runForever` if it depends on engine-thread state (ctx, events).
Always set `host.RequestStop(reason, detail)` rather than calling `cts.Cancel()` directly so
the surfaced reason matches the trigger.

---

## D-005: Folder name vs. project name

**Date:** 2026-04-27
**Status:** Accepted

Folder is `D:\Development\BrickBot`. C# project / namespace / solution name is **`BrickBot`** — matches the folder.

**Why:** Original folder was `NTE-Fisher`; renamed to `BrickBot` on 2026-04-27 to match the project identity. C# artifacts were named `BrickBot` from day one, so no code changes were needed for the rename.
