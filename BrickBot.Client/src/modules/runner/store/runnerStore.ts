import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { LogEntry, RunnerState, WindowInfo } from '../types';

interface RunnerStoreState {
  windows: WindowInfo[];
  selectedWindow: WindowInfo | undefined;
  /** Name (no extension) of the main script the user picked from the active profile. */
  selectedMain: string | undefined;
  /** Names (no extension) of every main/*.js in the active profile. Refreshed on profile load. */
  availableMains: string[];
  templateRoot: string;
  state: RunnerState;
  log: LogEntry[];
}

interface RunnerStoreActions {
  setWindows: (windows: WindowInfo[]) => void;
  selectWindow: (window: WindowInfo | undefined) => void;
  setAvailableMains: (mains: string[]) => void;
  setSelectedMain: (name: string | undefined) => void;
  setTemplateRoot: (path: string) => void;
  setState: (state: RunnerState) => void;
  appendLog: (entry: LogEntry) => void;
  clearLog: () => void;
}

export const useRunnerStore = create<RunnerStoreState & RunnerStoreActions>()(
  immer((set) => ({
    windows: [],
    selectedWindow: undefined,
    selectedMain: undefined,
    availableMains: [],
    templateRoot: '',
    state: { status: 'idle' },
    log: [],

    setWindows: (windows) => set((s) => { s.windows = windows; }),
    selectWindow: (window) => set((s) => { s.selectedWindow = window; }),
    setAvailableMains: (mains) => set((s) => {
      s.availableMains = mains;
      // If the previously selected main is gone, drop it; if nothing's picked yet, pick the first.
      if (s.selectedMain && !mains.includes(s.selectedMain)) s.selectedMain = undefined;
      if (!s.selectedMain && mains.length > 0) s.selectedMain = mains[0];
    }),
    setSelectedMain: (name) => set((s) => { s.selectedMain = name; }),
    setTemplateRoot: (path) => set((s) => { s.templateRoot = path; }),
    setState: (state) => set((s) => { s.state = state; }),
    appendLog: (entry) => set((s) => {
      s.log.push(entry);
      if (s.log.length > 500) s.log.splice(0, s.log.length - 500);
    }),
    clearLog: () => set((s) => { s.log = []; }),
  })),
);
