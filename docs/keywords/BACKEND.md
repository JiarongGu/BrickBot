# Backend Keyword Index

Fast lookup table for backend code. Update when adding services, facades, repositories, or extension points.

| Keyword | File | Notes |
|---|---|---|
| App entry | `BrickBot/Program.cs` | Calls `ApplicationBootstrapper.Run()` |
| Bootstrap / DI registration | `BrickBot/Infrastructure/ApplicationBootstrapper.cs` | Registers all `<Module>ServiceExtensions` |
| App host / lifetime | `BrickBot/Infrastructure/ApplicationHost.cs` | Owns `IServiceProvider` |
| Main window / WebView2 | `BrickBot/Infrastructure/OptimizedForm.cs` | — |
| Base facade | `BrickBot/Modules/Core/Ipc/BaseFacade.cs` | (planned) routes IPC requests |
| OperationException | `BrickBot/Modules/Core/Exceptions/OperationException.cs` | (planned) error code + i18n params |
| Profile event bus | `BrickBot/Modules/Core/Events/IProfileEventBus.cs` | (planned) services emit, facades never |
| Module registry | `BrickBot/Modules/Core/ModuleNames.cs` | (planned) string constants |

## Module index

| Module | Folder | Status |
|---|---|---|
| Capture | `BrickBot/Modules/Capture/` | Planned |
| Vision | `BrickBot/Modules/Vision/` | Planned |
| Input | `BrickBot/Modules/Input/` | Planned |
| Template | `BrickBot/Modules/Template/` | Planned |
| Script | `BrickBot/Modules/Script/` | Planned |
| Runner | `BrickBot/Modules/Runner/` | Planned |
| Profile | `BrickBot/Modules/Profile/` | Planned |
| Setting | `BrickBot/Modules/Setting/` | Planned |
