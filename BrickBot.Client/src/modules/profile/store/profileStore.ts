import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { Profile } from '../types';

interface ProfileStoreState {
  profiles: Profile[];
  activeProfileId: string | undefined;
  loading: boolean;
}

interface ProfileStoreActions {
  setAll: (profiles: Profile[], activeId: string | undefined) => void;
  upsert: (profile: Profile) => void;
  remove: (id: string) => void;
  setActive: (id: string | undefined) => void;
  setLoading: (loading: boolean) => void;
}

export const useProfileStore = create<ProfileStoreState & ProfileStoreActions>()(
  immer((set) => ({
    profiles: [],
    activeProfileId: undefined,
    loading: false,

    setAll: (profiles, activeId) =>
      set((s) => {
        s.profiles = profiles;
        s.activeProfileId = activeId || undefined;
      }),

    upsert: (profile) =>
      set((s) => {
        const idx = s.profiles.findIndex((p) => p.id === profile.id);
        if (idx >= 0) s.profiles[idx] = profile;
        else s.profiles.push(profile);
      }),

    remove: (id) =>
      set((s) => {
        s.profiles = s.profiles.filter((p) => p.id !== id);
        if (s.activeProfileId === id) s.activeProfileId = s.profiles[0]?.id;
      }),

    setActive: (id) =>
      set((s) => {
        s.activeProfileId = id;
      }),

    setLoading: (loading) =>
      set((s) => {
        s.loading = loading;
      }),
  })),
);
