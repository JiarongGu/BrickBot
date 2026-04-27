# Project Structure

> Living document. Update when adding/removing top-level folders or modules.

```
BrickBot/
├── BrickBot.slnx
├── CLAUDE.md                            Mandatory session rules
│
├── BrickBot/                            Main WinForms + WebView2 app
│   ├── BrickBot.csproj
│   ├── FodyWeavers.xml                  Costura assembly embedding config
│   ├── Program.cs                       Entry point → ApplicationBootstrapper
│   ├── Infrastructure/                  Cross-cutting bootstrap and host
│   │   ├── ApplicationBootstrapper.cs   Static Run(), wires up DI + WinForms
│   │   ├── ApplicationHost.cs           Owns ServiceProvider lifetime
│   │   └── OptimizedForm.cs             Main WinForms window + WebView2
│   ├── Modules/                         Feature modules (see list below)
│   ├── Languages/                       en.json, cn.json (i18n + error codes)
│   └── wwwroot/                         Built React output (embedded resource)
│
├── BrickBot.Client/                     React + Vite frontend
│   ├── package.json
│   ├── vite.config.ts                   Builds into ../BrickBot/wwwroot
│   ├── tsconfig.json
│   ├── index.html
│   └── src/
│       ├── index.tsx                    React root
│       ├── App.tsx                      App shell
│       ├── modules/                     Per-module UI (mirrors backend modules)
│       └── shared/                      bridgeService, eventBus, base hooks/types
│
├── BrickBot.Tests/                      xUnit + FluentAssertions + Moq
│   └── BrickBot.Tests.csproj
│
├── .claude/
│   ├── rules/                           Project-specific rules (committed)
│   ├── skills/                          Code-gen skill templates
│   └── settings.json                    Permissions
│
└── docs/                                Reference documentation (this folder)
    ├── README.md
    ├── AI_GUIDE.md                      Skills table, quick patterns, doc map
    ├── KEYWORDS_INDEX.md                Fast keyword → file lookup
    ├── CHANGELOG.md
    ├── core/                            Foundational docs
    │   ├── PROJECT_OVERVIEW.md
    │   ├── PROJECT_STRUCTURE.md         (this file)
    │   ├── DESIGN_DECISIONS.md
    │   ├── ADVANCED_PATTERNS.md
    │   └── DEVELOPMENT.md
    ├── ai-assistant/                    Assistant-facing reference
    │   ├── REFERENCE.md
    │   ├── TESTING_GUIDE.md
    │   └── TROUBLESHOOTING.md
    ├── keywords/                        Keyword → file lookup tables
    │   ├── BACKEND.md
    │   ├── FRONTEND.md
    │   ├── HOW_TO.md
    │   └── DOCUMENTATION.md
    ├── architecture/                    Detailed architecture diagrams (TBD)
    ├── features/                        Per-feature docs (TBD)
    ├── how-to/                          Step-by-step guides (TBD)
    └── changelogs/                      Per-release excerpts (TBD)
```

## Module folders

Each module under `BrickBot/Modules/<Name>/` has the same shape:

```
Modules/<Name>/
├── <Name>Facade.cs                      Thin IPC router (BaseFacade)
├── <Name>ServiceExtensions.cs           DI registration (AddXxxServices)
├── <Name>Events.cs                      Event name constants
├── Services/
│   ├── I<Name>Service.cs                Public interface
│   ├── <Name>Service.cs                 Business logic (emits events)
│   ├── I<Name>Repository.cs
│   └── <Name>Repository.cs              Dapper SQL access
├── Entities/                            DB row types
├── Mappers/                             Entity ↔ Model mapping
└── Models/                              Public DTOs (cross IPC boundary)
```

## Planned modules

| Module | Status |
|---|---|
| `Capture` | Planned |
| `Vision` | Planned |
| `Input` | Planned |
| `Template` | Planned |
| `Script` | Planned |
| `Runner` | Planned |
| `Profile` | Planned |
| `Setting` | Planned |
| `Core` | Planned (cross-cutting helpers, BaseFacade, OperationException, IProfileEventBus) |
