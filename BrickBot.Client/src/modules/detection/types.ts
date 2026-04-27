// Frontend mirror of BrickBot/Modules/Detection/Models/DetectionDefinition.cs.
// NOTE: Enums are camelCase because IpcHandler serializes with JsonStringEnumConverter(CamelCase).

/**
 * Locked-in detection kinds (the legacy template/colorPresence/effect/featureMatch/region
 * kinds were deleted in the v2 rewrite). Pick the kind that matches the visual you want
 * to detect:
 *   • `tracker` — moving sprite / character location (OpenCV KCF / CSRT / MIL).
 *   • `pattern` — static element appearance via ORB descriptors. Background-invariant.
 *   • `text`    — OCR text (Tesseract). Buff names, status banners, quest text.
 *   • `bar`     — HP / MP / cooldown meter; reads fill ratio.
 */
export type DetectionKind =
  | 'tracker'
  | 'pattern'
  | 'text'
  | 'bar';

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

// ============================================================================
//  Per-kind options
// ============================================================================

/** OpenCV visual tracker algorithm. KCF balanced (default), CSRT most accurate (slow),
 *  MIL robust to short occlusions (moderate). */
export type TrackerAlgorithm = 'kcf' | 'csrt' | 'mil';

export interface TrackerOptions {
  /** Base64 PNG of the frame the tracker was initialized on. */
  initFramePng?: string;
  /** Initial bbox in window-relative pixels — the user's drag-rectangle at training time. */
  initX: number;
  initY: number;
  initW: number;
  initH: number;
  algorithm: TrackerAlgorithm;
  /** When the tracker reports lost, automatically re-init from the saved frame on next call. */
  reacquireOnLost: boolean;
}

export interface PatternOptions {
  /** Reference patch (cropped to trained element). Used for re-training + overlay viz. */
  embeddedPng?: string;
  /** Base64 ORB descriptor blob (rows × 32 bytes, row-major). */
  descriptors?: string;
  /** Number of trained keypoints (descriptor blob row count). */
  keypointCount: number;
  /** Reference patch dimensions — used to project matched keypoints into a bbox. */
  templateWidth: number;
  templateHeight: number;
  /** Lowe ratio test threshold (0..1, lower = stricter). */
  loweRatio: number;
  /** Minimum match-ratio (0..1) to count as found. */
  minConfidence: number;
  /** Cap on ORB keypoints extracted per frame at runtime. */
  maxRuntimeKeypoints: number;
}

export interface TextOptions {
  /** Tesseract language tag (e.g. `eng`, `chi_sim`, `jpn`). */
  language: string;
  /** Page-segmentation mode: 7 = single line (default for game UI), 8 = single word, 6 = block. */
  pageSegMode: number;
  /** Optional regex — only count as found when the OCR result matches. */
  matchRegex?: string;
  /** Minimum Tesseract confidence (0..100). */
  minConfidence: number;
  /** Pre-binarize the ROI before OCR. */
  binarize: boolean;
  /** Upscale factor before OCR (Tesseract prefers larger glyphs). */
  upscaleFactor: number;
}

export interface BarOptions {
  /** Optional Pattern detection id whose match bbox locates the bar; empty = use ROI. */
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

/** Output value shape — drives `result.typedValue`. */
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
  tracker?: TrackerOptions;
  pattern?: PatternOptions;
  text?: TextOptions;
  bar?: BarOptions;
  output: DetectionOutput;
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
  /** Bar fill ratio (0..1). */
  value?: number;
  /** Match bbox (tracker / pattern / bar). */
  match?: ResultBox;
  /** Pattern keypoint-match ratio; tracker reports null. */
  confidence?: number;
  /** Bar fill-strip used for the per-line scan. */
  strip?: ResultBox;
  /** OCR result text (Text kind only). */
  text?: string;
}

export interface TrainingSample {
  imageBase64: string;
  label: string;
  roi?: DetectionRoi;
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

export interface TrainingSampleInfo {
  id: string;
  label: string;
  capturedAt: string;
  width: number;
  height: number;
  imageBase64?: string;
}

/** Wire-compatible with BrickBot.Modules.Detection.Services.NewTrainingSample. */
export interface NewTrainingSamplePayload {
  id?: string;
  imageBase64: string;
  label?: string;
  note?: string;
}

// ============================================================================
//  Defaults + UI labels
// ============================================================================

export const DETECTION_KIND_LABEL: Record<DetectionKind, string> = {
  tracker: 'Tracker (moving element)',
  pattern: 'Pattern (visual feature)',
  text: 'Text (OCR)',
  bar: 'Bar (HP / MP / cooldown)',
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
    case 'tracker':
      base.tracker = {
        initX: 0, initY: 0, initW: 0, initH: 0,
        algorithm: 'kcf',
        reacquireOnLost: true,
      };
      break;
    case 'pattern':
      base.pattern = {
        keypointCount: 0,
        templateWidth: 0, templateHeight: 0,
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
  }
  return base;
}
