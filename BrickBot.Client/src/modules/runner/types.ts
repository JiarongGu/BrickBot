// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)
export type RunnerStatus = 'idle' | 'running' | 'stopping' | 'faulted';

/** Why the runner left the Running state. Mirrors BrickBot.Modules.Runner.Models.StopReason. */
export type StopReason =
  | 'none' | 'user' | 'timeout' | 'event' | 'context'
  | 'script' | 'completed' | 'faulted';

/** Mirrors BrickBot.Modules.Runner.Services.StopWhenOptions. */
export interface StopWhenOptions {
  timeoutMs?: number;
  onEvent?: string;
  ctxKey?: string;
  /** 'eq' | 'neq' | 'gt' | 'gte' | 'lt' | 'lte'. Numeric ops parse via parseFloat; falls back to string equality. */
  ctxOp?: string;
  ctxValue?: string;
}

export interface RunnerState {
  status: RunnerStatus;
  errorMessage?: string;
  stoppedReason: StopReason;
  stoppedDetail?: string;
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
  className: string;
  x: number;
  y: number;
  width: number;
  height: number;
  /** PNG base64 of the process exe icon (32×32). May be absent for system processes. */
  iconBase64?: string;
}
