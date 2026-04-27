import { BaseModuleService } from '@/shared/services/baseModuleService';
import type { WindowInfo } from '../types';

class CaptureService extends BaseModuleService {
  constructor() { super('CAPTURE'); }

  listWindows(): Promise<WindowInfo[]> {
    return this.send('LIST_WINDOWS');
  }

  /**
   * Grab one frame from the given window and return it as a PNG (base64-encoded) plus pixel
   * dimensions. <c>maxDimension</c> caps the longest side — backend uniformly downscales any
   * larger frame. Default 1920 keeps 4K-monitor preview/IPC cheap; pass 0 for full res when
   * pixel-perfect template crops matter.
   */
  grabPng(
    windowHandle: number,
    maxDimension: number = 1920,
  ): Promise<{ pngBase64: string; width: number; height: number }> {
    return this.send('GRAB_PNG', { windowHandle, maxDimension });
  }
}

export const captureService = new CaptureService();
