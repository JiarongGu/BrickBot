import { eventBus } from '@/shared/services/eventBus';
import { profileService } from '../services/profileService';
import { useProfileStore } from '../store/profileStore';
import type { CreateProfileRequest, Profile, UpdateProfileRequest } from '../types';

let unsubs: Array<() => void> = [];

export async function initProfiles(): Promise<void> {
  const store = useProfileStore.getState();
  store.setLoading(true);
  try {
    const list = await profileService.getAll();
    store.setAll(list.profiles, list.activeProfileId || undefined);

    if (unsubs.length === 0) {
      unsubs = [
        eventBus.onModule('PROFILE', 'CREATED', (p) => useProfileStore.getState().upsert(p as Profile)),
        eventBus.onModule('PROFILE', 'UPDATED', (p) => useProfileStore.getState().upsert(p as Profile)),
        eventBus.onModule('PROFILE', 'DUPLICATED', (p) => useProfileStore.getState().upsert(p as Profile)),
        eventBus.onModule('PROFILE', 'DELETED', (payload) => {
          const id = (payload as { id: string }).id;
          useProfileStore.getState().remove(id);
        }),
        eventBus.onModule('PROFILE', 'SWITCHED', (p) => {
          const profile = p as Profile;
          useProfileStore.getState().setActive(profile.id);
        }),
      ];
    }
  } finally {
    store.setLoading(false);
  }
}

export async function createProfile(request: CreateProfileRequest): Promise<Profile> {
  return profileService.create(request);
}

export async function updateProfile(request: UpdateProfileRequest): Promise<void> {
  await profileService.update(request);
}

export async function deleteProfile(id: string): Promise<void> {
  await profileService.delete(id);
}

export async function switchProfile(id: string): Promise<void> {
  await profileService.switchTo(id);
}

export async function duplicateProfile(sourceId: string, newName: string): Promise<Profile> {
  return profileService.duplicate(sourceId, newName);
}

export async function clearProfileTemp(id: string): Promise<void> {
  await profileService.clearTemp(id);
}
