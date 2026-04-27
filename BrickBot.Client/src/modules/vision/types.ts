export interface VisionMatch {
  x: number;
  y: number;
  w: number;
  h: number;
  cx: number;
  cy: number;
  confidence: number;
}

export interface VisionTestResult {
  match: VisionMatch | null;
  durationMs: number;
}

export interface ColorRange {
  rMin: number; rMax: number;
  gMin: number; gMax: number;
  bMin: number; bMax: number;
}

export interface ColorBlob {
  x: number; y: number; w: number; h: number;
  area: number; cx: number; cy: number;
}

export interface FindColorsResult {
  blobs: ColorBlob[];
  durationMs: number;
}

export interface PercentBarResult {
  fill: number;
  durationMs: number;
}

export interface BarRect { x: number; y: number; w: number; h: number; }
export interface BarFromTemplateMatch extends BarRect { cx: number; cy: number; confidence: number; }
export interface BarFromTemplateResult {
  /** Bbox of the template match — null when the template wasn't found. */
  bar: BarFromTemplateMatch | null;
  /** The horizontal strip auto-selected as the brightest fill row, ±2 px tall. */
  strip: BarRect | null;
  /** 0..1 fraction of strip pixels matching the fill color. */
  fill: number;
  durationMs: number;
}

export type DetectionMethod = 'template' | 'colors' | 'percentBar';
