import i18n from '@/shared/i18n';
import { eventBus } from '@/shared/services/eventBus';
import { settingService } from '../services/settingService';
import { useSettingsStore } from '../store/settingsStore';
import type { GlobalSettings, ThemeMode, LogLevel, AnnotationLevel } from '../types';

let backendChangeUnsub: (() => void) | undefined;

export async function initSettings(): Promise<void> {
  const store = useSettingsStore.getState();
  store.setLoading(true);

  try {
    const [settings, languages] = await Promise.all([
      settingService.getGlobal(),
      settingService.getAvailableLanguages(),
    ]);

    store.setSettings(settings);
    store.setAvailableLanguages(languages.languages);

    await loadAndApplyLanguage(settings.language);
    applyResolvedThemeToBody();

    if (!backendChangeUnsub) {
      backendChangeUnsub = eventBus.onModule('SETTING', 'GLOBAL_SETTINGS_CHANGED', async (payload) => {
        const next = payload as GlobalSettings;
        const prev = useSettingsStore.getState().settings;
        useSettingsStore.getState().setSettings(next);
        applyResolvedThemeToBody();
        if (prev?.language !== next.language) {
          await loadAndApplyLanguage(next.language);
        }
      });
    }
  } finally {
    store.setLoading(false);
  }
}

export async function setTheme(theme: ThemeMode): Promise<void> {
  // Optimistic local update so the UI reacts instantly.
  useSettingsStore.getState().patchSettings({ theme });
  applyResolvedThemeToBody();
  await settingService.updateField('theme', theme);
}

export async function setLanguage(language: string): Promise<void> {
  useSettingsStore.getState().patchSettings({ language });
  await loadAndApplyLanguage(language);
  await settingService.updateField('language', language);
}

export async function setLogLevel(level: LogLevel): Promise<void> {
  useSettingsStore.getState().patchSettings({ logLevel: level });
  await settingService.updateField('logLevel', level);
}

export async function setAnnotationLevel(level: AnnotationLevel): Promise<void> {
  useSettingsStore.getState().patchSettings({ annotationLevel: level });
  await settingService.updateField('annotationLevel', level);
}

export async function resetAll(): Promise<void> {
  const { settings } = await settingService.resetGlobal();
  useSettingsStore.getState().setSettings(settings);
  await loadAndApplyLanguage(settings.language);
  applyResolvedThemeToBody();
}

export async function resetWindowState(): Promise<void> {
  await settingService.resetWindowState();
}

async function loadAndApplyLanguage(code: string): Promise<void> {
  try {
    const result = await settingService.getLanguage(code);
    if (result.success && result.language) {
      i18n.addResourceBundle(code, 'translation', result.language.translations, true, true);
      await i18n.changeLanguage(code);
    }
  } catch (err) {
    console.error(`Failed to load language ${code}`, err);
  }
}

function applyResolvedThemeToBody(): void {
  const { resolvedTheme } = useSettingsStore.getState();
  if (typeof document !== 'undefined') {
    // theme-colors.css reads [data-theme] from <html>, not <body>.
    document.documentElement.setAttribute('data-theme', resolvedTheme);
  }
}
