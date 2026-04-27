import { scriptService } from '../services/scriptService';
import { registerScriptTypings, transpileTypeScript } from '../services/typescriptTranspiler';
import { useScriptStore } from '../store/scriptStore';
import type { ScriptKind } from '../types';

export async function loadScripts(profileId: string): Promise<void> {
  const store = useScriptStore.getState();
  store.setLoading(true);
  try {
    const { files } = await scriptService.list(profileId);
    store.setFiles(files);
    // Refresh Monaco's host typings whenever we touch a profile so users get up-to-date
    // autocomplete even if the host API surface changes between runs.
    void scriptService.getTypes().then(({ source }) => registerScriptTypings(source));
  } finally {
    store.setLoading(false);
  }
}

export async function selectScript(profileId: string, kind: ScriptKind, name: string): Promise<void> {
  const { source } = await scriptService.get(profileId, kind, name);
  useScriptStore.getState().setSelected({ kind, name, source, dirty: false, diagnostics: [] });
}

export async function saveSelected(profileId: string): Promise<void> {
  const { selected } = useScriptStore.getState();
  if (!selected) return;

  const { js, diagnostics } = await transpileTypeScript(selected.source, `${selected.kind}_${selected.name}`);
  useScriptStore.getState().setDiagnostics(diagnostics);

  const blockingErrors = diagnostics.filter((d) => d.severity === 'error');
  if (blockingErrors.length > 0) {
    const first = blockingErrors[0];
    throw new Error(`TypeScript error at line ${first.line}: ${first.message}`);
  }

  await scriptService.save(profileId, selected.kind, selected.name, selected.source, js);
  useScriptStore.getState().markSaved();
  await loadScripts(profileId);
}

export async function createScript(profileId: string, kind: ScriptKind, name: string, source: string): Promise<void> {
  // Transpile the starter template so the runner has a valid .js sidecar from day one.
  const { js } = await transpileTypeScript(source, `${kind}_${name}`);
  await scriptService.save(profileId, kind, name, source, js);
  await loadScripts(profileId);
  useScriptStore.getState().setSelected({ kind, name, source, dirty: false, diagnostics: [] });
}

export async function deleteScript(profileId: string, kind: ScriptKind, name: string): Promise<void> {
  await scriptService.delete(profileId, kind, name);
  const { selected } = useScriptStore.getState();
  if (selected && selected.kind === kind && selected.name === name) {
    useScriptStore.getState().setSelected(undefined);
  }
  await loadScripts(profileId);
}

export const STARTER_TEMPLATES: Record<ScriptKind, string> = {
  main: `// Top-level orchestrator — the Runner picks one main script to execute.
//
// Train detections in the Detections tab, then read them in this script via
// detect.run('name'). Click the "Detection" button in the toolbar above to
// insert a reference at the cursor. Add library scripts later with require()
// when this main needs shared helpers.

import { brickbot, ctx, detect, log } from 'brickbot';

// Sample detections every tick and stash the values in ctx for triggers below.
brickbot.on('tick', () => {
  // Example — uncomment after training a "hp-bar" detection:
  // ctx.set('hp', detect.run('hp-bar').value);
});

// React when ctx state crosses a threshold. Cooldowns avoid spamming the action.
brickbot.when(
  () => (ctx.get<number>('hp') ?? 1) < 0.3,
  () => log('low hp — heal here'),
  { cooldownMs: 1000 },
);

// Pumps frames + drains UI invocations + evaluates when() triggers each tick.
brickbot.runForever({ tickMs: 50, autoDetect: true });
`,
  library: `// Library script — pulled in lazily when a main (or another library) requires it.
//
// Library scripts have full access to the host surface:
//   - listen for engine + custom events with brickbot.on(...)
//   - emit custom events with brickbot.emit(...)
//   - read/write the per-Run shared store via ctx.{set, get, has, inc, snapshot}
//   - register actions invokable from the Tools tab via brickbot.action(name, fn)
//
// Top-level code runs ONCE the first time another script requires this file. So putting
// brickbot.on(...) at the top level wires the handler for the rest of the Run.

import { brickbot, ctx, vision } from 'brickbot';

// Sample HP each frame and write it to ctx so the main script (or other libraries)
// can decide what to do without re-running the vision call.
brickbot.on('frame', () => {
  const sample = vision.colorAt(100, 50);
  const isHealthy = sample.r > 100;
  ctx.set('hp', isHealthy ? 'ok' : 'low');

  // Emit a custom event when state crosses a threshold — main can react via
  // brickbot.on('low-hp', () => heal()).
  if (!isHealthy) brickbot.emit('low-hp', { r: sample.r });
});

// Optional: expose a helper for main to call directly.
export function getHp(): 'ok' | 'low' {
  return ctx.get<'ok' | 'low'>('hp', 'ok');
}
`,
};
