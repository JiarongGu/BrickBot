# Backend Keyword Index

Fast lookup table for backend code. Update when adding services, facades, repositories, or extension points.

## Core / app

| Keyword | File | Notes |
|---|---|---|
| App entry | `BrickBot/Program.cs` | Calls `ApplicationBootstrapper.Run()` |
| Bootstrap / DI registration | `BrickBot/Infrastructure/ApplicationBootstrapper.cs` | Registers all `<Module>ServiceExtensions` |
| App host / lifetime | `BrickBot/Infrastructure/ApplicationHost.cs` | Owns `IServiceProvider` |
| Main window / WebView2 | `BrickBot/Infrastructure/OptimizedForm.cs` | — |
| Base facade | `BrickBot/Modules/Core/Ipc/BaseFacade.cs` | Routes IPC requests |
| OperationException | `BrickBot/Modules/Core/Exceptions/OperationException.cs` | Error code + i18n params |
| Profile event bus | `BrickBot/Modules/Core/Events/IProfileEventBus.cs` | Services emit, facades never |
| Module registry | `BrickBot/Modules/Core/ModuleNames.cs` | String constants |

## Detection (v3 — Definition / Model / Samples)

| Keyword | File |
|---|---|
| Detection definition (runtime config) | `BrickBot/Modules/Detection/Models/DetectionDefinition.cs` |
| Detection model (compiled artifact) | `BrickBot/Modules/Detection/Models/DetectionModel.cs` |
| Training sample (raw input + per-sample box) | `BrickBot/Modules/Detection/Models/TrainingSample.cs` |
| Detection result | `BrickBot/Modules/Detection/Models/DetectionResult.cs` |
| Trainer service | `BrickBot/Modules/Detection/Services/DetectionTrainerService.cs` |
| Runner (def+model → result) | `BrickBot/Modules/Detection/Services/DetectionRunner.cs` |
| Definition store (SQLite) | `BrickBot/Modules/Detection/Services/DetectionFileService.cs` |
| Model store (file-backed) | `BrickBot/Modules/Detection/Services/DetectionModelStore.cs` |
| Training sample store | `BrickBot/Modules/Detection/Services/TrainingSampleService.cs` |
| Facade (IPC) | `BrickBot/Modules/Detection/DetectionFacade.cs` |

## Input

| Keyword | File |
|---|---|
| InputMode enum (sendInput / postMessage / postMessageWithPos) | `BrickBot/Modules/Input/Models/InputMode.cs` |
| Service (Mode + TargetWindow) | `BrickBot/Modules/Input/Services/SendInputService.cs` |

## Profile

| Keyword | File |
|---|---|
| Configuration model | `BrickBot/Modules/Profile/Models/ProfileConfiguration.cs` |
| Service / repository | `BrickBot/Modules/Profile/Services/ProfileService.cs` |

## Script

| Keyword | File |
|---|---|
| Host API (`__host` exposed to JS) | `BrickBot/Modules/Script/Services/HostApi.cs` |
| Init/combat stdlib (inlined) | `BrickBot/Modules/Script/Services/StdLib.cs` |
| Engine | `BrickBot/Modules/Script/Services/JintScriptEngine.cs` |
| Typings (`brickbot.d.ts`) | `BrickBot/Modules/Script/Resources/brickbot.d.ts` |

## Vision

| Keyword | File |
|---|---|
| Service interface | `BrickBot/Modules/Vision/Services/IVisionService.cs` |
| Implementation | `BrickBot/Modules/Vision/Services/VisionService.cs` |
| Match models | `BrickBot/Modules/Vision/Models/VisionMatch.cs` |

## Migrations

| Keyword | File |
|---|---|
| TrainingSamples table | `BrickBot/Modules/Database/Migrations/202604280003_CreateTrainingSamplesTable.cs` |
| TrainingSamples per-sample box (v3) | `BrickBot/Modules/Database/Migrations/202604300001_AddTrainingSampleObjectBox.cs` |
| Detections table | `BrickBot/Modules/Database/Migrations/202604280002_CreateDetectionsTable.cs` |

## Module index

| Module | Folder |
|---|---|
| Capture | `BrickBot/Modules/Capture/` |
| Vision | `BrickBot/Modules/Vision/` |
| Input | `BrickBot/Modules/Input/` |
| Template | `BrickBot/Modules/Template/` |
| Detection | `BrickBot/Modules/Detection/` |
| Recording | `BrickBot/Modules/Recording/` |
| Script | `BrickBot/Modules/Script/` |
| Runner | `BrickBot/Modules/Runner/` |
| Profile | `BrickBot/Modules/Profile/` |
| Setting | `BrickBot/Modules/Setting/` |
