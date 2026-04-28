import { BaseModuleService } from '@/shared/services/baseModuleService';
import type {
  DetectionDefinition,
  DetectionKind,
  DetectionModel,
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
   * Run an in-memory definition against a captured frame. Pass `model` to preview a
   * trained-but-not-yet-saved candidate (TrainingPanel diagnostics); omit it to use the
   * persisted model on disk (live editor preview of a saved detection).
   */
  test(
    profileId: string,
    definition: DetectionDefinition,
    frameBase64: string,
    model?: DetectionModel,
  ): Promise<DetectionResult> {
    return this.send('TEST', { profileId, definition, frameBase64, model });
  }

  /**
   * Train a detection of the given kind from labeled samples. Returns a paired
   * (definition, model) — the definition holds runtime knobs, the model holds compiled
   * artifacts. Both must be saved together (`save()` + `saveModel()`) for the runner to
   * use the trained detection.
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
   * Persist labeled training samples for a detection. Each sample carries its own object
   * box (per-sample annotation). Pass replaceExisting=true (default) to drop prior samples.
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

  // ============================================================
  //  Detection model — compiled trainer output
  // ============================================================

  /** Load the trained model for a detection. Returns null when untrained. */
  getModel(profileId: string, detectionId: string): Promise<DetectionModel | null> {
    return this.send('GET_MODEL', { profileId, detectionId });
  }

  /** Persist a trained model. Overwrites any prior model for the same detection. */
  saveModel(profileId: string, model: DetectionModel): Promise<DetectionModel> {
    return this.send('SAVE_MODEL', { profileId, model });
  }

  /** Remove the trained model file. Definition + samples remain — useful to "untrain"
   *  without losing config. */
  deleteModel(profileId: string, detectionId: string): Promise<{ success: boolean }> {
    return this.send('DELETE_MODEL', { profileId, detectionId });
  }
}

export const detectionService = new DetectionService();
