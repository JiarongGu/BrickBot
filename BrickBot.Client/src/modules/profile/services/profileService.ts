import { BaseModuleService } from '@/shared/services/baseModuleService';
import type {
  CreateProfileRequest,
  Profile,
  ProfileConfiguration,
  ProfileListResponse,
  UpdateProfileRequest,
} from '../types';

class ProfileService extends BaseModuleService {
  constructor() {
    super('PROFILE');
  }

  getAll(): Promise<ProfileListResponse> {
    return this.send('GET_ALL');
  }

  getActive(): Promise<Profile | null> {
    return this.send('GET_ACTIVE');
  }

  getById(id: string): Promise<Profile | null> {
    return this.send('GET_BY_ID', { id });
  }

  create(request: CreateProfileRequest): Promise<Profile> {
    return this.send('CREATE', { request });
  }

  update(request: UpdateProfileRequest): Promise<{ success: boolean }> {
    return this.send('UPDATE', { request });
  }

  delete(id: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { id });
  }

  switchTo(id: string): Promise<{ success: boolean }> {
    return this.send('SWITCH', { id });
  }

  duplicate(sourceId: string, newName: string): Promise<Profile> {
    return this.send('DUPLICATE', { sourceId, newName });
  }

  getConfig(id: string): Promise<ProfileConfiguration | null> {
    return this.send('GET_CONFIG', { id });
  }

  updateConfig(config: ProfileConfiguration): Promise<{ success: boolean }> {
    return this.send('UPDATE_CONFIG', { config });
  }

  clearTemp(id: string): Promise<{ success: boolean; id: string }> {
    return this.send('CLEAR_TEMP', { id });
  }

  createScratchFolder(id: string, prefix?: string): Promise<{ path: string }> {
    return this.send('CREATE_SCRATCH_FOLDER', { id, prefix });
  }
}

export const profileService = new ProfileService();
