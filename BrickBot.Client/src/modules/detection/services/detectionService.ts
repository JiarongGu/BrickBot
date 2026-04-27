import { BaseModuleService } from '@/shared/services/baseModuleService';
import type {
  DetectionDefinition,
  DetectionKind,
  DetectionResult,
  NewTrainingSamplePayload,
  TrainingResult,
  TrainingSample,
  TrainingSampleInfo,
} from '../types';

class DetectionService extends BaseModuleService {
  constructor() { super('DETECTION'); }

  list(profileId: string): Promise<{ detections: DetectionDefinition[] }> {
    return this.send('LIST', { profileId });
  }

  get(profileId: string, id: string): Promise<DetectionDefinition | null> {
    return this.send('GET', { profileId, id });
  }

  /** Save a definition. Backend assigns an id (slug from name) when blank. */
  save(profileId: string, definition: DetectionDefinition): Promise<DetectionDefinition> {
    return this.send('SAVE', { profileId, definition });
  }

  delete(profileId: string, id: string): Promise<{ success: boolean }> {
    return this.send('DELETE', { profileId, id });
  }

  /**
   * Run an in-memory definition against a captured frame (PNG base64). Used by the editor's
   * live preview so the user can iterate without saving.
   */
  test(profileId: string, definition: DetectionDefinition, frameBase64: string): Promise<DetectionResult> {
    return this.send('TEST', { profileId, definition, frameBase64 });
  }

  /**
   * Train a detection of the given kind from labeled samples. Returns the suggested
   * definition + per-sample diagnostics. The suggested config can be inspected, edited,
   * and then saved via `save()`.
   */
  train(
    profileId: string,
    kind: DetectionKind,
    samples: TrainingSample[],
    seed?: DetectionDefinition,
  ): Promise<TrainingResult> {
    return this.send('TRAIN', { profileId, kind, samples, seed });
  }

  /** Suggest high-variance regions (candidate ROIs) from a stack of recorded frames. */
  suggestRois(
    frames: string[],
    maxResults: number = 5,
  ): Promise<{ suggestions: { x: number; y: number; w: number; h: number; score: number; reason: string }[] }> {
    return this.send('SUGGEST_ROIS', { frames, maxResults });
  }

  /**
   * Persist labeled training samples for a detection. Images are written to
   * data/profiles/{id}/training/{sampleId}.png and metadata to the TrainingSamples table.
   * Pass replaceExisting=true (default) to drop prior samples for the detection first.
   */
  saveSamples(
    profileId: string,
    detectionId: string,
    samples: NewTrainingSamplePayload[],
    replaceExisting: boolean = true,
  ): Promise<{ samples: TrainingSampleInfo[] }> {
    return this.send('SAVE_SAMPLES', { profileId, detectionId, samples, replaceExisting });
  }

  listSamples(
    profileId: string,
    detectionId: string,
    includeImages: boolean = false,
  ): Promise<{ samples: TrainingSampleInfo[] }> {
    return this.send('LIST_SAMPLES', { profileId, detectionId, includeImages });
  }

  deleteSample(profileId: string, sampleId: string): Promise<{ success: boolean }> {
    return this.send('DELETE_SAMPLE', { profileId, sampleId });
  }

  deleteSamplesForDetection(profileId: string, detectionId: string): Promise<{ success: boolean }> {
    return this.send('DELETE_SAMPLES_FOR_DETECTION', { profileId, detectionId });
  }
}

export const detectionService = new DetectionService();
