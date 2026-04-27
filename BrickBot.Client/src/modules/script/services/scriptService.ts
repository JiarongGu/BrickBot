import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { ScriptFileInfo, ScriptKind } from '../types';

class ScriptService extends BaseModuleService {
  constructor() {
    super('SCRIPT');
  }

  list(profileId: string): Promise<{ files: ScriptFileInfo[] }> {
    return this.send('LIST', { profileId });
  }

  get(profileId: string, kind: ScriptKind, name: string): Promise<{ source: string }> {
    return this.send('GET', { profileId, kind, name });
  }

  save(profileId: string, kind: ScriptKind, name: string, source: string): Promise<{ success: boolean; path: string }> {
    return this.send('SAVE', { profileId, kind, name, source });
  }

  delete(profileId: string, kind: ScriptKind, name: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, kind, name });
  }
}

export const scriptService = new ScriptService();
