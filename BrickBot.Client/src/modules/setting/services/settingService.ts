import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { GlobalSettings, LanguagePack } from '../types';

class SettingService extends BaseModuleService {
  constructor() {
    super('SETTING');
  }

  getGlobal(): Promise<GlobalSettings> {
    return this.send('GET_GLOBAL');
  }

  updateGlobal(patch: Partial<Pick<GlobalSettings, 'theme' | 'language' | 'logLevel' | 'annotationLevel'>>): Promise<{
    success: boolean;
    settings: GlobalSettings;
  }> {
    return this.send('UPDATE_GLOBAL', patch);
  }

  updateField(key: string, value: string): Promise<{ success: boolean; key: string; value: string }> {
    return this.send('UPDATE_FIELD', { key, value });
  }

  resetGlobal(): Promise<{ success: boolean; settings: GlobalSettings }> {
    return this.send('RESET_GLOBAL');
  }

  getFile(filename: string): Promise<{ success: boolean; content: string | null }> {
    return this.send('GET_FILE', { filename });
  }

  saveFile(filename: string, content: string): Promise<{ success: boolean; filename: string }> {
    return this.send('SAVE_FILE', { filename, content });
  }

  deleteFile(filename: string): Promise<{ success: boolean; filename: string }> {
    return this.send('DELETE_FILE', { filename });
  }

  fileExists(filename: string): Promise<{ exists: boolean }> {
    return this.send('FILE_EXISTS', { filename });
  }

  listFiles(): Promise<{ files: string[] }> {
    return this.send('LIST_FILES');
  }

  getLanguage(languageCode: string): Promise<{ success: boolean; language: LanguagePack | null }> {
    return this.send('GET_LANGUAGE', { languageCode });
  }

  getAvailableLanguages(): Promise<{ languages: string[] }> {
    return this.send('GET_AVAILABLE_LANGUAGES');
  }

  resetWindowState(): Promise<{ success: boolean }> {
    return this.send('RESET_WINDOW_STATE');
  }
}

export const settingService = new SettingService();
