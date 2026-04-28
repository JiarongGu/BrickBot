import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Tooltip } from 'antd';
import { ClearOutlined, CopyOutlined, AimOutlined, ColumnHeightOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import { CompactButton, CompactSpace } from '@/shared/components/compact';
import type { DetectionKind, DetectionRoi } from '../../types';
import './AnnotationCanvas.css';

/**
 * Per-sample object-box annotation canvas. The user drags a rectangle on the displayed
 * sample to mark WHERE the object lives in THIS specific frame. Distinct from the
 * detection's runtime search ROI (different concern, different step in the wizard).
 *
 * Per-kind UX:
 *   • Tracker → "Use as init frame" toggle. Exactly one sample is the init frame.
 *   • Pattern → no extra toggles; positives need boxes, negatives don't.
 *   • Text    → first sample with a box defines OCR region; subsequent boxes ignored.
 *   • Bar     → every sample needs a box; "Copy from prev" speeds up sequential frames.
 *
 * Uses native canvas instead of SVG so it scales gracefully to 1080p+ frames; image-rendering:
 * pixelated keeps game UI crisp when the canvas is downscaled.
 */
export interface AnnotationCanvasProps {
  kind: DetectionKind;
  /** Image bytes (base64 PNG) and natural dimensions of the sample. */
  imageBase64: string;
  width: number;
  height: number;

  /** Current annotation for this sample. */
  box: DetectionRoi | undefined;
  /** Whether this sample is the tracker init frame (tracker kind only). */
  isInit: boolean;

  /** Box from the previous sample, drawn as a faint ghost so the user can align quickly. */
  previousBox?: DetectionRoi;

  /** Number of samples that have a box (for the header summary). */
  annotatedCount: number;
  totalCount: number;

  onChange: (box: DetectionRoi | undefined) => void;
  onSetIsInit: (init: boolean) => void;
  onCopyFromPrev?: () => void;
  onApplyToAll?: () => void;
  onClearAll?: () => void;
}

export const AnnotationCanvas: React.FC<AnnotationCanvasProps> = ({
  kind, imageBase64, width, height, box, isInit, previousBox,
  annotatedCount, totalCount,
  onChange, onSetIsInit, onCopyFromPrev, onApplyToAll, onClearAll,
}) => {
  const { t } = useTranslation();
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const wrapRef = useRef<HTMLDivElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);
  const [hover, setHover] = useState<{ x: number; y: number } | undefined>();

  // Render image + overlay whenever inputs change. Runs in a single effect so the
  // image and overlay never desync (e.g., overlay drawn before image loaded).
  useEffect(() => {
    const c = canvasRef.current;
    if (!c) return;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    c.width = width;
    c.height = height;

    const img = new Image();
    img.onload = () => {
      ctx.clearRect(0, 0, c.width, c.height);
      ctx.drawImage(img, 0, 0);

      // Ghost previous box (orange, dashed)
      if (previousBox && previousBox.w > 0 && previousBox.h > 0) {
        ctx.strokeStyle = 'rgba(250, 140, 22, 0.6)';
        ctx.lineWidth = 1.5;
        ctx.setLineDash([4, 4]);
        ctx.strokeRect(previousBox.x + 0.5, previousBox.y + 0.5, previousBox.w, previousBox.h);
      }

      // Current box (primary blue, solid + tint)
      if (box && box.w > 0 && box.h > 0) {
        ctx.strokeStyle = '#1890ff';
        ctx.lineWidth = 2;
        ctx.setLineDash([]);
        ctx.strokeRect(box.x + 0.5, box.y + 0.5, box.w, box.h);
        ctx.fillStyle = 'rgba(24, 144, 255, 0.15)';
        ctx.fillRect(box.x, box.y, box.w, box.h);

        // Label chip with dimensions in the top-left corner of the box
        const label = `${box.w}×${box.h}`;
        ctx.font = '12px source-code-pro, Menlo, monospace';
        const tw = ctx.measureText(label).width + 8;
        ctx.fillStyle = 'rgba(24, 144, 255, 0.9)';
        ctx.fillRect(box.x, Math.max(0, box.y - 16), tw, 16);
        ctx.fillStyle = '#fff';
        ctx.fillText(label, box.x + 4, Math.max(11, box.y - 4));
      }
    };
    img.src = `data:image/png;base64,${imageBase64}`;
  }, [imageBase64, width, height, box?.x, box?.y, box?.w, box?.h, previousBox?.x, previousBox?.y, previousBox?.w, previousBox?.h]);

  const canvasToPixel = useCallback((e: React.MouseEvent<HTMLCanvasElement>): { x: number; y: number } | null => {
    const c = canvasRef.current;
    if (!c) return null;
    const rect = c.getBoundingClientRect();
    const sx = width / rect.width;
    const sy = height / rect.height;
    return {
      x: Math.max(0, Math.min(width - 1, Math.round((e.clientX - rect.left) * sx))),
      y: Math.max(0, Math.min(height - 1, Math.round((e.clientY - rect.top) * sy))),
    };
  }, [width, height]);

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    dragStartRef.current = p;
    onChange({ x: p.x, y: p.y, w: 0, h: 0 });
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    setHover(p);
    if (!dragStartRef.current) return;
    const start = dragStartRef.current;
    onChange({
      x: Math.min(start.x, p.x),
      y: Math.min(start.y, p.y),
      w: Math.abs(p.x - start.x),
      h: Math.abs(p.y - start.y),
    });
  };

  const onMouseUp = () => {
    if (dragStartRef.current && box && (box.w < 4 || box.h < 4)) {
      // Treat tiny drags (< 4×4 px) as accidental clicks — clear the box rather than save.
      onChange(undefined);
    }
    dragStartRef.current = null;
  };

  const onMouseLeave = () => {
    dragStartRef.current = null;
    setHover(undefined);
  };

  return (
    <div className="annotation-canvas" ref={wrapRef}>
      <div className="annotation-canvas__toolbar">
        <span className="annotation-canvas__count">
          {t('detection.train.annotate.boxedCount', '{{n}} of {{total}} samples annotated', { n: annotatedCount, total: totalCount })}
        </span>
        <CompactSpace size={4}>
          {kind === 'tracker' && (
            <Tooltip title={t('detection.train.annotate.markInit', 'Use as tracker init frame') as string}>
              <CompactButton
                size="small"
                type={isInit ? 'primary' : 'text'}
                icon={<AimOutlined />}
                onClick={() => onSetIsInit(!isInit)}
              >
                {t('detection.train.annotate.isInit', 'Init frame')}
              </CompactButton>
            </Tooltip>
          )}
          {onCopyFromPrev && (
            <Tooltip title={t('detection.train.annotate.copyPrev', 'Copy box from previous') as string}>
              <CompactButton
                size="small"
                type="text"
                icon={<CopyOutlined />}
                onClick={onCopyFromPrev}
                disabled={!previousBox}
              />
            </Tooltip>
          )}
          {onApplyToAll && (
            <Tooltip title={t('detection.train.annotate.applyAll', 'Apply box to all samples') as string}>
              <CompactButton
                size="small"
                type="text"
                icon={<ColumnHeightOutlined />}
                onClick={onApplyToAll}
                disabled={!box || box.w <= 0 || box.h <= 0}
              />
            </Tooltip>
          )}
          {box && (
            <Tooltip title={t('detection.train.annotate.clearOne', 'Clear box') as string}>
              <CompactButton
                size="small"
                type="text"
                icon={<ClearOutlined />}
                onClick={() => onChange(undefined)}
              />
            </Tooltip>
          )}
          {onClearAll && annotatedCount > 0 && (
            <Tooltip title={t('detection.train.annotate.clearAll', 'Clear all boxes') as string}>
              <CompactButton size="small" type="text" onClick={onClearAll}>
                {t('detection.train.annotate.clearAll', 'Clear all boxes')}
              </CompactButton>
            </Tooltip>
          )}
        </CompactSpace>
      </div>

      <div className="annotation-canvas__wrap">
        <canvas
          ref={canvasRef}
          className={classNames('annotation-canvas__canvas')}
          onMouseDown={onMouseDown}
          onMouseMove={onMouseMove}
          onMouseUp={onMouseUp}
          onMouseLeave={onMouseLeave}
        />
      </div>

      <div className="annotation-canvas__hud">
        {hover && <span>({hover.x}, {hover.y})</span>}
        {box && box.w > 0 && box.h > 0 && (
          <span>box: ({box.x}, {box.y}) {box.w}×{box.h}</span>
        )}
      </div>
    </div>
  );
};
