# BrickBot

Windows desktop application for game automation. Watches a game window via high-FPS screen capture, recognises on-screen elements with OpenCV, and drives the game with simulated mouse/keyboard input. Behaviour is defined by user-written **JavaScript** scripts that chain detection + action.

Initial use case: fishing automation. Designed to extend to action games — the capture pipeline targets 60+ FPS so script logic can react to fast on-screen events.

## Architecture

```
WinForms + WebView2 host (.NET 10)
├── Backend (C#) — heavy lifting: capture, vision, input, scripting, file I/O
└── Frontend (React 19 + TypeScript + Vite) — UI only, embedded in WebView2
```

### Modules

| Module | Responsibility |
|---|---|
| `Capture` | Find target window, screen-capture region, expose shared frame buffer |
| `Vision` | Template matching, color detect, color-at-point — reads from frame buffer |
| `Input` | Win32 `SendInput` for mouse/keyboard, coords translated to capture target |
| `Template` | Per-profile template image library (PNGs) |
| `Script` | Per-profile JavaScript files: `main/*.js` orchestrators + `library/*.js` helpers |
| `Runner` | Orchestrate a running session — start/stop/pause, live log, cancellation |
| `Profile` | Per-game profiles bundling capture target + scripts + templates + settings |
| `Setting` | App-wide settings — hotkeys, default FPS, theme, language |

### Tech stack

**Backend**: .NET 10 WinForms + WebView2, Microsoft.Extensions.DependencyInjection, SQLite + Dapper + FluentMigrator, Costura.Fody (single-file packaging), OpenCvSharp4, **Jint** (pure-C# JavaScript interpreter), `Windows.Graphics.Capture` (with `BitBlt` fallback).

**Frontend**: React 19 + TypeScript + Vite + Ant Design 6 + Zustand (immer) + react-i18next + Monaco editor.

## Scripting model

Each profile owns two script kinds under `data/profiles/{id}/scripts/`:

- `main/*.js` — top-level orchestrators. The Runner picks ONE main script and executes it.
- `library/*.js` — helpers, monitors, skill definitions. **All** library files load into the engine BEFORE main runs (alphabetical order).

A single Jint engine runs per session, single thread. Boot order: `init.js` → `combat.js` (BT primitives) → every `library/*.js` (alphabetical) → selected `main/<name>.js`.

A per-Run shared key/value store (`ctx`) is exposed as a JS global. Library "monitor" functions write (`ctx.set('hp', 70)`); main scripts read (`ctx.get('hp')`). Cleared each Run.

### Host API surface

```js
vision.find(path, { minConfidence?, roi? })   // → match | null
vision.waitFor(path, timeoutMs, opts?)        // → match | null
vision.colorAt(x, y)                          // → { r, g, b }

input.click(x, y, button?)                    // 'left' | 'right' | 'middle'
input.moveTo(x, y)
input.drag(x1, y1, x2, y2, button?)
input.key(vk) / keyDown(vk) / keyUp(vk)       // Win32 VK codes
input.type(text)

wait(ms)            // cooperative — wakes early on Stop
log(msg)            // surfaces to runner UI
isCancelled()
now()
```

### Behavior-tree stdlib (`combat.*`)

```js
combat.Sequence([...])      // all-success
combat.Selector([...])      // first-success
combat.Inverter(node)
combat.Cooldown(ms, child)  // gates on time, resets only on success
combat.Action(fn)
combat.Condition(predicate)
combat.SkillRotation([{ name, cooldown, cast, ready? }, ...])

combat.runTree(tree, { intervalMs?, limitMs? })   // tick loop, exits on Stop
```

## UI

Four top-level tabs:

- **Runner** — pick window + main script, start/stop, log
- **Scripts** — `main/` + `library/` file browser, Monaco editor; toolbar Capture button opens the capture panel as a slide-in for mid-edit template authoring
- **Tools** — utility cards (Profiles, Captures, future ROI / color sampler)
- **Settings** — theme / language / log-level / window-state-reset

The active profile dropdown lives in the header.

## Repository layout

```
BrickBot/
├── BrickBot/                    # main WinForms + WebView2 app (C#)
│   ├── Modules/                 # one folder per module
│   ├── Infrastructure/          # bootstrap, host, base classes
│   ├── Languages/               # en.json, cn.json
│   └── wwwroot/                 # built React frontend (embedded)
├── BrickBot.Client/             # React + Vite frontend
├── BrickBot.Tests/              # xUnit tests
├── BrickBot.slnx                # solution file
├── CLAUDE.md                    # mandatory session rules
├── .claude/
│   ├── rules/                   # project-specific rules
│   ├── skills/                  # code-gen skill templates
│   └── settings.json            # permissions + hooks
└── docs/                        # full documentation set
```

## Build & run

Requirements: .NET 10 SDK, Node 20+, Windows 10/11.

```powershell
# Restore + run (debug — frontend served from Vite dev server at :5173)
cd BrickBot.Client && npm install
cd ..
dotnet run --project BrickBot

# Production build (frontend embedded into the .exe via Costura)
./build-production.ps1

# Tests
dotnet test BrickBot.slnx
cd BrickBot.Client && npx jest
```

See [RELEASING.md](RELEASING.md) for release packaging.

## Documentation

- [docs/AI_GUIDE.md](docs/AI_GUIDE.md) — entry point for AI coding sessions
- [docs/core/PROJECT_OVERVIEW.md](docs/core/PROJECT_OVERVIEW.md) — extended overview
- [docs/core/DESIGN_DECISIONS.md](docs/core/DESIGN_DECISIONS.md) — locked-in architectural decisions
- [docs/keywords/](docs/keywords/) — fast-lookup indexes (BACKEND, FRONTEND, HOW_TO)
- [CLAUDE.md](CLAUDE.md) — mandatory rules for AI sessions

## License

[MIT](LICENSE) © 2026 Jiarong Gu
