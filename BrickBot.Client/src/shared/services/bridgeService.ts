import { v4 as uuidv4 } from 'uuid';
import type { BridgeMessage, BridgeNotification, BridgeResponse, ModuleName } from '@/shared/types/bridge';
import { eventBus } from './eventBus';

type Resolver = (response: BridgeResponse) => void;

class BridgeService {
  private readonly handlers = new Map<string, Resolver>();
  private readonly requestTimeoutMs = 30_000;

  constructor() {
    this.initialize();
  }

  send<T>(args: { module: ModuleName; type: string; profileId?: string; payload?: unknown }): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const id = uuidv4();
      const message: BridgeMessage = { id, ...args };

      this.handlers.set(id, (response) => {
        if (response.success) {
          resolve(response.data as T);
        } else {
          const err = new Error(response.error ?? 'Unknown IPC error') as Error & {
            errorDetails?: BridgeResponse['errorDetails'];
          };
          err.errorDetails = response.errorDetails;
          reject(err);
        }
      });

      const webview = window.chrome?.webview;
      if (!webview) {
        this.handlers.delete(id);
        reject(new Error('WebView2 bridge unavailable (running outside host?)'));
        return;
      }
      webview.postMessage(JSON.stringify(message));

      setTimeout(() => {
        if (this.handlers.has(id)) {
          this.handlers.delete(id);
          reject(new Error(`IPC timeout after ${this.requestTimeoutMs}ms (${args.module}/${args.type})`));
        }
      }, this.requestTimeoutMs);
    });
  }

  private initialize() {
    const webview = window.chrome?.webview;
    if (!webview) return;
    webview.addEventListener('message', (e) => {
      try {
        // Backend sends via PostWebMessageAsString → e.data is a JSON string.
        // If a future path uses PostWebMessageAsJson, e.data is already an object.
        const parsed: BridgeResponse | BridgeNotification =
          typeof e.data === 'string' ? JSON.parse(e.data) : (e.data as BridgeResponse | BridgeNotification);

        if (parsed.category === 'IPC') {
          const handler = this.handlers.get(parsed.id);
          if (handler) {
            handler(parsed);
            this.handlers.delete(parsed.id);
          }
        } else if (parsed.category === 'NOTIFICATION') {
          eventBus.emit(parsed);
        }
      } catch (err) {
        console.error('Failed to parse IPC message', err, e.data);
      }
    });
  }
}

export const bridgeService = new BridgeService();
