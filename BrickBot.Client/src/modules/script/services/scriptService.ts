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

  /**
   * Persist a script. The frontend transpiles TypeScript → CommonJS JavaScript before
   * sending; the backend stores both files side-by-side and the runner executes the .js.
   */
  save(
    profileId: string,
    kind: ScriptKind,
    name: string,
    tsSource: string,
    jsSource: string,
  ): Promise<{ success: boolean; path: string }> {
    return this.send('SAVE', { profileId, kind, name, tsSource, jsSource });
  }

  delete(profileId: string, kind: ScriptKind, name: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, kind, name });
  }

  /** Returns the embedded brickbot.d.ts so Monaco can offer host-API autocomplete. */
  getTypes(): Promise<{ source: string }> {
    return this.send('GET_TYPES');
  }
}

export const scriptService = new ScriptService();
