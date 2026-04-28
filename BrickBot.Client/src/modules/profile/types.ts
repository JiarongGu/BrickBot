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

/** How the runner delivers keyboard / mouse events to the target window.
 *  • sendInput          — Win32 SendInput; OS-level. Steals focus + cursor; works against any
 *                         focused window. Default for compat.
 *  • postMessage        — PostMessage(WM_KEYDOWN/WM_LBUTTONDOWN). Background-friendly: target
 *                         window does NOT need focus, cursor isn't stolen. Some games (raw-input
 *                         FPS titles) ignore WM_KEY* — fall back to sendInput.
 *  • postMessageWithPos — postMessage + temporary SetWindowPos kick. Workaround for games that
 *                         consult window state inside their input handler. */
export type InputMode = 'sendInput' | 'postMessage' | 'postMessageWithPos';

export interface InputSettings {
  mode: InputMode;
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
  input: InputSettings;
  script: ScriptSettings;
  uiHints: Record<string, string>;
}
