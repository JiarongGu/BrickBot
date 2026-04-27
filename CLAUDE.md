# CLAUDE.md — BrickBot Mandatory Rules

> Auto-loaded every session. Keep this file short — details belong in `.claude/rules/`.

**Project**: BrickBot (game automation app — fishing first, action games later)

---

## 0. Per-Task Gate (BLOCKING — do this before ANY code exploration)

Follow the full 5-step protocol in `.claude/rules/skills-workflow.md`. Summary:

1. **Invoke 4 core skills** in parallel: `/doc-loader`, `/skill-loader`, `/pattern-finder`, `/caveman`
2. **Invoke code-gen skills** that `skill-loader` returns in its INVOKE list
3. **Read EVERY doc** that doc-loader routes to — not just AI_GUIDE.md. Confirm in response text.
4. **Run ALL search commands** from pattern-finder
5. **Print skill match summary** (INVOKE/SKIP from skill-loader — must be visible in response)
6. **Only then** explore code or write anything

**If you skip steps 3–5 you WILL generate non-conforming code.**
Never hand-write what a skill generates (service, facade, IPC, component, error, registration).
Skip this gate only when doing a direct continuation of the same task in the same scope.

**All 4 core skills are ATOMIC — invoke all or none.** Never invoke skill-loader alone and skip doc-loader/pattern-finder/caveman. "Simple fix" is not a valid skip reason.

**RE-INVOKE on scope change:** If a follow-up request changes module, creates a new component/service, or touches a different tool — re-run **all 4 core skills** even mid-conversation. The cost of re-invoking is tiny vs. the cost of hand-writing buggy code.

---

## 1. Git Commits

**NEVER commit without explicit user approval.**
Always ask "Ready to commit?" and wait for a clear "yes".

---

## 2. Architecture (non-negotiable)

```
Backend  → ALL heavy operations: capture, vision, input, scripting, file I/O
Frontend → UI only, NO image/data processing
Facades  → Thin delegation only — no business logic, no events
Services → Business logic + event emission
```

**Module boundaries** — never access another module's repository directly. Always call through that module's facade.

**Tech stack** (locked):
- Backend: .NET 10 WinForms + WebView2, Microsoft.Extensions.DependencyInjection, SQLite + Dapper + FluentMigrator, Costura.Fody, Microsoft.Extensions.Caching.Memory, OpenCvSharp4 (vision), Jint (JavaScript scripting), Windows.Graphics.Capture (high-FPS) with BitBlt fallback.
- Frontend: React 19 + TypeScript + Vite + Ant Design 6 + Zustand (immer) + react-i18next + Monaco editor (script editing).

**Module list**: `Capture`, `Vision`, `Input`, `Template`, `Script`, `Runner`, `Profile`, `Setting`.

**Error handling** — throw `OperationException("ERROR_CODE", params)`. Add message to BOTH `Languages/en.json` AND `Languages/cn.json`.

**Events** — services emit events (inject `IProfileEventBus`). Facades never emit events.

**Frontend data** — use `undefined` for absent data, never `null`. `null` is only for React render returns (`if (!data) return null`).

**Enum serialization** — C# enums serialize as **camelCase strings** via `JsonStringEnumConverter(CamelCase)`. TypeScript enum types MUST use camelCase: `'running'` not `'Running'`. See `.claude/rules/enum-serialization.md`.

**Font sizes** — 12px or 14px ONLY. No 13px. No exceptions. See `.claude/rules/ui-design-rules.md`.

---

## 3. Testing (non-negotiable)

**After every bug fix or new feature, write tests.**

Backend: xUnit + FluentAssertions + Moq. Frontend: Jest + React Testing Library.

---

## 4. Work Style

**Skills → Agents → RAG → Manual** (in that order)

### Code-Gen Skills (use these, don't write by hand)

| Building | Skill |
|----------|-------|
| Backend service | `/backend-service Name Module Deps Methods` |
| IPC facade | `/backend-facade Name Module Services` |
| Frontend IPC service | `/ipc-service Name Module Methods` |
| React component | `/react-component Name type features` |
| Error + i18n | `/error-with-i18n CODE params "en msg" "cn msg"` |
| Backend + frontend IPC | `/ipc-message-pair Module MessageType ...` |
| Batch SQL operation | `/batch-operation Module Op EntityType Params` |
| DI registration | `/service-registration Module Interface Impl Lifecycle` |

Manual code is ONLY for unique business logic inside a skill-generated structure.

### Discovery Tools (mandatory first step — see Section 0)

- `/doc-loader "task" scope` — routes to relevant docs by scope keyword
- `/skill-loader "task"` — routes to relevant code-gen skills
- `/pattern-finder PatternType Module` — gives Glob/Grep commands for existing patterns

### After Finishing

1. Write tests (Section 3)
2. Build succeeds (`dotnet build BrickBot.slnx`, `npx tsc --noEmit` in `BrickBot.Client`)
3. Run `/post-feature` for non-trivial changes (new IPC, component, store field, multi-file)
4. **Evolve the system** — if you discovered a multi-file wiring chain (3+ files edited in sequence), create `.claude/rules/{pattern}.md` so the next session doesn't re-discover it.
5. Ask user: "Ready to commit?"

---

## 5. Rules & Memory

- **Project rules** → `.claude/rules/*.md` (repo-committed, shared across sessions and users)
- **Global memory** → `~/.claude/projects/*/memory/` (personal/user-specific only)

Save workflow feedback, conventions, and corrections to `.claude/rules/`, NOT global memory. Global memory is reserved for user-specific preferences (role, communication style).

---

## 6. Scripting

User scripts use **JavaScript via Jint** (pure-C#, no native deps). See D-007 in `docs/core/DESIGN_DECISIONS.md`.

**Two script kinds per profile**, picked up from `data/profiles/{id}/scripts/`:
- `main/*.js` — top-level orchestrators. The Runner picks ONE main script and executes it.
- `library/*.js` — helpers, monitors, skill defs. **All** library files load into the engine BEFORE main runs (alphabetical order).

**Run order** (single Jint engine, single thread): `init.js` → `combat.js` (BT primitives) → every `library/*.js` (alphabetical) → selected `main/<name>.js`.

**Shared context (`ctx`)** — per-Run JSON-backed key/value store, exposed as JS global. Library "monitor" functions write (`ctx.set('hp', 70)`), main reads (`ctx.get('hp')`). Methods: `set / get / has / delete / keys / snapshot / inc`. Cleared each Run.

**Files**: `Modules/Script/Services/{StdLib.cs, HostApi.cs, ScriptContext.cs, ScriptFileService.cs}`. New host primitive → method on `HostApi.cs` + wrapper in `StdLib.InitScript`. Never expose `__host` directly to user scripts.

---

## 7. UI Tabs

Top-level navigation (`App.tsx`):
- **Runner** — pick window + main script, start/stop, log. No code editor here.
- **Scripts** — Main/Library file browser + Monaco editor. Toolbar **Capture** button opens the Capture panel as a slide-in for mid-edit template authoring.
- **Tools** — grid of utility cards, each opens in a `<SlideInScreen>`. Today: Profiles, Captures. Future: ROI picker, color sampler.
- **Settings** — theme / language / log-level / window-state-reset (global).

Active profile dropdown lives in the header. Its "Manage profiles" link routes to the Tools tab.

---

## 8. Capture & Templates Workflow

Two distinct capture paths:
- **Streaming (Run time)** — used by `vision.*` while scripts run. `Windows.Graphics.Capture` primary, `BitBlt` fallback. Shared frame buffer, decoupled FPS.
- **One-shot (authoring time)** — `IScreenshotService.GrabPng(handle)` captures one frame, PNG-encodes. IPC: `CAPTURE.GRAB_PNG`. Used by the `<CapturePanel>`.

**Template CRUD** in `Modules/Template/Services/TemplateFileService.cs` via the `TEMPLATE` IPC module (`LIST`, `SAVE`, `DELETE`). Files persist at `data/profiles/{id}/templates/*.png` so scripts can `vision.find('bobber.png')`.

**Frontend `CapturePanel`** ([BrickBot.Client/src/modules/template/components/CapturePanel.tsx](BrickBot.Client/src/modules/template/components/CapturePanel.tsx)) does cropping client-side via HTML5 Canvas — backend never holds an in-flight frame. Hover surfaces `(x, y)` + `rgb(...)` for reading coords straight into a script.
