import { scriptService } from '../services/scriptService';
import { useScriptStore } from '../store/scriptStore';
import type { ScriptKind } from '../types';

export async function loadScripts(profileId: string): Promise<void> {
  const store = useScriptStore.getState();
  store.setLoading(true);
  try {
    const { files } = await scriptService.list(profileId);
    store.setFiles(files);
  } finally {
    store.setLoading(false);
  }
}

export async function selectScript(profileId: string, kind: ScriptKind, name: string): Promise<void> {
  const { source } = await scriptService.get(profileId, kind, name);
  useScriptStore.getState().setSelected({ kind, name, source, dirty: false });
}

export async function saveSelected(profileId: string): Promise<void> {
  const { selected } = useScriptStore.getState();
  if (!selected) return;
  await scriptService.save(profileId, selected.kind, selected.name, selected.source);
  useScriptStore.getState().markSaved();
  await loadScripts(profileId);
}

export async function createScript(profileId: string, kind: ScriptKind, name: string, source: string): Promise<void> {
  await scriptService.save(profileId, kind, name, source);
  await loadScripts(profileId);
  useScriptStore.getState().setSelected({ kind, name, source, dirty: false });
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
// This file runs AFTER all library/*.js files load, so any helper functions or skills
// defined there are available as globals here.
//
// Pattern: tick a behavior tree forever; library scripts update ctx between ticks.

const { Sequence, Selector, Action, Condition, runTree } = combat;

const tree = Sequence(
  // Re-run perception each tick so action decisions read fresh state from ctx.
  Action(() => { if (typeof updatePerception === 'function') updatePerception(); }),

  Selector(
    // Add your action priorities here:
    Action(() => log('idle...')),
  ),
);

runTree(tree, { intervalMs: 80 });
`,
  library: `// Library script — preloaded into the engine before main runs.
// Define helper functions / monitors / skill defs as globals; main reads from ctx.

globalThis.updatePerception = function () {
  // Sample game state into ctx for action scripts to consume.
  // Example: ctx.set('hp', vision.colorAt(100, 50).r > 100 ? 'ok' : 'low');
};
`,
};
