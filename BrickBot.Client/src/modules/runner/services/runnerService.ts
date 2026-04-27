import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { RunnerState, StopWhenOptions } from '../types';

class RunnerService extends BaseModuleService {
  constructor() { super('RUNNER'); }

  getState(): Promise<RunnerState> {
    return this.send('GET_STATE');
  }

  /**
   * Start a run. Pass <c>stopWhen</c> to wire auto-stop conditions:
   * <code>{ timeoutMs: 30 * 60 * 1000, onEvent: 'goalReached', ctxKey: 'fishCount', ctxOp: 'gte', ctxValue: '100' }</code>
   * All fields optional and combined with OR. Manual Stop button always works.
   */
  start(args: {
    windowHandle: number;
    profileId: string;
    mainName: string;
    templateRoot?: string;
    stopWhen?: StopWhenOptions;
  }): Promise<RunnerState> {
    return this.send('START', args);
  }

  stop(): Promise<RunnerState> {
    return this.send('STOP');
  }
}

export const runnerService = new RunnerService();
