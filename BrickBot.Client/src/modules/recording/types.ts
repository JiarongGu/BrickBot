/** Mirror of BrickBot.Modules.Recording.Models.RecordingInfo. */
export interface RecordingInfo {
  id: string;
  name: string;
  description?: string;
  windowTitle?: string;
  width: number;
  height: number;
  frameCount: number;
  intervalMs: number;
  /** ISO-8601. */
  createdAt: string;
  updatedAt: string;
}

export interface RecordingFrameInfo {
  id: string;
  frameIndex: number;
  width: number;
  height: number;
  capturedAt: string;
  /** Only populated by GET_FRAME — not on LIST_FRAMES. */
  imageBase64?: string;
}

export interface NewRecordingFramePayload {
  imageBase64: string;
  capturedAt?: string;
}
