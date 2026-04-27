import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { WindowInfo } from '../types';

class CaptureService extends BaseModuleService {
  constructor() { super('CAPTURE'); }

  listWindows(): Promise<WindowInfo[]> {
    return this.send('LIST_WINDOWS');
  }

  /** Grab one frame from the given window and return it as a PNG (base64-encoded) plus pixel dimensions. */
  grabPng(windowHandle: number): Promise<{ pngBase64: string; width: number; height: number }> {
    return this.send('GRAB_PNG', { windowHandle });
  }
}

export const captureService = new CaptureService();
