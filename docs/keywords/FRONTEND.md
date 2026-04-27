# Frontend Keyword Index

Fast lookup table for frontend code. Update when adding services, hooks, stores, or shared components.

| Keyword | File | Notes |
|---|---|---|
| App entry | `BrickBot.Client/src/index.tsx` | React root |
| App shell | `BrickBot.Client/src/App.tsx` | — |
| Bridge service (WebView2 IPC) | `BrickBot.Client/src/shared/services/bridgeService.ts` | (planned) singleton, request/response + notifications |
| Event bus | `BrickBot.Client/src/shared/services/eventBus.ts` | (planned) backend → frontend pub/sub |
| Base IPC service | `BrickBot.Client/src/shared/services/baseModuleService.ts` | (planned) typed sendMessage helper |
| Bridge types | `BrickBot.Client/src/shared/types/bridge.ts` | `ModuleName`, `BridgeMessage`, `BridgeResponse` |

## Module UI index

Each backend module gets a frontend folder under `src/modules/<name>/` with `services/`, `store/`, `hooks/`, `components/`, `operations/`. (Mirror BrickBot `src/modules/mod/` shape.)

| Module UI | Folder | Status |
|---|---|---|
| Capture | `src/modules/capture/` | Planned |
| Vision | `src/modules/vision/` | Planned |
| Template | `src/modules/template/` | Planned |
| Script (Monaco editor) | `src/modules/script/` | Planned |
| Runner (live log + preview) | `src/modules/runner/` | Planned |
| Profile (sidebar selector) | `src/modules/profile/` | Planned |
| Setting | `src/modules/setting/` | Planned |
