import { BaseModuleService } from '@/shared/services/baseModuleService';

export interface ActionsChangedPayload {
  actions: string[];
}

class ActionService extends BaseModuleService {
  constructor() {
    super('RUNNER');
  }

  list(): Promise<{ actions: string[] }> {
    return this.send('LIST_ACTIONS');
  }

  invoke(name: string): Promise<{ success: boolean }> {
    return this.send('INVOKE_ACTION', { name });
  }
}

export const actionService = new ActionService();
