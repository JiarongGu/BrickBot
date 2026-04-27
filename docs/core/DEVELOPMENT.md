# Development Workflow

## Build

```sh
# Backend
dotnet build BrickBot.slnx

# Frontend (also writes into BrickBot/wwwroot)
cd BrickBot.Client
npm run build

# Run
dotnet run --project BrickBot
```

## Dev mode

Dev mode is on when **either**:
- `ASPNETCORE_ENVIRONMENT=Development` (or `DOTNET_ENVIRONMENT=Development`) is set, OR
- A `.dev` marker file exists in the build output directory.

When dev mode is on:
- WebView2 navigates to `http://localhost:3000` (Vite dev server) — full HMR.
- `LogHelper` defaults to `Debug` level + colored console output.
- `EmbeddedResourceProvider` serves from the on-disk `wwwroot/` instead of extracting embedded resources.

`BrickBot/Properties/launchSettings.json` provides two profiles — **BrickBot (Development)** (default for `dotnet run` / F5) and **BrickBot (Production)**.

### Frontend dev loop

```sh
# Terminal 1: Vite dev server
cd BrickBot.Client
npm start                    # serves on :3000 with HMR

# Terminal 2: backend (auto-uses Development profile)
dotnet run --project BrickBot
```

If WebView2 shows a "site can't be reached" error, you forgot Terminal 1 — start the Vite server.

### Production-mode local run

```sh
cd BrickBot.Client && npm run build      # writes ../BrickBot/wwwroot
dotnet run --project BrickBot --launch-profile "BrickBot (Production)"
```

### Override the log level

Set `BRICKBOT_LOG_LEVEL` to one of `Verbose|Debug|Info|Warn|Error|Off|All` — overrides the env-default.

## UI Theme & Layout

 Two pieces stay in sync:

1. **CSS variables** — [src/styles/theme-colors.css](../../BrickBot.Client/src/styles/theme-colors.css) defines `--color-*`, `--shadow-*`, `--border-radius-*`, and `--layout-*` variables for both light and dark themes. The `[data-theme="light|dark"]` attribute on `<html>` switches palettes. All component CSS reads these variables; never hardcode hex.

2. **AntD algorithm** — `<ConfigProvider theme={{ algorithm }}>` in [App.tsx](../../BrickBot.Client/src/App.tsx) selects `defaultAlgorithm` or `darkAlgorithm` from `useSettingsStore.resolvedTheme`. The same `resolvedTheme` value drives the `data-theme` attribute via a `useEffect`.

Theme source of truth: `GlobalSettings.theme` ("light" | "dark" | "auto") on the backend. Frontend resolves "auto" against `prefers-color-scheme` in [settingsStore.ts](../../BrickBot.Client/src/modules/setting/store/settingsStore.ts).

To restyle existing AntD components globally, add a rule in `theme-colors.css`. Example: `.ant-layout-header { background: var(--color-header-bg); }`.

## Window State Persistence

All main-window geometry persists in **`data/settings/global.json`** (`Window` block) — same file as theme/language. No separate `window.json`.

Lifecycle:
- **Load** — `ApplicationHost.CreateMainForm` calls `IWindowStateService.LoadWindowStateAsync()` before the form is shown. `IsPositionValid` rejects (x, y) on a missing monitor and falls back to centered.
- **Save** — `ApplicationHost.OnFormClosed` calls `SaveWindowStateAsync(form)`. If maximized, only the maximized flag is updated; the prior position/size is preserved for the next normal-state restore.
- **Reset** — `SETTING.RESET_WINDOW_STATE` clears `Window.{X,Y,Width,Height,Maximized}` AND emits `SettingEvents.WINDOW_STATE_RESET`. `ApplicationHost.HandleWindowStateResetAsync` listens, marshals to the UI thread, and re-centers/re-sizes immediately (no relaunch needed).

DPI: `HighDpiMode.PerMonitorV2` is set in `ApplicationBootstrapper.InitializeWinForms`. All saved coordinates are logical pixels; Windows scales per-monitor automatically.

## Tests

```sh
dotnet test BrickBot.slnx
```

## Adding a new module

1. `mkdir BrickBot/Modules/<Name>/{Services,Entities,Mappers,Models}`
2. Run `/backend-service <Name>Service <Name> ...` to generate service + interface + events
3. Run `/backend-facade <Name>Facade <Name> I<Name>Service` for the IPC router
4. Run `/service-registration <Name> I<Name>Service <Name>Service Singleton` to register DI
5. Wire facade into `ApplicationHost`'s message router
6. Run `/ipc-service <Name>Service <NAME> ...` to generate the frontend service
7. Add Zustand store + `use<Name>()` hook + `<name>Operations.ts` (mirror the same pattern)
