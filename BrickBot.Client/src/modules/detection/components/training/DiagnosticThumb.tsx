import React, { useEffect, useRef } from 'react';
import classNames from 'classnames';
import type { DetectionResult, DetectionRoi, PredictedBox, TrainingDiagnostic } from '../../types';

/**
 * Per-sample diagnostic card. Renders:
 *   • The sample image, scaled to fit a max-width thumbnail.
 *   • The labeled object box (orange dashed) — "ground truth".
 *   • The predicted box (green / yellow / red by IoU) — what the trained model would output.
 *   • A meta row showing label / predicted / error / IoU.
 *
 * The two overlays let the user see at a glance WHICH samples the trained model nails and
 * which it misses, so they know where to refine.
 */
export const DiagnosticThumb: React.FC<{
  sample: { id: string; imageBase64: string; width: number; height: number; objectBox?: DetectionRoi };
  index: number;
  diagnostic: TrainingDiagnostic;
  /** Optional live test result for additional overlays (bar fill strip, etc). */
  prediction?: DetectionResult;
}> = ({ sample, index, diagnostic, prediction }) => {
  const ref = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const c = ref.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    const maxW = 400, maxH = 240;
    const scale = Math.min(maxW / sample.width, maxH / sample.height, 1);
    c.width = Math.round(sample.width * scale);
    c.height = Math.round(sample.height * scale);
    const img = new Image();
    img.onload = () => {
      ctx.drawImage(img, 0, 0, c.width, c.height);
      // Labeled box (orange dashed) — "what the user said"
      if (sample.objectBox && sample.objectBox.w > 0) {
        drawBox(ctx, sample.objectBox, scale, 'rgba(250, 140, 22, 0.9)', /*dashed*/ true, 'label');
      }
      // Predicted box (color by IoU / error) — "what the model says"
      const predColor = colorForIoU(diagnostic.iou, diagnostic.error);
      if (diagnostic.predictedBox) {
        drawBox(ctx, diagnostic.predictedBox, scale, predColor, false, 'pred');
      }
      // Bar-kind extras (fill strip)
      if (prediction?.kind === 'bar' && prediction.strip) {
        ctx.fillStyle = predColor + '33';
        ctx.fillRect(prediction.strip.x * scale, prediction.strip.y * scale, prediction.strip.w * scale, prediction.strip.h * scale);
      }
    };
    img.src = `data:image/png;base64,${sample.imageBase64}`;
  }, [sample.id, sample.imageBase64, sample.width, sample.height, sample.objectBox?.x, sample.objectBox?.y, sample.objectBox?.w, sample.objectBox?.h, diagnostic.predictedBox?.x, diagnostic.predictedBox?.y, diagnostic.predictedBox?.w, diagnostic.predictedBox?.h, diagnostic.iou, diagnostic.error, prediction]);

  const errClass = diagnostic.error <= 0.05 ? 'good' : diagnostic.error <= 0.2 ? 'warn' : 'err';
  return (
    <div className={classNames('diagnostic-thumb', `diagnostic-thumb--${errClass}`)}>
      <canvas ref={ref} className="diagnostic-thumb__canvas" />
      <div className="diagnostic-thumb__label">
        <span className="diagnostic-thumb__index">#{index + 1}</span>
        <span>label <b>{diagnostic.label}</b></span>
        <span>→ predicted <b>{diagnostic.predicted}</b></span>
        {diagnostic.iou > 0 && (
          <span className="diagnostic-thumb__iou">IoU <b>{diagnostic.iou.toFixed(2)}</b></span>
        )}
        <span className="diagnostic-thumb__err">err {diagnostic.error.toFixed(3)}</span>
      </div>
    </div>
  );
};

function colorForIoU(iou: number, err: number): string {
  // Prefer IoU when present (pattern positives); fall back to error otherwise.
  if (iou > 0) {
    if (iou >= 0.7) return '#52c41a';
    if (iou >= 0.4) return '#fa8c16';
    return '#ff4d4f';
  }
  if (err <= 0.05) return '#52c41a';
  if (err <= 0.2) return '#fa8c16';
  return '#ff4d4f';
}

function drawBox(
  ctx: CanvasRenderingContext2D,
  box: DetectionRoi | PredictedBox,
  scale: number,
  color: string,
  dashed: boolean,
  label: 'label' | 'pred',
) {
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.setLineDash(dashed ? [4, 3] : []);
  const x = box.x * scale, y = box.y * scale, w = box.w * scale, h = box.h * scale;
  ctx.strokeRect(x + 0.5, y + 0.5, w, h);
  // Label chip — top-left for "label" (above), top-right for "pred" (below) so they don't overlap
  ctx.font = '11px source-code-pro, Menlo, monospace';
  const tw = ctx.measureText(label).width + 6;
  ctx.fillStyle = color;
  if (label === 'label') {
    ctx.fillRect(x, Math.max(0, y - 14), tw, 14);
    ctx.fillStyle = '#fff';
    ctx.fillText(label, x + 3, Math.max(10, y - 3));
  } else {
    ctx.fillRect(x + w - tw, y, tw, 14);
    ctx.fillStyle = '#fff';
    ctx.fillText(label, x + w - tw + 3, y + 11);
  }
}
