import { BaseModuleService } from '@/shared/services/baseModuleService';
import type {
  BarFromTemplateResult,
  ColorRange,
  FindColorsResult,
  PercentBarResult,
  VisionTestResult,
} from '../types';

class VisionService extends BaseModuleService {
  constructor() {
    super('VISION');
  }

  /**
   * Run a saved template against a captured frame and return the best match (or null).
   * Backend defaults: grayscale on, scale 1.0; pass overrides for the Capture panel's
   * Detect controls.
   */
  testTemplate(
    profileId: string,
    templateName: string,
    frameBase64: string,
    opts?: { minConfidence?: number; grayscale?: boolean; scale?: number },
  ): Promise<VisionTestResult> {
    return this.send('TEST_TEMPLATE', {
      profileId,
      templateName,
      frameBase64,
      minConfidence: opts?.minConfidence,
      grayscale: opts?.grayscale,
      scale: opts?.scale,
    });
  }

  /** Find blobs of pixels within the given RGB range. Returns sorted-by-area-desc. */
  testFindColors(
    frameBase64: string,
    range: ColorRange,
    opts?: { minArea?: number; maxResults?: number },
  ): Promise<FindColorsResult> {
    return this.send('TEST_FIND_COLORS', {
      frameBase64,
      rMin: range.rMin, rMax: range.rMax,
      gMin: range.gMin, gMax: range.gMax,
      bMin: range.bMin, bMax: range.bMax,
      minArea: opts?.minArea,
      maxResults: opts?.maxResults,
    });
  }

  /**
   * Measure fill percentage (0..1) of a colored bar inside the given ROI. Counts pixels
   * within `tolerance` of the target color.
   */
  testPercentBar(
    frameBase64: string,
    roi: { x: number; y: number; w: number; h: number },
    color: { r: number; g: number; b: number },
    opts?: { tolerance?: number },
  ): Promise<PercentBarResult> {
    return this.send('TEST_PERCENT_BAR', {
      frameBase64,
      x: roi.x, y: roi.y, w: roi.w, h: roi.h,
      r: color.r, g: color.g, b: color.b,
      tolerance: opts?.tolerance,
    });
  }

  /**
   * Two-stage HP/MP/cooldown bar detection: template locates the bar's area, then
   * the brightest row inside is auto-selected and the fill percentage of pixels
   * matching `fillColor` in a ±2 strip is reported. Returns null bar/strip when the
   * template doesn't match.
   */
  testBarFromTemplate(
    profileId: string,
    templateName: string,
    frameBase64: string,
    fillColor: { r: number; g: number; b: number },
    opts?: { minConfidence?: number; tolerance?: number; insetLeftPct?: number; insetRightPct?: number },
  ): Promise<BarFromTemplateResult> {
    return this.send('TEST_BAR_FROM_TEMPLATE', {
      profileId,
      templateName,
      frameBase64,
      r: fillColor.r, g: fillColor.g, b: fillColor.b,
      minConfidence: opts?.minConfidence,
      tolerance: opts?.tolerance,
      insetLeftPct: opts?.insetLeftPct,
      insetRightPct: opts?.insetRightPct,
    });
  }
}

export const visionService = new VisionService();
