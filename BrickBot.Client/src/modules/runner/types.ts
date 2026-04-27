// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)
export type RunnerStatus = 'idle' | 'running' | 'stopping' | 'faulted';

export interface RunnerState {
  status: RunnerStatus;
  errorMessage?: string;
}

export interface LogEntry {
  timestamp: string;
  level: 'info' | 'warn' | 'error';
  message: string;
}

export interface WindowInfo {
  handle: number;
  title: string;
  processName: string;
  x: number;
  y: number;
  width: number;
  height: number;
}
