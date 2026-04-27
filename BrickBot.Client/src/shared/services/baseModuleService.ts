import type { ModuleName } from '@/shared/types/bridge';
import { bridgeService } from './bridgeService';

export class BaseModuleService {
  constructor(private readonly module: ModuleName) {}

  protected send<T>(type: string, payload?: unknown, profileId?: string): Promise<T> {
    return bridgeService.send<T>({ module: this.module, type, profileId, payload });
  }
}
