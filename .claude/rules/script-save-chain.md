# Script Save / Run Chain (TypeScript phase)

User scripts are authored in **TypeScript** but the runner executes **CommonJS JavaScript**.
The frontend transpiles via Monaco's bundled TS worker — backend stores both files; runner only
ever loads the compiled `.js` sidecar.

## Save chain (frontend → backend, must stay in sync)

1. **`ScriptsView.tsx`** — Save button triggers `saveSelected(profileId)`.
2. **`scriptOperations.saveSelected`** — calls `transpileTypeScript(source, name)` first,
   surfaces blocking errors via `setDiagnostics` and throws if any `severity === 'error'`.
3. **`typescriptTranspiler.transpileTypeScript`** — gets the Monaco TS worker, calls
   `getEmitOutput` + `getSyntacticDiagnostics` + `getSemanticDiagnostics`. Compiler options
   are configured once in `getMonaco()`: `module: CommonJS`, `target: ES2020`, `strict: false`.
4. **`scriptService.save`** — IPC `SCRIPT.SAVE` with `{ profileId, kind, name, tsSource, jsSource }`.
5. **`ScriptFacade.Save`** — pulls `tsSource` + `jsSource` from payload, calls
   `IScriptFileService.Save(...)`. Throws `PAYLOAD_FIELD_MISSING` if either is absent.
6. **`ScriptFileService.Save`** — writes `{name}.ts` + `{name}.js` side-by-side under
   `data/profiles/{id}/scripts/{main|library}/`.

## Run chain (Runner → engine)

1. **`RunnerService.Start`** — calls `LoadCompiledMain(profileId, mainName)` (throws
   `SCRIPT_NOT_COMPILED` if the .js sidecar is missing) and builds a `ScriptRunRequest`
   with a `LibraryResolver` closure that calls `LoadCompiledLibrary(profileId, libName)`.
2. **`JintScriptEngine.Execute`** — runs `init.js` + `combat.js`, installs `require()`
   that:
   - For `'brickbot'` returns the host module (vision, input, combat, ctx, log, wait, …).
   - For everything else, normalizes the id (strip `./`, `/lib/`, `.ts`/`.js`) and calls
     `LibraryResolver`. Caches results, supports cyclic deps via partial-export pattern.
3. **Main script** runs as a CommonJS module so TS-emitted `require()` + `exports` work.
   Globals (`vision`, `input`, etc.) remain accessible.

## Adding a new host primitive (atomic — both must change)

1. Add a method to `HostApi.cs` (camelCase to match JS).
2. Wrap it as an ergonomic global in `StdLib.cs` (`InitScript`).
3. Update `Modules/Script/Resources/brickbot.d.ts` so Monaco can suggest it.
4. **Skipping step 3** = users get autocomplete without the new method but it works at runtime.
   **Skipping step 2** = `__host.foo()` works but `foo()` does not. Don't.

## Why this design

- **Frontend transpile** — Monaco already bundles a full TS language service, so we get
  a free transpiler with zero extra deps and zero embedded resources. Diagnostics surface
  inline before save.
- **Compiled-only run** — runner never invokes a TS compiler, so a Run is fast + offline-safe.
- **Lazy `require()`** — libraries no longer pre-load alphabetically; the engine pulls them
  as the main script asks for them. Unused libraries cost zero load time.

## CRITICAL — `CatchClrExceptions` predicate

`JintScriptEngine` configures `options.CatchClrExceptions(ex => ex is not OperationException
&& ex is not OperationCanceledException)`. If you ever change this:
- Returning `true` for `OperationException` will swallow our error codes inside Jint and
  the facade returns a generic JS error instead of `ErrorDetails`.
- Returning `false` for unexpected exceptions will propagate them to the caller raw,
  bypassing `SCRIPT_RUNTIME_ERROR` / `SCRIPT_SYNTAX_ERROR` mapping.

## brickbot event / action / trigger surface

The `brickbot` global (defined in `StdLib.InitScript`) is the runtime's event-bus + named-action
registry + declarative-trigger surface. It is single-threaded — every handler runs on the
script thread; cross-thread coordination happens through `IScriptDispatcher`.

```ts
brickbot.on('frame', (f) => /* ... */);
brickbot.action('cast.fireball', () => input.key(0x46));
brickbot.when(() => ctx.get<number>('hp') < 30, () => heal(), { cooldownMs: 1000 });
brickbot.runForever({ tickMs: 16 });   // pumps frames, drains UI invocations, fires triggers
```

### Action invocation chain (UI → engine)

1. **`ActionsPanel`** subscribes to `SCRIPT.ACTIONS_CHANGED` (push event from `ScriptDispatcher`)
   and lists registered actions.
2. **Click "Run"** → `actionService.invoke(name)` → IPC `RUNNER.INVOKE_ACTION { name }`.
3. **`RunnerFacade`** → `IRunnerService.InvokeAction(name)` → `ScriptDispatcher.EnqueueInvocation(name)`.
4. The dispatcher validates the name against the registered list (throws `RUNNER_ACTION_NOT_FOUND`
   if unknown — covers both unregistered names AND no-active-run state because the registered
   list is empty when no run exists).
5. **Engine tick** (next iteration of `brickbot.runForever`) calls `host.tryDequeueAction()` →
   `ScriptDispatcher.TryDequeueInvocation()` → invokes the registered JS function.

### Lifecycle invariants

- `RunnerService.Start` calls `_dispatcher.Reset()` BEFORE the new engine boots.
- `RunnerService.RunLoop`'s `finally` block calls `_dispatcher.Reset()` so a faulted run
  doesn't leak stale registrations.
- A script that doesn't call `brickbot.runForever()` simply never has its actions invoked —
  the registry pushes via `host.publishActions()` at registration time but no tick drains
  the queue. Document this if user reports "I clicked Run and nothing happened".

### Adding a new built-in event

1. Add the event name to the `BbBuiltInEvent` union in `brickbot.d.ts`.
2. Emit it from `StdLib.InitScript` at the appropriate point in `runForever()`.
3. If the event payload is structured, declare a `BbXxxPayload` interface in `brickbot.d.ts`
   and add an overload to `BbBrickbotApi.on`.
