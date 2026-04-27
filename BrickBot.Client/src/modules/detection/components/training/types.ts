/**
 * Training-wizard-local types. Distinct from the saved `DetectionDefinition` shape:
 * these describe in-memory samples being labeled before training runs.
 */
export interface SampleRecord {
  id: string;
  imageBase64: string;
  width: number;
  height: number;
  label: string;
  capturedAt: number;
}

/** Filter pill state for the samples strip. */
export type SampleFilter = 'all' | 'unlabeled' | 'labeled';
