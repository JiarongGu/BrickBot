import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { GlobalSettings, ThemeMode } from '../types';

interface SettingsStoreState {
  settings: GlobalSettings | undefined;
  availableLanguages: string[];
  resolvedTheme: 'light' | 'dark';
  loading: boolean;
}

interface SettingsStoreActions {
  setSettings: (settings: GlobalSettings) => void;
  patchSettings: (patch: Partial<GlobalSettings>) => void;
  setAvailableLanguages: (codes: string[]) => void;
  setResolvedTheme: (mode: 'light' | 'dark') => void;
  setLoading: (loading: boolean) => void;
}

function resolveTheme(mode: ThemeMode | undefined): 'light' | 'dark' {
  if (mode === 'light') return 'light';
  if (mode === 'dark') return 'dark';
  // auto / undefined → follow system
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  return 'dark';
}

export const useSettingsStore = create<SettingsStoreState & SettingsStoreActions>()(
  immer((set) => ({
    settings: undefined,
    availableLanguages: [],
    resolvedTheme: 'dark',
    loading: false,

    setSettings: (settings) =>
      set((s) => {
        s.settings = settings;
        s.resolvedTheme = resolveTheme(settings.theme);
      }),

    patchSettings: (patch) =>
      set((s) => {
        if (!s.settings) return;
        s.settings = { ...s.settings, ...patch };
        if (patch.theme !== undefined) {
          s.resolvedTheme = resolveTheme(patch.theme);
        }
      }),

    setAvailableLanguages: (codes) =>
      set((s) => {
        s.availableLanguages = codes;
      }),

    setResolvedTheme: (mode) =>
      set((s) => {
        s.resolvedTheme = mode;
      }),

    setLoading: (loading) =>
      set((s) => {
        s.loading = loading;
      }),
  })),
);
