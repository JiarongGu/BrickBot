import { BaseModuleService } from '@/shared/services/baseModuleService';

class TemplateService extends BaseModuleService {
  constructor() { super('TEMPLATE'); }

  list(profileId: string): Promise<{ templates: string[] }> {
    return this.send('LIST', { profileId });
  }

  /** Save a base64-encoded PNG as `{name}.png` in the profile's templates dir. */
  save(profileId: string, name: string, pngBase64: string): Promise<{ success: boolean; path: string }> {
    return this.send('SAVE', { profileId, name, pngBase64 });
  }

  delete(profileId: string, name: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, name });
  }
}

export const templateService = new TemplateService();
