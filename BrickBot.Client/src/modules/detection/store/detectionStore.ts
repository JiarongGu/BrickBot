import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { DetectionDefinition, DetectionResult } from '../types';

interface DetectionStoreState {
  detections: DetectionDefinition[];
  /** Detection currently shown in the editor (may be unsaved/draft). */
  draft: DetectionDefinition | undefined;
  /** Result of the last live test, keyed by detection id (or '__draft' for unsaved). */
  lastResults: Record<string, DetectionResult>;
  loading: boolean;
}

interface DetectionStoreActions {
  setDetections: (defs: DetectionDefinition[]) => void;
  upsert: (def: DetectionDefinition) => void;
  remove: (id: string) => void;
  setDraft: (draft: DetectionDefinition | undefined) => void;
  patchDraft: (patch: Partial<DetectionDefinition>) => void;
  setResult: (id: string, result: DetectionResult) => void;
  setLoading: (loading: boolean) => void;
  reset: () => void;
}

export const DRAFT_ID = '__draft';

export const useDetectionStore = create<DetectionStoreState & DetectionStoreActions>()(
  immer((set) => ({
    detections: [],
    draft: undefined,
    lastResults: {},
    loading: false,

    setDetections: (defs) => set((s) => { s.detections = defs; }),

    upsert: (def) => set((s) => {
      const idx = s.detections.findIndex((d) => d.id === def.id);
      if (idx >= 0) s.detections[idx] = def;
      else s.detections.push(def);
    }),

    remove: (id) => set((s) => {
      s.detections = s.detections.filter((d) => d.id !== id);
      if (s.draft && s.draft.id === id) s.draft = undefined;
      delete s.lastResults[id];
    }),

    setDraft: (draft) => set((s) => { s.draft = draft; }),

    patchDraft: (patch) => set((s) => {
      if (!s.draft) return;
      Object.assign(s.draft, patch);
    }),

    setResult: (id, result) => set((s) => { s.lastResults[id] = result; }),

    setLoading: (loading) => set((s) => { s.loading = loading; }),

    reset: () => set((s) => {
      s.detections = [];
      s.draft = undefined;
      s.lastResults = {};
      s.loading = false;
    }),
  })),
);
