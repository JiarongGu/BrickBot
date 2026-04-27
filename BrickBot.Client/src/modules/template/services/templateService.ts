import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { TemplateInfo } from '../types';

class TemplateService extends BaseModuleService {
  constructor() { super('TEMPLATE'); }

  /** List all templates (with name + description metadata) for a profile. */
  list(profileId: string): Promise<{ templates: TemplateInfo[] }> {
    return this.send('LIST', { profileId });
  }

  /**
   * Save a base64-encoded PNG with metadata. Pass an empty `id` to create a new row;
   * pass an existing id to overwrite the image and metadata in place.
   */
  save(
    profileId: string,
    args: { id?: string; name: string; description?: string; pngBase64: string },
  ): Promise<TemplateInfo> {
    return this.send('SAVE', { profileId, ...args });
  }

  /** Update name / description without re-uploading the image. */
  updateMetadata(
    profileId: string,
    id: string,
    name: string,
    description?: string,
  ): Promise<TemplateInfo> {
    return this.send('UPDATE_METADATA', { profileId, id, name, description });
  }

  delete(profileId: string, id: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, id });
  }
}

export const templateService = new TemplateService();
