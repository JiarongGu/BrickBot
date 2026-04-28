# AI Assistant Guide — BrickBot

**Version:** 0.2
**Last Updated:** 2026-04-29

## Changelog

- **0.2 (2026-04-29)** — Detection v3 rewrite: 3-tier split (Definition / Model / Samples) with
  per-sample object boxes, IoU diagnostics, and a separate `DetectionModel` artifact. Added
  composite (AND/OR) detection kind, `inverse` flag, `maxHit` cap, and ROI offset modes
  (inset / relative). New host primitive `vision.waitStable`. Per-profile input delivery mode
  (SendInput / PostMessage / PostMessageWithPos). See D-008 (updated) and D-009 (new).
- **0.1 (2026-04-27)** — Initial project scaffold + locked decisions D-001..D-007.

> Mandatory rules are in `../CLAUDE.md` (auto-loaded). This file holds reference content: skills table, architecture quick-patterns, and documentation map.

---

## Skills — Complete Reference

### Code Generation (10 skills)

| Skill | Usage | Generates |
|-------|-------|-----------|
| `/backend-service` | `Name Module Deps Methods` | C# service + interface + DI + events |
| `/backend-facade` | `Name Module Services` | Thin IPC facade (delegation only) |
| `/ipc-service` | `Name Module Methods` | TypeScript IPC service + singleton |
| `/react-component` | `Name type features` | Component + BEM CSS + hooks |
| `/error-with-i18n` | `CODE params "en msg" "cn msg"` | OperationException + en.json + cn.json |
| `/event-handler` | `Name Module SourceEvents Target` | C# event consolidation handler |
| `/ipc-message-pair` | `Module MessageType ...` | Backend handler + Frontend method |
| `/batch-operation` | `Module Op EntityType Params` | SQL batch + Facade + Frontend |
| `/file-watcher` | `Name Module Path Filters Events` | FileSystemWatcher + disposal |
| `/service-registration` | `Module Interface Impl Lifecycle` | DI registration in ServiceExtensions.cs |

### Discovery & Documentation (11 skills)

| Skill | Usage | What It Does |
|-------|-------|--------------|
| `/skill-loader` | `"task description"` | Routes to relevant code-gen skills |
| `/doc-loader` | `"task" scope` | Routes to relevant docs by scope |
| `/pattern-finder` | `PatternType Module?` | Gives Glob/Grep commands for pattern |
| `/caveman` | `[lite\|full\|ultra]` | Token-optimized terse communication |
| `/post-feature` | (no args) | Audits git diff, suggests doc updates |
| `/doc-update-guide` | `ChangeType Details` | Updates this file with versioning |
| `/doc-update-reference` | `EntryType Details` | Updates KEYWORDS_INDEX.md |
| `/doc-update-technical` | `Document UpdateType Details` | Updates ADVANCED_PATTERNS / DESIGN_DECISIONS |
| `/doc-monitor` | `CheckType Scope` | Audits docs for broken links, redundancy |
| `/doc-cleanup` | `Operation Target Details` | Removes redundant docs |
| `/doc-optimize` | `Document Operation Details` | Splits oversized docs |
| `/release-notes` | `[from-tag] [to-ref]` | Auto-generates release notes from git log |

> Skill examples reference the BrickBot codebase (paths/modules) until BrickBot has equivalents. Translate `Mod` → `Capture`/`Vision`/`Script`/etc. when applying patterns.

---

## Architecture Quick-Patterns

 See `core/DESIGN_DECISIONS.md` for full rationale.

### Backend Service

```csharp
public class CaptureService : ICaptureService {
    private readonly ICaptureRepository _repository;
    private readonly IProfileEventBus _eventBus;

    public async Task<CaptureFrame> GrabAsync() {
        var frame = await _repository.GrabAsync();
        await _eventBus.EmitAsync(ModuleNames.CAPTURE, CaptureEvents.FRAME_GRABBED, frame);
        return frame;
    }
}
```

Generate with: `/backend-service CaptureService Capture ICaptureRepository,IProfileEventBus GrabAsync`

### Facade (IPC delegation only)

```csharp
public class CaptureFacade : BaseFacade {
    private readonly ICaptureService _service;

    private async Task<CaptureFrame> HandleAsync(IpcRequest req) {
        return await _service.GrabAsync();  // No logic here
    }
}
```

### Frontend IPC Service

```typescript
export class CaptureService extends BaseModuleService {
  async grab(profileId: string): Promise<CaptureFrame> {
    return this.sendMessage('GRAB', profileId);
  }
}
export const captureService = new CaptureService();
```

### UI Rules

- **BEM naming**: `.component-name__element--modifier`
- **Font sizes**: 12px or 14px only
- **Colors**: CSS variables only (`var(--color-*)`)
- **Conditionals**: Use `classNames()` library

---

## Documentation Map

| Need | Load This |
|------|-----------|
| Find code/files | [KEYWORDS_INDEX.md](KEYWORDS_INDEX.md) |
| Architecture constraints | [DESIGN_DECISIONS.md](core/DESIGN_DECISIONS.md) |
| Non-automatable patterns | [ADVANCED_PATTERNS.md](core/ADVANCED_PATTERNS.md) |
| Project layout | [PROJECT_STRUCTURE.md](core/PROJECT_STRUCTURE.md) |
| Testing patterns | [TESTING_GUIDE.md](ai-assistant/TESTING_GUIDE.md) |
| Backend reference | [keywords/BACKEND.md](keywords/BACKEND.md) |
| Frontend reference | [keywords/FRONTEND.md](keywords/FRONTEND.md) |
