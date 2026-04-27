import { BaseModuleService } from '@/shared/services/baseModuleService';
import type {
  NewRecordingFramePayload,
  RecordingFrameInfo,
  RecordingInfo,
} from '../types';

class RecordingService extends BaseModuleService {
  constructor() { super('RECORDING'); }

  list(profileId: string): Promise<{ recordings: RecordingInfo[] }> {
    return this.send('LIST', { profileId });
  }

  get(profileId: string, id: string): Promise<RecordingInfo | null> {
    return this.send('GET', { profileId, id });
  }

  create(
    profileId: string,
    args: { name: string; description?: string; windowTitle?: string; intervalMs?: number; frames: NewRecordingFramePayload[] },
  ): Promise<RecordingInfo> {
    return this.send('CREATE', { profileId, ...args });
  }

  updateMetadata(profileId: string, id: string, name: string, description?: string): Promise<RecordingInfo> {
    return this.send('UPDATE_METADATA', { profileId, id, name, description });
  }

  delete(profileId: string, id: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, id });
  }

  listFrames(profileId: string, recordingId: string): Promise<{ frames: RecordingFrameInfo[] }> {
    return this.send('LIST_FRAMES', { profileId, recordingId });
  }

  /** Load a single frame's image bytes (base64). */
  getFrame(profileId: string, recordingId: string, frameIndex: number): Promise<RecordingFrameInfo | null> {
    return this.send('GET_FRAME', { profileId, recordingId, frameIndex });
  }
}

export const recordingService = new RecordingService();
