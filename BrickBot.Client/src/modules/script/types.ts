// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)

export type ScriptKind = 'main' | 'library';

export interface ScriptFileInfo {
  kind: ScriptKind;
  name: string;
}

export interface ScriptDiagnostic {
  message: string;
  line: number;
  column: number;
  severity: 'error' | 'warning';
}
