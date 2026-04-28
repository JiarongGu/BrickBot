// Frontend mirror of BrickBot/Modules/Detection/Models/.
// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase)

/**
 * Locked-in detection kinds:
 *   • `tracker` — moving sprite / character location (OpenCV KCF / CSRT / MIL).
 *   • `pattern` — static element appearance via ORB descriptors. Background-invariant.
 *   • `text`    — OCR text (Tesseract). Buff names, status banners, quest text.
 *   • `bar`     — HP / MP / cooldown meter; reads fill ratio.
 */
export type DetectionKind =
  | 'tracker'
  | 'pattern'
  | 'text'
  | 'bar'
  | 'composite';

/** Boolean op for composite detections. */
export type CompositeOp = 'and' | 'or';

/** Mode for interpreting DetectionRoi.x/y/w/h when fromDetectionId is set. */
export type RoiOffsetMode = 'inset' | 'relative';

export type ColorSpace = 'rgb' | 'hsv';

export type AnchorOrigin =
  | 'topLeft' | 'topCenter' | 'topRight'
  | 'midLeft' | 'center' | 'midRight'
  | 'bottomLeft' | 'bottomCenter' | 'bottomRight';

export type FillDirection = 'leftToRight' | 'rightToLeft' | 'topToBottom' | 'bottomToTop';

export const ANCHOR_ORIGINS: AnchorOrigin[] = [
  'topLeft', 'topCenter', 'topRight',
  'midLeft', 'center', 'midRight',
  'bottomLeft', 'bottomCenter', 'bottomRight',
];

export interface DetectionRoi {
  x: number;
  y: number;
  w: number;
  h: number;
  anchor?: AnchorOrigin;
  fromDetectionId?: string;
  /** How x/y/w/h are interpreted when fromDetectionId is set. Default 'inset' (back-compat). */
  offsetMode?: RoiOffsetMode;
}

export interface CompositeOptions {
  op: CompositeOp;
  detectionIds: string[];
}

export interface RgbColor {
  r: number;
  g: number;
  b: number;
}

// ============================================================================
//  Per-kind options — RUNTIME KNOBS only. Trained artifacts live on DetectionModel.
// ============================================================================

export type TrackerAlgorithm = 'kcf' | 'csrt' | 'mil';

export interface TrackerOptions {
  algorithm: TrackerAlgorithm;
  reacquireOnLost: boolean;
}

export interface PatternOptions {
  loweRatio: number;
  minConfidence: number;
  maxRuntimeKeypoints: number;
}

export interface TextOptions {
  language: string;
  pageSegMode: number;
  matchRegex?: string;
  minConfidence: number;
  binarize: boolean;
  upscaleFactor: number;
}

export interface BarOptions {
  anchorPatternId?: string;
  fillColor: RgbColor;
  tolerance: number;
  colorSpace: ColorSpace;
  direction: FillDirection;
  lineThreshold: number;
  insetLeftPct: number;
  insetRightPct: number;
}

// ============================================================================
//  Output bindings
// ============================================================================

export interface DetectionOverlay {
  enabled: boolean;
  color: string;
  label?: string;
}

export type DetectionOutputType = 'boolean' | 'number' | 'text' | 'bbox' | 'bboxes' | 'point';

export interface DetectionStability {
  minDurationMs: number;
  tolerance: number;
}

export interface DetectionOutput {
  ctxKey?: string;
  event?: string;
  eventOnChangeOnly: boolean;
  overlay?: DetectionOverlay;
  type?: DetectionOutputType;
  stability?: DetectionStability;
}

export interface DetectionDefinition {
  id: string;
  name: string;
  kind: DetectionKind;
  group?: string;
  enabled: boolean;
  /** Runtime SEARCH region — where the runner looks. Distinct from training samples'
   *  per-sample object boxes. Null = whole frame. */
  roi?: DetectionRoi;
  tracker?: TrackerOptions;
  pattern?: PatternOptions;
  text?: TextOptions;
  bar?: BarOptions;
  composite?: CompositeOptions;
  /** Flip the result's `found` flag. Saves boilerplate vs `!detect.run(x).found`. */
  inverse?: boolean;
  /** Auto-disable after N successful runs in current Run. Null/0 = unlimited. */
  maxHit?: number;
  output: DetectionOutput;
  /** Annotated by the LIST endpoint — true when a trained model file exists for this id. */
  hasModel?: boolean;
}

// ============================================================================
//  DetectionModel — compiled trainer output (separate from DetectionDefinition)
// ============================================================================

export interface TrackerModelData {
  initFramePng: string;
  initX: number;
  initY: number;
  initW: number;
  initH: number;
}

export interface PatternModelData {
  descriptors: string;
  keypointCount: number;
  templateWidth: number;
  templateHeight: number;
  embeddedPng: string;
}

export interface TextModelData {
  boxX: number;
  boxY: number;
  boxW: number;
  boxH: number;
  embeddedPng: string;
}

export interface BarModelData {
  boxX: number;
  boxY: number;
  boxW: number;
  boxH: number;
  fillColor: RgbColor;
  tolerance: number;
  direction: FillDirection;
  lineThreshold: number;
  embeddedPng: string;
}

export interface CompositeModelData {
  op: CompositeOp;
  detectionIds: string[];
}

export interface DetectionModel {
  id: string;
  detectionId: string;
  kind: DetectionKind;
  version: number;
  trainedAt: string;
  sampleCount: number;
  positiveCount: number;
  negativeCount: number;
  meanError: number;
  meanIoU: number;
  summary: string;
  tracker?: TrackerModelData;
  pattern?: PatternModelData;
  text?: TextModelData;
  bar?: BarModelData;
  composite?: CompositeModelData;
}

// ============================================================================
//  Result + training
// ============================================================================

export interface ResultBox {
  x: number; y: number; w: number; h: number; cx: number; cy: number;
}

export interface DetectionResult {
  id: string;
  name: string;
  kind: DetectionKind;
  found: boolean;
  durationMs: number;
  value?: number;
  match?: ResultBox;
  confidence?: number;
  strip?: ResultBox;
  text?: string;
}

export interface TrainingSample {
  imageBase64: string;
  label: string;
  /** Per-sample object box — where the object IS in this specific frame. Distinct from
   *  the runtime search ROI on the definition. */
  objectBox?: DetectionRoi;
  /** Tracker only: marks this sample as the init frame. */
  isInit?: boolean;
}

export interface PredictedBox {
  x: number; y: number; w: number; h: number;
}

export interface TrainingDiagnostic {
  label: string;
  predicted: string;
  error: number;
  predictedBox?: PredictedBox;
  iou: number;
}

export interface TrainingResult {
  definition?: DetectionDefinition;
  model?: DetectionModel;
  diagnostics: TrainingDiagnostic[];
  summary: string;
}

export interface TrainingSampleInfo {
  id: string;
  detectionId: string;
  label?: string;
  note?: string;
  capturedAt: string;
  width: number;
  height: number;
  imageBase64?: string;
  objectBox?: DetectionRoi;
  isInit: boolean;
}

/** Wire-compatible with BrickBot.Modules.Detection.Services.NewTrainingSample. */
export interface NewTrainingSamplePayload {
  id?: string;
  imageBase64: string;
  label?: string;
  note?: string;
  objectBox?: DetectionRoi;
  isInit?: boolean;
}

// ============================================================================
//  Defaults + UI labels
// ============================================================================

export const DETECTION_KIND_LABEL: Record<DetectionKind, string> = {
  tracker: 'Tracker (moving element)',
  pattern: 'Pattern (visual feature)',
  text: 'Text (OCR)',
  bar: 'Bar (HP / MP / cooldown)',
  composite: 'Composite (AND / OR)',
};

export function newDetection(kind: DetectionKind): DetectionDefinition {
  const base: DetectionDefinition = {
    id: '',
    name: '',
    kind,
    enabled: true,
    output: { eventOnChangeOnly: true },
  };
  switch (kind) {
    case 'tracker':
      base.tracker = {
        algorithm: 'kcf',
        reacquireOnLost: true,
      };
      break;
    case 'pattern':
      base.pattern = {
        loweRatio: 0.75,
        minConfidence: 0.20,
        maxRuntimeKeypoints: 500,
      };
      break;
    case 'text':
      base.text = {
        language: 'eng',
        pageSegMode: 7,
        minConfidence: 60,
        binarize: true,
        upscaleFactor: 2.0,
      };
      break;
    case 'bar':
      base.bar = {
        fillColor: { r: 220, g: 30, b: 30 },
        tolerance: 60,
        colorSpace: 'rgb',
        direction: 'leftToRight',
        lineThreshold: 0.4,
        insetLeftPct: 0.30,
        insetRightPct: 0.18,
      };
      break;
    case 'composite':
      base.composite = {
        op: 'and',
        detectionIds: [],
      };
      break;
  }
  return base;
}
