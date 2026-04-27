import { loader, type Monaco } from '@monaco-editor/react';

export interface ScriptDiagnostic {
  message: string;
  /** 1-based line number in the source. */
  line: number;
  /** 1-based column in the source. */
  column: number;
  severity: 'error' | 'warning';
}

export interface TranspileResult {
  /** The CommonJS JavaScript output. Empty string when the source fails to parse. */
  js: string;
  /** Combined syntactic + semantic diagnostics from the TS language service. */
  diagnostics: ScriptDiagnostic[];
}

let monacoPromise: Promise<Monaco> | undefined;
let configured = false;

/**
 * Lazily resolves the singleton Monaco namespace and applies our TypeScript compiler defaults.
 * The returned promise is cached so subsequent callers don't re-init.
 */
function getMonaco(): Promise<Monaco> {
  if (!monacoPromise) {
    monacoPromise = loader.init().then((monaco) => {
      if (!configured) {
        const ts = monaco.languages.typescript;
        ts.typescriptDefaults.setCompilerOptions({
          target: ts.ScriptTarget.ES2020,
          module: ts.ModuleKind.CommonJS,
          moduleResolution: ts.ModuleResolutionKind.NodeJs,
          esModuleInterop: true,
          allowSyntheticDefaultImports: true,
          strict: false,
          noImplicitAny: false,
          // Don't override `lib` — let Monaco bundle the standard ES + DOM lib files so
          // user scripts get autocomplete for Date, JSON, Math, Array, Promise, etc.
          // (DOM types are inert at type-check time even though Jint isn't a browser; we
          // surface "use wait() not setTimeout" via brickbot.d.ts conventions, not lib stripping.)
        });
        ts.typescriptDefaults.setDiagnosticsOptions({
          noSemanticValidation: false,
          noSyntaxValidation: false,
        });
        configured = true;
      }
      return monaco;
    });
  }
  return monacoPromise;
}

/**
 * Registers a virtual .d.ts file so Monaco can offer host-API autocomplete + type checks
 * against `vision`, `input`, `combat`, `ctx`, etc. Idempotent — call on every profile load
 * because the brickbot.d.ts content is fetched fresh from the backend.
 */
export async function registerScriptTypings(brickbotDts: string): Promise<void> {
  const monaco = await getMonaco();
  const fileName = 'file:///node_modules/@types/brickbot/index.d.ts';
  // addExtraLib replaces any prior content under the same fileName.
  monaco.languages.typescript.typescriptDefaults.addExtraLib(brickbotDts, fileName);
}

/**
 * Transpile a TypeScript source string to CommonJS JavaScript using the bundled
 * Monaco TS worker. The worker also reports syntactic + semantic diagnostics so
 * the caller can surface errors before saving / running.
 */
export async function transpileTypeScript(
  source: string,
  scriptName: string,
): Promise<TranspileResult> {
  const monaco = await getMonaco();
  const uri = monaco.Uri.parse(`file:///scripts/${scriptName}.ts`);
  let model = monaco.editor.getModel(uri);
  if (!model) {
    model = monaco.editor.createModel(source, 'typescript', uri);
  } else {
    model.setValue(source);
  }

  const workerFactory = await monaco.languages.typescript.getTypeScriptWorker();
  const client = await workerFactory(uri);
  const fileName = uri.toString();

  const [output, syntactic, semantic] = await Promise.all([
    client.getEmitOutput(fileName),
    client.getSyntacticDiagnostics(fileName),
    client.getSemanticDiagnostics(fileName),
  ]);

  const jsFile = output.outputFiles.find((f: { name: string }) => f.name.endsWith('.js'));
  const diagnostics: ScriptDiagnostic[] = [...syntactic, ...semantic].map((d) =>
    toDiagnostic(d, source),
  );

  return { js: jsFile?.text ?? '', diagnostics };
}

interface RawDiagnostic {
  start?: number;
  length?: number;
  messageText: string | { messageText: string; next?: unknown };
  category?: number;
}

function toDiagnostic(raw: RawDiagnostic, source: string): ScriptDiagnostic {
  const message = typeof raw.messageText === 'string' ? raw.messageText : raw.messageText.messageText;
  const offset = raw.start ?? 0;
  const { line, column } = offsetToLineColumn(source, offset);
  // TS DiagnosticCategory: 0 = warning, 1 = error, 2 = suggestion, 3 = message.
  const severity: ScriptDiagnostic['severity'] = raw.category === 1 ? 'error' : 'warning';
  return { message, line, column, severity };
}

function offsetToLineColumn(source: string, offset: number): { line: number; column: number } {
  let line = 1;
  let column = 1;
  for (let i = 0; i < offset && i < source.length; i += 1) {
    if (source.charCodeAt(i) === 10) {
      line += 1;
      column = 1;
    } else {
      column += 1;
    }
  }
  return { line, column };
}
