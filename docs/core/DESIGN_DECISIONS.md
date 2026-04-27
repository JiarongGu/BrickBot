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

**Primary:** `Windows.Graphics.Capture` (WinRT API). Hardware-accelerated, supports DirectX games, hits 60+ FPS.
**Fallback:** `BitBlt` for windowed/GDI games where WinRT capture is blocked or unavailable.

The capture pipeline exposes a **shared frame buffer** (single-writer, multi-reader, frame counter) that vision modules read at any frame. Capture rate is decoupled from script tick rate so vision can sample frames as fast as needed without blocking script execution.

Configurable target FPS (30 / 60 / 120).

**Why:** User's forward requirement is action-game support, which BitBlt alone can't deliver. WinRT is the modern, hardware-accelerated path; BitBlt fallback covers edge cases.

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
- Adding new host primitives: extend `HostApi.cs` (camelCase methods), then expose/wrap in InitScript. Don't expose `__host` directly to user scripts.
- Adding combat patterns: extend CombatScript. Behavior trees compose cleanly — prefer adding a new composite/decorator to writing imperative helpers.
- Adding shared-state shapes: just `ctx.set(key, value)` — no schema needed; values are JSON-serializable. If a key needs to be discoverable by other scripts, document the convention in `library/<thing>.js` near where it's set.
- TypeScript not supported (Jint is JS-only). If we want autocomplete in Monaco later, ship a `.d.ts` for the JS surface — no C# changes required.

---

## D-005: Folder name vs. project name

**Date:** 2026-04-27
**Status:** Accepted

Folder is `D:\Development\BrickBot`. C# project / namespace / solution name is **`BrickBot`** — matches the folder.

**Why:** Original folder was `NTE-Fisher`; renamed to `BrickBot` on 2026-04-27 to match the project identity. C# artifacts were named `BrickBot` from day one, so no code changes were needed for the rename.
