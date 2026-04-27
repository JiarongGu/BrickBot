import React, { useEffect, useRef } from 'react';
import classNames from 'classnames';
import type { DetectionResult } from '../../types';

/**
 * Per-sample diagnostic card: thumbnail + label/prediction text + ROI/match overlay.
 * Renders the sample image into a small canvas (capped at 280×160 logical px), then
 * draws the predicted bbox / fill strip / blob list on top so the user can see WHY
 * the trainer scored each sample the way it did.
 */
export const DiagnosticThumb: React.FC<{
  sample: { id: string; imageBase64: string; width: number; height: number };
  index: number;
  diagnostic: { label: string; predicted: string; error: number };
  prediction: DetectionResult | undefined;
}> = ({ sample, index, diagnostic, prediction }) => {
  const ref = useRef<HTMLCanvasElement | null>(null);
  useEffect(() => {
    const c = ref.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    // Cap thumbnail at 280×160; preserve aspect ratio.
    const maxW = 280, maxH = 160;
    const scale = Math.min(maxW / sample.width, maxH / sample.height, 1);
    c.width = Math.round(sample.width * scale);
    c.height = Math.round(sample.height * scale);
    const img = new Image();
    img.onload = () => {
      ctx.drawImage(img, 0, 0, c.width, c.height);
      if (prediction) drawPredictionOverlay(ctx, prediction, scale, diagnostic.error);
    };
    img.src = `data:image/png;base64,${sample.imageBase64}`;
  }, [sample.id, sample.imageBase64, sample.width, sample.height, prediction, diagnostic.error]);

  const errClass = diagnostic.error <= 0.05 ? 'good' : diagnostic.error <= 0.2 ? 'warn' : 'err';
  return (
    <div className={classNames('diagnostic-thumb', `diagnostic-thumb--${errClass}`)}>
      <canvas ref={ref} className="diagnostic-thumb__canvas" />
      <div className="diagnostic-thumb__label">
        <span className="diagnostic-thumb__index">#{index + 1}</span>
        <span>label <b>{diagnostic.label}</b></span>
        <span>→ predicted <b>{diagnostic.predicted}</b></span>
        <span className="diagnostic-thumb__err">err {diagnostic.error.toFixed(3)}</span>
      </div>
    </div>
  );
};

function drawPredictionOverlay(
  ctx: CanvasRenderingContext2D,
  r: DetectionResult,
  scale: number,
  err: number,
) {
  const ok = err <= 0.1;
  const color = ok ? '#52c41a' : err <= 0.3 ? '#fa8c16' : '#ff4d4f';
  const line = (x: number, y: number, w: number, h: number, dashed = false) => {
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.setLineDash(dashed ? [4, 3] : []);
    ctx.strokeRect(x * scale + 0.5, y * scale + 0.5, w * scale, h * scale);
  };
  if (r.kind === 'bar') {
    if (r.match) line(r.match.x, r.match.y, r.match.w, r.match.h, true);
    if (r.strip) {
      ctx.fillStyle = color + '33';
      ctx.fillRect(r.strip.x * scale, r.strip.y * scale, r.strip.w * scale, r.strip.h * scale);
      line(r.strip.x, r.strip.y, r.strip.w, r.strip.h);
    }
  } else if (r.match) {
    line(r.match.x, r.match.y, r.match.w, r.match.h);
  }
}
