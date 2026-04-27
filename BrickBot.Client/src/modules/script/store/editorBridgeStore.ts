import { create } from 'zustand';
import type { editor as MonacoEditor } from 'monaco-editor';

/**
 * Bridge between non-editor UI (CapturePanel, future ROI picker, color sampler) and the
 * Monaco instance owned by ScriptsView. Tools call insertAtCursor() — ScriptsView's
 * onMount handler registers the editor here and wires the actual edit. This keeps tools
 * decoupled from Monaco internals; they just emit text.
 */
interface EditorBridgeState {
  editor: MonacoEditor.IStandaloneCodeEditor | undefined;
  /** Total number of inserts performed. Tools may watch this to flash UI on success. */
  insertCount: number;
}

interface EditorBridgeActions {
  setEditor: (editor: MonacoEditor.IStandaloneCodeEditor | undefined) => void;
  insertAtCursor: (text: string) => boolean;
}

export const useEditorBridgeStore = create<EditorBridgeState & EditorBridgeActions>((set, get) => ({
  editor: undefined,
  insertCount: 0,

  setEditor: (editor) => set({ editor }),

  insertAtCursor: (text) => {
    const { editor } = get();
    if (!editor) return false;
    const selection = editor.getSelection();
    if (!selection) return false;
    editor.executeEdits('brickbot.bridge', [
      { range: selection, text, forceMoveMarkers: true },
    ]);
    editor.focus();
    set((s) => ({ insertCount: s.insertCount + 1 }));
    return true;
  },
}));
