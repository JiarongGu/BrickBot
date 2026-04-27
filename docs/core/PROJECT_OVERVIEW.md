# BrickBot — Project Overview

## What it is

A Windows desktop application that automates gameplay by:
1. **Watching** a target game window (high-FPS screen capture).
2. **Seeing** the game (image template matching, color detection, OCR — via OpenCV).
3. **Acting** on the game (mouse/keyboard simulation via Win32 SendInput).
4. **Following user-written scripts** (JavaScript, evaluated by Jint) that chain detection + action.

Initial use case: fishing automation. Designed to extend to action games — capture pipeline targets 60+ FPS so script logic can react to fast on-screen events.

## Why this stack

- **WinForms + WebView2 host** — native Win32 access for input/capture, modern web UI for script editing and live preview.
- **React + TypeScript + Ant Design + Zustand** — proven UX stack, fast iteration, Monaco editor for in-app script editing.
- **OpenCvSharp4** — full OpenCV power without writing C++. Multi-scale template matching, color filtering, ROI processing.
- **Jint (JavaScript)** — pure-C# embedded interpreter, no native deps. Familiar syntax for users, sandboxable, supports a behavior-tree stdlib for action-game combat.
- **Windows.Graphics.Capture** — modern WinRT API; works with DirectX games and is hardware-accelerated.

## Module list

| Module | Responsibility |
|---|---|
| `Capture` | Find target window, screen-capture region (WinRT Graphics Capture / BitBlt), expose shared frame buffer. |
| `Vision` | Template matching, color detect, OCR. Reads from frame buffer. |
| `Input` | Win32 SendInput for mouse/keyboard. Coordinates relative to capture target. |
| `Template` | Manage per-profile template image library (PNGs + thumbnails). |
| `Script` | Store + load user JavaScript files (`main/` + `library/`) per profile. |
| `Runner` | Orchestrate a running session (start/stop/pause, live log, cancellation). |
| `Profile` | Per-game profiles bundling capture target + script + templates + settings. |
| `Setting` | App-wide settings (hotkeys, default FPS, theme, etc.). |

## Lifecycle of a script run

```
User clicks Run
  → RunnerService.Start(profileId)
    → CaptureService starts grabbing frames into shared buffer
    → JintScriptEngine boots: init.js → combat.js → library/*.js (alphabetical) → main/<selected>.js
    → Script calls vision.find(template) → reads frame buffer, runs template match, returns match
    → Script calls input.click(x, y) → SendInput at translated coords
    → Loop, with stop/pause checked between calls
  → User clicks Stop → cancellation token fires → engine unwinds → Capture stops
```

## Repository layout

```
BrickBot/
├── BrickBot/                     # main WinForms + WebView2 app (C#)
│   ├── Modules/                  # one folder per module
│   ├── Infrastructure/           # bootstrap, host, base classes
│   ├── Languages/                # en.json, cn.json
│   └── wwwroot/                  # built React frontend (embedded)
├── BrickBot.Client/              # React + Vite frontend (TypeScript)
├── BrickBot.Tests/               # xUnit tests
├── BrickBot.slnx                 # solution file
├── CLAUDE.md                     # mandatory session rules
├── .claude/
│   ├── rules/                    # project-specific rules
│   ├── skills/                   # code-gen skills
│   └── settings.json
└── docs/                         # this folder
```
