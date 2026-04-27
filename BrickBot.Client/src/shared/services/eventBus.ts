import type { BridgeNotification, ModuleName } from '@/shared/types/bridge';

type Listener = (n: BridgeNotification) => void;

class EventBus {
  private readonly listeners = new Set<Listener>();

  on(listener: Listener): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  onModule(module: ModuleName, type: string, listener: (payload: unknown) => void): () => void {
    return this.on((n) => {
      if (n.module === module && n.type === type) listener(n.payload);
    });
  }

  emit(notification: BridgeNotification): void {
    for (const l of this.listeners) {
      try {
        l(notification);
      } catch (err) {
        console.error('eventBus listener error', err);
      }
    }
  }
}

export const eventBus = new EventBus();
