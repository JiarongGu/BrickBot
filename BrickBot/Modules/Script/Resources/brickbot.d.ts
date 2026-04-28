// BrickBot host API typings — loaded into Monaco for autocomplete + type checking.
// Do NOT ship this file in the published exe; it's served via SCRIPT.GET_TYPES IPC.
// When editing this file, also update HostApi.cs / StdLib.cs to match.

declare interface BbRect { x: number; y: number; w: number; h: number; }
declare interface BbMatch extends BbRect { cx: number; cy: number; confidence: number; }
declare interface BbColor { r: number; g: number; b: number; }

declare type BbMouseButton = 'left' | 'right' | 'middle';

declare interface BbFindOptions {
  /** CCoeffNormed threshold in [0..1]; default 0.85. */
  minConfidence?: number;
  /** Restrict the search to this client-relative rectangle. Always combine with scale + grayscale for speed. */
  roi?: BbRect;
  /**
   * Downsample frame + template before matching. 1.0 = full res, 0.5 ≈ 4× faster, 0.25 ≈ 16×.
   * Match coords still come back at full resolution.
   */
  scale?: number;
  /**
   * Convert frame + template to single-channel before matching. ~3× faster, slight accuracy
   * loss on color-discriminated templates. Combine with scale + roi for action-game speeds.
   */
  grayscale?: boolean;
  /**
   * Coarse-to-fine pyramid search — match at quarter res, then refine at full res in a tight
   * ROI around the candidate. ~5× faster than flat full-res with same accuracy. Ignored when
   * roi or scale are explicitly set.
   */
  pyramid?: boolean;
}

declare interface BbFeatureMatchOptions {
  /** CCoeffNormed threshold across scales. Default 0.80 (slightly looser than vision.find
   *  since the per-scale match accepts any single hit ≥ this confidence). */
  minConfidence?: number;
  /** Smallest scale factor to try. Default 0.8. */
  scaleMin?: number;
  /** Largest scale factor to try. Default 1.2. */
  scaleMax?: number;
  /** Number of uniformly-spaced scales between min and max. Default 5. Cost scales linearly. */
  scaleSteps?: number;
  /** Optional ROI to restrict the search to. Strongly recommended for action-game speeds. */
  roi?: BbRect;
}

declare interface BbColorRange {
  rMin: number; rMax: number;
  gMin: number; gMax: number;
  bMin: number; bMax: number;
}

declare interface BbColorBlob {
  x: number; y: number; w: number; h: number;
  area: number; cx: number; cy: number;
}

declare interface BbFindColorsOptions {
  roi?: BbRect;
  /** Reject blobs smaller than this many pixels (bbox area). Default 25. */
  minArea?: number;
  /** Cap on returned blobs after sort-by-area-desc. Default 32. */
  maxResults?: number;
}

declare interface BbPercentBarOptions {
  /** Per-channel ± tolerance for color match. Default 25. */
  tolerance?: number;
}

declare interface BbVisionApi {
  /** Find a template inside the current frame. Returns null on no match. */
  find(templatePath: string, opts?: BbFindOptions): BbMatch | null;
  /** Poll for a template until found or timeout (ms). */
  waitFor(templatePath: string, timeoutMs: number, opts?: BbFindOptions): BbMatch | null;
  /** Sample BGR color at window-relative (x, y). */
  colorAt(x: number, y: number): BbColor;

  /**
   * Scale-invariant template matching — tries multiple scales, returns the best match.
   * Robust against small UI scaling differences (e.g. icon sized differently on 1440p
   * vs 1080p). Cost is roughly scaleSteps × cost-of-vision.find; pair with `roi` for speed.
   */
  findFeatures(templatePath: string, opts?: BbFeatureMatchOptions): BbMatch | null;

  /**
   * Find blobs of a color range. Returns up to opts.maxResults bounding boxes sorted by
   * area (largest first). Order of magnitude faster than template matching for distinctly-
   * colored elements (HP/MP fills, skill cooldown overlays, debuff icons).
   *
   * @example
   * const reds = vision.findColors({ rMin: 180, rMax: 255, gMin: 0, gMax: 60, bMin: 0, bMax: 60 });
   * if (reds.length > 0) input.click(reds[0].cx, reds[0].cy);
   */
  findColors(range: BbColorRange, opts?: BbFindColorsOptions): BbColorBlob[];

  /**
   * HP / MP / cooldown bar fill ratio (0..1). Counts pixels within tolerance of `color`
   * inside the ROI; great for any solid-colored progress indicator. Use a thin horizontal
   * (or vertical) strip ROI for speed and accuracy.
   *
   * @example
   * const hp = vision.percentBar({ x: 100, y: 50, w: 200, h: 4 }, { r: 200, g: 30, b: 30 });
   * if (hp < 0.3) brickbot.emit('low-hp', { hp });
   */
  percentBar(roi: BbRect, color: BbColor, opts?: BbPercentBarOptions): number;

  /**
   * Snapshot a ROI into a named baseline. Subsequent calls to vision.diff(name, roi)
   * compare the current frame's ROI against this snapshot. Use to detect visual effects:
   * skill activations, buff icons appearing, status overlays.
   *
   * @example
   * brickbot.on('start', () => vision.captureBaseline('fireball', skillIconRoi));
   * brickbot.when(() => vision.diff('fireball', skillIconRoi) > 0.15, () => log('cast!'));
   */
  captureBaseline(name: string, roi: BbRect): void;

  /** Mean absolute pixel diff (0..1) vs the named baseline. 1.0 if no baseline registered. */
  diff(name: string, roi: BbRect): number;

  /** Drop all stored baselines. */
  clearBaselines(): void;

  /**
   * Block until an ROI's contents stop changing — wait out menu animations / fade-ins /
   * transitions before sampling a fragile detection. Returns `true` when the ROI settles
   * within `timeoutMs`, `false` on timeout.
   *
   * @example
   * if (vision.waitStable({ x: 100, y: 50, w: 400, h: 200 }, { stableMs: 300, timeoutMs: 3000 })) {
   *   const r = detect.run('confirm-button');
   *   if (r.found) input.click(r.match.cx, r.match.cy);
   * }
   */
  waitStable(roi: BbRect, opts?: BbWaitStableOptions): boolean;
}

declare interface BbWaitStableOptions {
  /** ROI must hold steady for this many ms in a row to count as stable. Default 250. */
  stableMs?: number;
  /** Per-channel mean abs diff threshold (0..1). Higher = looser; tolerates more noise. Default 0.02. */
  maxDiff?: number;
  /** Sampling cadence in ms. Default 50. */
  intervalMs?: number;
  /** Give up after this many ms — returns false. Default 5000. */
  timeoutMs?: number;
}

declare interface BbInputApi {
  click(x: number, y: number, button?: BbMouseButton): void;
  moveTo(x: number, y: number): void;
  drag(x1: number, y1: number, x2: number, y2: number, button?: BbMouseButton): void;
  /** Press + release a Win32 virtual-key code. */
  key(vk: number): void;
  keyDown(vk: number): void;
  keyUp(vk: number): void;
  type(text: string): void;
}

declare type BtStatus = 'success' | 'failure' | 'running';
declare type BtNode = (ctx: unknown) => BtStatus;

declare interface BbCombatApi {
  readonly SUCCESS: 'success';
  readonly FAILURE: 'failure';
  readonly RUNNING: 'running';
  Sequence(...children: BtNode[]): BtNode;
  Selector(...children: BtNode[]): BtNode;
  Inverter(child: BtNode): BtNode;
  /** Gate a child by a per-instance cooldown. Cooldown only resets on success. */
  Cooldown(ms: number, child: BtNode): BtNode;
  /** Side-effect leaf; always succeeds. */
  Action(fn: (ctx: unknown) => void): BtNode;
  /** Predicate leaf; succeeds when fn(ctx) is truthy. */
  Condition(predicate: (ctx: unknown) => boolean): BtNode;
  SkillRotation(skills: Array<{
    name?: string;
    cooldown: number;
    cast: () => void;
    ready?: () => boolean;
  }>): BtNode;
  /** Tick a tree at a fixed interval until cancelled or limitMs elapsed. */
  runTree(tree: BtNode, opts?: { intervalMs?: number; limitMs?: number }): void;
}

declare interface BbCtxApi {
  /** JSON-serializable values only. */
  set(key: string, value: unknown): void;
  get<T = unknown>(key: string, fallback?: T): T;
  has(key: string): boolean;
  delete(key: string): boolean;
  keys(): string[];
  snapshot(): Record<string, unknown>;
  inc(key: string, by?: number): number;
}

// ----- Event bus + actions + triggers -----
// Built-in events emitted by `brickbot.runForever`:
//   'start'  — once, before the first tick
//   'frame'  — once per pumped frame, payload = { width, height, frameNumber }
//   'tick'   — once per tick (after frame + triggers)
//   'stop'   — once, after the loop exits (cancelled or main returned)
//   'error'  — when a handler / trigger / pump throws, payload = { phase, message }
// User-defined event names work too — call brickbot.emit(name, payload) anywhere.

declare type BbBuiltInEvent = 'start' | 'frame' | 'tick' | 'stop' | 'error';

declare interface BbFramePayload {
  width: number;
  height: number;
  frameNumber: number;
}

declare interface BbErrorPayload {
  phase: string;
  message: string;
}

declare interface BbTriggerOptions {
  /** Minimum ms between successive firings of this trigger. Defaults to 0 (no throttling). */
  cooldownMs?: number;
}

declare interface BbRunForeverOptions {
  /** Tick interval. Defaults to 16 ms (~60 Hz). Lower bound is the cost of pump+vision work. */
  tickMs?: number;
}

declare interface BbRunForeverOptionsExt extends BbRunForeverOptions {
  /** Run every enabled detection at the start of each tick before triggers evaluate.
   *  Detections write to ctx and emit brickbot events per their `output` bindings — so
   *  triggers downstream see fresh perception state. Default false. */
  autoDetect?: boolean;
}

declare interface BbBrickbotApi {
  on(event: 'start' | 'tick' | 'stop', handler: () => void): () => void;
  on(event: 'frame', handler: (payload: BbFramePayload) => void): () => void;
  on(event: 'error', handler: (payload: BbErrorPayload) => void): () => void;
  on(event: string, handler: (payload?: unknown) => void): () => void;
  off(event: string, handler: (...args: unknown[]) => void): void;
  emit(event: string, payload?: unknown): void;

  /** Register a named action; surfaces in the Tools tab "Actions" panel. */
  action(name: string, fn: () => void): void;
  /** Run a registered action by name. */
  invoke(name: string): void;
  listActions(): string[];

  /** Declarative trigger: predicate runs each tick, action fires when truthy. */
  when(predicate: () => boolean, action: () => void, opts?: BbTriggerOptions): void;

  /** Request graceful shutdown of the run. Reason surfaces in the runner's stoppedReason
   *  state (e.g. for the UI: "stopped: goalReached"). First call wins.
   *  @param reason free-form identifier; defaults to 'script'. */
  stop(reason?: string): void;

  /** Main loop — tick until cancelled. Pumps a frame, drains UI invocations,
   *  optionally runs detections, evaluates triggers, fires 'tick'. Call from main();
   *  blocks until Stop. */
  runForever(opts?: BbRunForeverOptionsExt): void;
}

// ----- detect — typed, persisted vision rules -----

declare type BbDetectionKind =
  | 'template'
  | 'progressBar'
  | 'colorPresence'
  | 'effect'
  | 'featureMatch';

/**
 * Color match mode. <c>'rgb'</c> = literal per-channel match (sensitive to lighting).
 * <c>'hsv'</c> = hue-window match — robust against brightness / color-grading drift.
 */
declare type BbColorSpace = 'rgb' | 'hsv';

declare interface BbDetectionDefinition {
  id: string;
  name: string;
  kind: BbDetectionKind;
  group?: string;
  enabled: boolean;
  roi?: BbRect;
  template?: {
    templateName: string;
    minConfidence?: number;
    scale?: number;
    grayscale?: boolean;
    pyramid?: boolean;
    /** Match in Canny edge space — robust to color drift / lighting / variable fill. */
    edge?: boolean;
  };
  progressBar?: {
    templateName: string;
    minConfidence?: number;
    /** Match the bar bbox by edges instead of pixels — survives saved-template-fill-mismatch. */
    templateEdge?: boolean;
    fillColor: BbColor;
    tolerance?: number;
    /** Color space for the fill match. HSV preferred when game has bloom / color filters. */
    colorSpace?: BbColorSpace;
    insetLeftPct?: number;
    insetRightPct?: number;
  };
  colorPresence?: {
    color: BbColor;
    tolerance?: number;
    minArea?: number;
    maxResults?: number;
    colorSpace?: BbColorSpace;
  };
  effect?: {
    threshold?: number;
    autoBaseline?: boolean;
    /** Edge-diff vs baseline — flags shape changes, ignores color/lighting drift. */
    edge?: boolean;
  };
  featureMatch?: {
    templateName: string;
    minConfidence?: number;
    scaleMin?: number;
    scaleMax?: number;
    scaleSteps?: number;
    edge?: boolean;
  };
  output?: {
    ctxKey?: string;
    event?: string;
    eventOnChangeOnly?: boolean;
    overlay?: { enabled: boolean; color: string; label?: string };
  };
}

declare interface BbDetectionResult {
  id: string;
  name: string;
  kind: BbDetectionKind;
  found: boolean;
  durationMs: number;
  /** Numeric value: 0..1 fill ratio for progressBar / 0..1 diff for effect / blob count for colorPresence. */
  value?: number;
  /** True when an effect's diff exceeded its threshold. */
  triggered?: boolean;
  match?: BbMatch;
  /** Discovered fill strip rectangle for progressBar — useful for overlay rendering. */
  strip?: BbRect;
  /** Bboxes of color blobs (colorPresence). */
  blobs?: Array<BbRect & { cx: number; cy: number }>;
  confidence?: number;
}

declare interface BbDetectApi {
  /** Force re-read of definitions from disk + clear effect baselines. */
  reload(): void;
  /** All detection definitions for the active profile (cached). */
  list(): BbDetectionDefinition[];
  /** Run one detection by id (or name). Applies output bindings (ctx + event). */
  run(idOrName: string): BbDetectionResult;
  /** Run every enabled detection. Returns the array of results. */
  runAll(): BbDetectionResult[];
  /** Run an in-memory definition without persisting — used by editors for live preview. */
  test(definition: BbDetectionDefinition): BbDetectionResult;
}

// ----- Globals (back-compat with the original JS surface) -----
declare const vision: BbVisionApi;
declare const input: BbInputApi;
declare const combat: BbCombatApi;
declare const ctx: BbCtxApi;
declare const brickbot: BbBrickbotApi;
declare const detect: BbDetectApi;
declare function log(message: unknown): void;
declare function wait(ms: number): void;
declare function isCancelled(): boolean;
declare function now(): number;

// ----- Module form: import { vision } from 'brickbot' -----
declare module 'brickbot' {
  export const vision: BbVisionApi;
  export const input: BbInputApi;
  export const combat: BbCombatApi;
  export const ctx: BbCtxApi;
  export const brickbot: BbBrickbotApi;
  export const detect: BbDetectApi;
  export function log(message: unknown): void;
  export function wait(ms: number): void;
  export function isCancelled(): boolean;
  export function now(): number;
  export type Rect = BbRect;
  export type Match = BbMatch;
  export type Color = BbColor;
  export type MouseButton = BbMouseButton;
  export type FramePayload = BbFramePayload;
  export type ErrorPayload = BbErrorPayload;
  export type TriggerOptions = BbTriggerOptions;
  export type RunForeverOptions = BbRunForeverOptions;
  export type DetectionKind = BbDetectionKind;
  export type DetectionDefinition = BbDetectionDefinition;
  export type DetectionResult = BbDetectionResult;
}

// ----- CommonJS shims so transpiled libraries type-check -----
declare const module: { exports: unknown };
declare const exports: Record<string, unknown>;
declare function require<T = unknown>(modulePath: string): T;
