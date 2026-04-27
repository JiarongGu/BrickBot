import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { ScriptFileInfo, ScriptKind } from '../types';

interface SelectedScript {
  kind: ScriptKind;
  name: string;
  source: string;
  /** dirty = unsaved local edits since the last load/save */
  dirty: boolean;
}

interface ScriptStoreState {
  files: ScriptFileInfo[];
  selected: SelectedScript | undefined;
  loading: boolean;
}

interface ScriptStoreActions {
  setFiles: (files: ScriptFileInfo[]) => void;
  setSelected: (selected: SelectedScript | undefined) => void;
  setSource: (source: string) => void;
  markSaved: () => void;
  setLoading: (loading: boolean) => void;
}

export const useScriptStore = create<ScriptStoreState & ScriptStoreActions>()(
  immer((set) => ({
    files: [],
    selected: undefined,
    loading: false,

    setFiles: (files) => set((s) => { s.files = files; }),

    setSelected: (selected) => set((s) => { s.selected = selected; }),

    setSource: (source) => set((s) => {
      if (s.selected) {
        s.selected.source = source;
        s.selected.dirty = true;
      }
    }),

    markSaved: () => set((s) => { if (s.selected) s.selected.dirty = false; }),

    setLoading: (loading) => set((s) => { s.loading = loading; }),
  })),
);
