// Type definitions for the WebView2 IPC bridge.
// bridgeService contract.

export type ModuleName =
  | 'CAPTURE'
  | 'VISION'
  | 'INPUT'
  | 'TEMPLATE'
  | 'DETECTION'
  | 'RECORDING'
  | 'SCRIPT'
  | 'RUNNER'
  | 'PROFILE'
  | 'SETTING';

export interface BridgeMessage {
  id: string;
  module: ModuleName;
  type: string;
  profileId?: string;
  payload?: unknown;
}

export interface BridgeResponse {
  id: string;
  category: 'IPC';
  success: boolean;
  data?: unknown;
  error?: string;
  errorDetails?: { code: string; parameters?: Record<string, string> };
}

export interface BridgeNotification {
  category: 'NOTIFICATION';
  module: ModuleName;
  type: string;
  payload?: unknown;
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        addEventListener: (event: 'message', cb: (e: { data: string }) => void) => void;
        postMessage: (data: string) => void;
      };
    };
  }
}
