import type { DetectionRoi } from '../../types';

/**
 * Training-wizard-local types. Distinct from the saved `DetectionDefinition` shape:
 * these describe in-memory samples being annotated + labeled before training runs.
 */
export interface SampleRecord {
  id: string;
  imageBase64: string;
  width: number;
  height: number;
  /** Kind-dependent label — see TrainingSample.cs for semantics. */
  label: string;
  capturedAt: number;
  /** Per-sample object box (annotation). Undefined = no box drawn yet. */
  objectBox?: DetectionRoi;
  /** Tracker only: marks this sample as the init frame. Mutually exclusive across samples. */
  isInit?: boolean;
}

/** Filter pill state for the samples strip. */
export type SampleFilter = 'all' | 'unlabeled' | 'labeled' | 'unboxed';
