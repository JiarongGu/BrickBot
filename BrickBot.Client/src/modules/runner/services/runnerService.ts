import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { RunnerState } from '../types';

class RunnerService extends BaseModuleService {
  constructor() { super('RUNNER'); }

  getState(): Promise<RunnerState> {
    return this.send('GET_STATE');
  }

  start(args: { windowHandle: number; profileId: string; mainName: string; templateRoot?: string }): Promise<RunnerState> {
    return this.send('START', args);
  }

  stop(): Promise<RunnerState> {
    return this.send('STOP');
  }
}

export const runnerService = new RunnerService();
