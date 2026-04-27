export { detectionService } from './services/detectionService';
export { useDetectionStore, DRAFT_ID } from './store/detectionStore';
export { DetectionsPanel } from './components/DetectionsPanel';
export { DetectionEditor } from './components/DetectionEditor';
export { TrainingPanel } from './components/TrainingPanel';
export { DetectionsView } from './components/DetectionsView';
export type {
  ColorPresenceOptions,
  DetectionDefinition,
  DetectionKind,
  DetectionOutput,
  DetectionOverlay,
  DetectionResult,
  DetectionRoi,
  EffectOptions,
  FeatureMatchOptions,
  ProgressBarOptions,
  ResultBox,
  RgbColor,
  TemplateOptions,
  TrainingDiagnostic,
  TrainingResult,
  TrainingSample,
  TrainingSampleInfo,
  NewTrainingSamplePayload,
} from './types';
export { DETECTION_KIND_LABEL, newDetection } from './types';
