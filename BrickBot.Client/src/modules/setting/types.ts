// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)

export type ThemeMode = 'light' | 'dark' | 'auto';
export type AnnotationLevel = 'all' | 'more' | 'less' | 'off';
export type LogLevel = 'All' | 'Verbose' | 'Debug' | 'Info' | 'Warn' | 'Error' | 'Off';

export interface WindowSettings {
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  maximized: boolean;
}

export interface GlobalSettings {
  theme: ThemeMode;
  language: string;
  logLevel: LogLevel;
  annotationLevel: AnnotationLevel;
  lastUpdated: string;
  window: WindowSettings;
}

export interface LanguagePack {
  code: string;
  name: string;
  translations: Record<string, string>;
}
