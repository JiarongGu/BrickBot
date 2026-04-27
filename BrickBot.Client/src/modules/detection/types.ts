// Frontend mirror of BrickBot/Modules/Detection/Models/DetectionDefinition.cs.
// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase).

export type DetectionKind =
  | 'template'
  | 'progressBar'
  | 'colorPresence'
  | 'effect'
  | 'featureMatch'
  | 'region';

/** RGB threshold = literal color match. HSV = hue-window match, robust against lighting drift. */
export type ColorSpace = 'rgb' | 'hsv';

/** 9-point window-relative origin used by anchored ROIs. */
export type AnchorOrigin =
  | 'topLeft' | 'topCenter' | 'topRight'
  | 'midLeft' | 'center' | 'midRight'
  | 'bottomLeft' | 'bottomCenter' | 'bottomRight';

/** Direction the bar grows toward — drives the per-line scan in LinearFillRatio. */
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
  /** When set, x/y are offsets from the anchor on the current frame; w/h are absolute sizes. */
  anchor?: AnchorOrigin;
  /** When set, this ROI is the referenced detection's match bbox; x/y/w/h are inset margins
   *  (left, top, right, bottom). Composes one detection on top of another. */
  fromDetectionId?: string;
}

export interface RgbColor {
  r: number;
  g: number;
  b: number;
}

export interface TemplateOptions {
  /** Legacy: id of a Templates-table row. Trainer-built definitions populate `embeddedPng`. */
  templateName: string;
  /** Base64 PNG embedded directly in the detection — the trainer writes this. */
  embeddedPng?: string;
  minConfidence: number;
  scale: number;
  grayscale: boolean;
  pyramid: boolean;
  /** Match in Canny edge space — robust to color drift / variable fill / lighting. Implies grayscale. */
  edge: boolean;
}

export interface ProgressBarOptions {
  /** Optional — leave blank to source the bar bbox from the ROI directly (anchor / fromDetection). */
  templateName: string;
  embeddedPng?: string;
  minConfidence: number;
  templateEdge: boolean;
  scale: number;
  grayscale: boolean;
  pyramid: boolean;
  fillColor: RgbColor;
  tolerance: number;
  colorSpace: ColorSpace;
  /** Direction the bar grows toward. */
  direction: FillDirection;
  /** Per-line fill threshold (0..1) for the directional scan. */
  lineThreshold: number;
  insetLeftPct: number;
  insetRightPct: number;
}

export interface ColorPresenceOptions {
  color: RgbColor;
  tolerance: number;
  minArea: number;
  maxResults: number;
  colorSpace: ColorSpace;
}

export interface EffectOptions {
  threshold: number;
  autoBaseline: boolean;
  /** Trainer-pinned baseline image (base64 PNG). When set, runtime uses this instead of
   *  capturing the first runtime frame as baseline. */
  embeddedBaselinePng?: string;
  /** Edge-diff vs baseline. Catches shape changes; ignores lighting / color shifts. */
  edge: boolean;
}

export interface FeatureMatchOptions {
  templateName: string;
  embeddedPng?: string;
  minConfidence: number;
  scaleMin: number;
  scaleMax: number;
  scaleSteps: number;
  grayscale: boolean;
  edge: boolean;
}

export interface RegionOptions {
  note?: string;
}

export interface DetectionOverlay {
  enabled: boolean;
  color: string;
  label?: string;
}

/** Output value shape — drives `result.typedValue` so scripts read a consistent shape regardless of kind. */
export type DetectionOutputType = 'boolean' | 'number' | 'text' | 'bbox' | 'bboxes' | 'point';

export interface DetectionStability {
  /** Value must hold steady for this many ms before being surfaced. 0 = no debounce. */
  minDurationMs: number;
  /** Numeric jitter tolerance for "same value" comparison (0 = exact equality). */
  tolerance: number;
}

export interface DetectionOutput {
  ctxKey?: string;
  event?: string;
  eventOnChangeOnly: boolean;
  overlay?: DetectionOverlay;
  /** Primary value shape exposed via `r.typedValue` to scripts. */
  type?: DetectionOutputType;
  stability?: DetectionStability;
}

export interface DetectionDefinition {
  id: string;
  name: string;
  kind: DetectionKind;
  group?: string;
  enabled: boolean;
  roi?: DetectionRoi;
  template?: TemplateOptions;
  progressBar?: ProgressBarOptions;
  colorPresence?: ColorPresenceOptions;
  effect?: EffectOptions;
  featureMatch?: FeatureMatchOptions;
  region?: RegionOptions;
  output: DetectionOutput;
}

export interface ResultBox {
  x: number;
  y: number;
  w: number;
  h: number;
  cx: number;
  cy: number;
}

export interface DetectionResult {
  id: string;
  name: string;
  kind: DetectionKind;
  found: boolean;
  durationMs: number;
  value?: number;
  triggered?: boolean;
  match?: ResultBox;
  strip?: ResultBox;
  blobs?: ResultBox[];
  confidence?: number;
  /** Output-shape-aware value driven by `output.type`. Set by the JS detect.run wrapper. */
  typedValue?: unknown;
}

/** One labeled image for training. The label's interpretation depends on `kind`:
 *  ProgressBar → expected fill ratio (0..1). */
export interface TrainingSample {
  imageBase64: string;
  label: string;
  roi?: DetectionRoi;
  note?: string;
}

export interface TrainingDiagnostic {
  label: string;
  predicted: string;
  error: number;
}

export interface TrainingResult {
  suggested?: DetectionDefinition;
  diagnostics: TrainingDiagnostic[];
  summary: string;
}

/** Mirror of BrickBot.Modules.Detection.Models.TrainingSampleInfo. */
export interface TrainingSampleInfo {
  id: string;
  detectionId: string;
  label?: string;
  note?: string;
  width: number;
  height: number;
  capturedAt: string;
  /** Only populated when LIST_SAMPLES requests includeImages = true. */
  imageBase64?: string;
}

/** Wire-compatible with BrickBot.Modules.Detection.Services.NewTrainingSample. */
export interface NewTrainingSamplePayload {
  id?: string;
  imageBase64: string;
  label?: string;
  note?: string;
}

export const DETECTION_KIND_LABEL: Record<DetectionKind, string> = {
  template: 'Element',
  progressBar: 'Progress Bar',
  colorPresence: 'Color Presence',
  effect: 'Visual Effect',
  featureMatch: 'Sprite / Character',
  region: 'Region (anchor)',
};

/** Default factory — produces a fresh definition with sensible per-kind defaults. */
export function newDetection(kind: DetectionKind): DetectionDefinition {
  const base: DetectionDefinition = {
    id: '',
    name: '',
    kind,
    enabled: true,
    output: { eventOnChangeOnly: true },
  };
  switch (kind) {
    case 'template':
      base.template = { templateName: '', minConfidence: 0.85, scale: 1.0, grayscale: true, pyramid: false, edge: false };
      break;
    case 'progressBar':
      base.progressBar = {
        templateName: '', minConfidence: 0.80, templateEdge: true,
        scale: 1.0, grayscale: true, pyramid: false,
        fillColor: { r: 220, g: 30, b: 30 },
        tolerance: 60, colorSpace: 'rgb',
        direction: 'leftToRight', lineThreshold: 0.4,
        insetLeftPct: 0.30, insetRightPct: 0.18,
      };
      break;
    case 'colorPresence':
      base.colorPresence = {
        color: { r: 220, g: 30, b: 30 }, tolerance: 30, minArea: 100, maxResults: 8, colorSpace: 'rgb',
      };
      break;
    case 'effect':
      base.effect = { threshold: 0.15, autoBaseline: true, edge: false };
      break;
    case 'featureMatch':
      base.featureMatch = {
        templateName: '', minConfidence: 0.80,
        scaleMin: 0.9, scaleMax: 1.1, scaleSteps: 3,
        grayscale: true, edge: false,
      };
      break;
    case 'region':
      base.region = {};
      break;
  }
  return base;
}
