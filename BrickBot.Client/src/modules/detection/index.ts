export { detectionService } from './services/detectionService';
export { useDetectionStore, DRAFT_ID } from './store/detectionStore';
export { DetectionsPanel } from './components/DetectionsPanel';
export { DetectionEditor } from './components/DetectionEditor';
export { TrainingPanel } from './components/TrainingPanel';
export { DetectionsView } from './components/DetectionsView';
export type {
  BarOptions,
  DetectionDefinition,
  DetectionKind,
  DetectionOutput,
  DetectionOverlay,
  DetectionResult,
  DetectionRoi,
  PatternOptions,
  ResultBox,
  RgbColor,
  TextOptions,
  TrackerAlgorithm,
  TrackerOptions,
  TrainingDiagnostic,
  TrainingResult,
  TrainingSample,
  TrainingSampleInfo,
  NewTrainingSamplePayload,
} from './types';
export { DETECTION_KIND_LABEL, newDetection } from './types';
