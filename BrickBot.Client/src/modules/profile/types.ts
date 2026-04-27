// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)

export interface Profile {
  id: string;
  name: string;
  description?: string;
  color?: string;
  gameName?: string;
  thumbnail?: string;
}

export interface CreateProfileRequest {
  name: string;
  description?: string;
  color?: string;
  gameName?: string;
}

export interface UpdateProfileRequest {
  id: string;
  name?: string;
  description?: string;
  color?: string;
  gameName?: string;
  thumbnail?: string;
}

export interface ProfileListResponse {
  profiles: Profile[];
  activeProfileId: string;
}

export interface WindowMatchRule {
  strategy: 'title' | 'titleClass' | 'process';
  titlePattern?: string;
  className?: string;
  processName?: string;
}

export interface RoiSettings {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface CaptureSettings {
  mode: 'winRT' | 'bitBlt';
  targetFps: number;
  defaultRoi?: RoiSettings;
}

export interface ScriptSettings {
  entryFile?: string;
  autoStart: boolean;
  tickIntervalMs: number;
}

export interface ProfileConfiguration {
  profileId: string;
  windowMatch: WindowMatchRule;
  capture: CaptureSettings;
  script: ScriptSettings;
  uiHints: Record<string, string>;
}
