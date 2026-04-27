import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Popconfirm, Tag, Tooltip, message } from 'antd';
import {
  AimOutlined,
  CameraOutlined,
  DeleteOutlined,
  ExperimentOutlined,
  PlayCircleOutlined,
  PlusOutlined,
  ReloadOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactAlert,
  CompactButton,
  CompactDangerButton,
  CompactPrimaryButton,
  CompactSelect,
  CompactSpace,
} from '@/shared/components/compact';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import { templateService } from '@/modules/template';
import type { TemplateInfo } from '@/modules/template';
import { detectionService } from '../services/detectionService';
import { DRAFT_ID, useDetectionStore } from '../store/detectionStore';
import {
  DETECTION_KIND_LABEL,
  type DetectionDefinition,
  type DetectionKind,
  type DetectionResult,
  type RgbColor,
} from '../types';
import { newDetection } from '../types';
import { DetectionEditor } from './DetectionEditor';
// TrainingDialog replaced by full TrainingPanel — opened from DetectionsView. The "Train" button
// in this panel's header is now a no-op hint; users start training from the Detections tab.
import './DetectionsPanel.css';

interface CaptureState {
  pngBase64: string;
  width: number;
  height: number;
}

interface HoverState { x: number; y: number; r: number; g: number; b: number; }

/**
 * 3-pane panel: detection list (left) | captured frame canvas (center) | editor (right).
 *
 * Workflow:
 *   1. Pick the target window + Capture a frame.
 *   2. Click "Add" to start a new detection draft, or pick an existing one from the list.
 *   3. Edit fields in the right pane (kind, ROI, options, output bindings).
 *      ROI: drag on the canvas. Color picks: alt+click on the canvas.
 *   4. Click "Test" to run the in-memory definition against the current frame — overlay
 *      shows the result (template bbox / blob list / fill strip).
 *   5. Save persists to data/profiles/{id}/detections/{slug}.json. Scripts pick it up
 *      automatically next Run via `detect.run('name')` / `detect.runAll()`.
 */
export const DetectionsPanel: React.FC = () => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const detections = useDetectionStore((s) => s.detections);
  const draft = useDetectionStore((s) => s.draft);
  const lastResults = useDetectionStore((s) => s.lastResults);
  const setDetections = useDetectionStore((s) => s.setDetections);
  const upsert = useDetectionStore((s) => s.upsert);
  const removeFromStore = useDetectionStore((s) => s.remove);
  const setDraft = useDetectionStore((s) => s.setDraft);
  const setResult = useDetectionStore((s) => s.setResult);

  const [windows, setWindows] = useState<WindowInfo[]>([]);
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [capture, setCapture] = useState<CaptureState | undefined>();
  const [grabbing, setGrabbing] = useState(false);
  const [hover, setHover] = useState<HoverState | undefined>();
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [testing, setTesting] = useState(false);
  const [saving, setSaving] = useState(false);
  const [trainOpen, setTrainOpen] = useState(false);

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);

  // ---------- bootstrap ----------

  const refreshWindows = useCallback(async () => {
    const list = await captureService.listWindows();
    setWindows(list);
    if (list.length > 0 && !windowHandle) setWindowHandle(list[0].handle);
  }, [windowHandle]);

  const refreshDetections = useCallback(async () => {
    if (!profileId) return;
    const { detections: defs } = await detectionService.list(profileId);
    setDetections(defs);
  }, [profileId, setDetections]);

  const refreshTemplates = useCallback(async () => {
    if (!profileId) return;
    const { templates: list } = await templateService.list(profileId);
    setTemplates(list);
  }, [profileId]);

  useEffect(() => { void refreshWindows(); }, [refreshWindows]);
  useEffect(() => { void refreshDetections(); }, [refreshDetections]);
  useEffect(() => { void refreshTemplates(); }, [refreshTemplates]);

  // ---------- frame capture ----------

  const grab = useCallback(async () => {
    if (!windowHandle) {
      message.warning(t('detection.pickWindowFirst', 'Pick a window first.'));
      return;
    }
    setGrabbing(true);
    try {
      const r = await captureService.grabPng(windowHandle);
      setCapture(r);
    } catch (err) { message.error(String(err)); }
    finally { setGrabbing(false); }
  }, [windowHandle, t]);

  // ---------- canvas render ----------

  const draftResult = draft ? lastResults[draft.id || DRAFT_ID] : undefined;

  useEffect(() => {
    if (!capture || !canvasRef.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      canvas.width = capture.width;
      canvas.height = capture.height;
      ctx.drawImage(img, 0, 0);
      drawOverlay(ctx, capture, draft, draftResult);
    };
    img.src = `data:image/png;base64,${capture.pngBase64}`;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [capture, draft?.roi?.x, draft?.roi?.y, draft?.roi?.w, draft?.roi?.h, draftResult]);

  // ---------- mouse handlers ----------

  const canvasToPixel = (e: React.MouseEvent<HTMLCanvasElement>): { x: number; y: number } | null => {
    const canvas = canvasRef.current;
    if (!canvas || !capture) return null;
    const rect = canvas.getBoundingClientRect();
    const scaleX = capture.width / rect.width;
    const scaleY = capture.height / rect.height;
    return {
      x: Math.max(0, Math.min(capture.width - 1, Math.round((e.clientX - rect.left) * scaleX))),
      y: Math.max(0, Math.min(capture.height - 1, Math.round((e.clientY - rect.top) * scaleY))),
    };
  };

  const sampleColor = (x: number, y: number): RgbColor | null => {
    // Sample a 5×5 window around (x,y) and take the median R/G/B independently — robust
    // against single-pixel highlights / anti-aliased edges (the previous single-pixel sample
    // routinely picked an edge pixel that didn't represent the bulk fill color).
    const canvas = canvasRef.current;
    if (!canvas || !capture) return null;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    const half = 2;
    const sx = Math.max(0, x - half);
    const sy = Math.max(0, y - half);
    const sw = Math.min(capture.width - sx, half * 2 + 1);
    const sh = Math.min(capture.height - sy, half * 2 + 1);
    const data = ctx.getImageData(sx, sy, sw, sh).data;
    const rs: number[] = [], gs: number[] = [], bs: number[] = [];
    for (let i = 0; i < data.length; i += 4) {
      rs.push(data[i]); gs.push(data[i + 1]); bs.push(data[i + 2]);
    }
    const median = (xs: number[]) => xs.sort((a, b) => a - b)[Math.floor(xs.length / 2)];
    return { r: median(rs), g: median(gs), b: median(bs) };
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    const c = sampleColor(p.x, p.y);
    setHover({ x: p.x, y: p.y, r: c?.r ?? 0, g: c?.g ?? 0, b: c?.b ?? 0 });
    if (dragStartRef.current && draft) {
      const start = dragStartRef.current;
      // Canvas drag always writes absolute pixel coords — drop any anchor / fromDetection so
      // the runner uses x/y/w/h directly. Switch back to anchored / linked from the editor.
      const roi = {
        x: Math.min(start.x, p.x),
        y: Math.min(start.y, p.y),
        w: Math.abs(p.x - start.x),
        h: Math.abs(p.y - start.y),
      };
      setDraft({ ...draft, roi });
    }
  };

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p || !draft) return;
    if (e.altKey) {
      // alt+click → pick color into the active kind's color field.
      const c = sampleColor(p.x, p.y);
      if (!c) return;
      if (draft.kind === 'colorPresence' && draft.colorPresence) {
        setDraft({ ...draft, colorPresence: { ...draft.colorPresence, color: c } });
      } else if (draft.kind === 'progressBar' && draft.progressBar) {
        setDraft({ ...draft, progressBar: { ...draft.progressBar, fillColor: c } });
      } else {
        message.info(t('detection.colorPickWrongKind', 'Color pick only applies to Color Presence / Progress Bar kinds.'));
        return;
      }
      message.success(t('detection.colorPicked', 'Picked rgb({{r}}, {{g}}, {{b}})', { r: c.r, g: c.g, b: c.b }));
      return;
    }
    dragStartRef.current = p;
    setDraft({ ...draft, roi: { x: p.x, y: p.y, w: 0, h: 0 } });
  };

  const onMouseUp = () => { dragStartRef.current = null; };
  const onMouseLeave = () => { dragStartRef.current = null; setHover(undefined); };

  // ---------- list actions ----------

  const onAdd = (kind: DetectionKind) => {
    const fresh = newDetection(kind);
    fresh.id = '';  // backend assigns slug from name on save
    setDraft(fresh);
  };

  const selectDetection = (def: DetectionDefinition) => {
    // Deep clone so editor edits don't mutate the store entry until save.
    setDraft(JSON.parse(JSON.stringify(def)));
  };

  const onDelete = async (def: DetectionDefinition) => {
    if (!profileId) return;
    try {
      await detectionService.delete(profileId, def.id);
      removeFromStore(def.id);
    } catch (err) { message.error(String(err)); }
  };

  // ---------- editor actions ----------

  const onTest = async () => {
    if (!profileId || !draft || !capture) return;
    setTesting(true);
    try {
      const r = await detectionService.test(profileId, draft, capture.pngBase64);
      setResult(draft.id || DRAFT_ID, r);
    } catch (err) { message.error(String(err)); }
    finally { setTesting(false); }
  };

  const onSave = async () => {
    if (!profileId || !draft) return;
    if (!draft.name.trim()) { message.warning(t('detection.needName', 'Give the detection a name first.')); return; }
    setSaving(true);
    try {
      const saved = await detectionService.save(profileId, draft);
      upsert(saved);
      setDraft(JSON.parse(JSON.stringify(saved)));
      message.success(t('detection.saved', 'Saved'));
    } catch (err) { message.error(String(err)); }
    finally { setSaving(false); }
  };

  if (!profileId) {
    return (
      <div className="detections-panel">
        <CompactAlert type="info" message={t('detection.selectProfile', 'Select a profile to manage detections.')} />
      </div>
    );
  }

  return (
    <div className="detections-panel">
      {/* Top bar — window picker + capture */}
      <div className="detections-panel__bar">
        <CompactSpace wrap>
          <CompactSelect
            showSearch
            placeholder={t('runner.pickWindow', 'Pick a window')}
            value={windowHandle}
            onChange={setWindowHandle}
            filterOption={(input, opt) => String(opt?.label ?? '').toLowerCase().includes(input.toLowerCase())}
            options={windows.map((w) => ({
              value: w.handle,
              label: `${w.title} — ${w.processName} ${w.width}×${w.height}`,
            }))}
            style={{ minWidth: 320 }}
          />
          <Tooltip title={t('runner.refreshWindows', 'Refresh windows')}>
            <CompactButton icon={<ReloadOutlined />} onClick={refreshWindows} />
          </Tooltip>
          <CompactPrimaryButton icon={<CameraOutlined />} loading={grabbing} onClick={grab}>
            {t('detection.grab', 'Capture frame')}
          </CompactPrimaryButton>
          {capture && <Tag>{capture.width} × {capture.height}</Tag>}
        </CompactSpace>
      </div>

      <div className="detections-panel__grid">
        {/* ----- left pane: list ----- */}
        <div className="detections-panel__list-wrap">
          <div className="detections-panel__list-header">
            <span>{t('detection.list.title', 'Detections')} ({detections.length})</span>
            <CompactSelect<string>
              size="small"
              placeholder={t('detection.list.add', '+ Add')}
              value={undefined}
              onChange={(v) => onAdd(v as DetectionKind)}
              suffixIcon={<PlusOutlined />}
              options={(Object.keys(DETECTION_KIND_LABEL) as DetectionKind[]).map((k) => ({
                value: k,
                label: t(`detection.kind.${k}`, DETECTION_KIND_LABEL[k]),
              }))}
              style={{ width: 110 }}
            />
          </div>
          <div className="detections-panel__list-body">
            {detections.length === 0 ? (
              <div className="detections-panel__list-empty">
                {t('detection.list.empty', 'No detections yet.')}
              </div>
            ) : (
              detections.map((def) => (
                <DetectionRow
                  key={def.id}
                  def={def}
                  active={draft?.id === def.id}
                  onSelect={() => selectDetection(def)}
                  onDelete={() => void onDelete(def)}
                  result={lastResults[def.id]}
                />
              ))
            )}
          </div>
        </div>

        {/* ----- center pane: canvas ----- */}
        <div className="detections-panel__canvas-wrap">
          {capture ? (
            <>
              <canvas
                ref={canvasRef}
                className="detections-panel__canvas"
                onMouseDown={onMouseDown}
                onMouseMove={onMouseMove}
                onMouseUp={onMouseUp}
                onMouseLeave={onMouseLeave}
              />
              <div className="detections-panel__hud">
                {hover && (
                  <div className="detections-panel__hud-row">
                    ({hover.x}, {hover.y}) · rgb({hover.r}, {hover.g}, {hover.b})
                  </div>
                )}
                {draft?.roi && (
                  <div className="detections-panel__hud-row">
                    roi {draft.roi.w}×{draft.roi.h} @ ({draft.roi.x}, {draft.roi.y})
                  </div>
                )}
                <div className="detections-panel__hud-row detections-panel__hud-row--hint">
                  {t('detection.canvasHint', 'drag = ROI · alt+click = pick color')}
                </div>
              </div>
            </>
          ) : (
            <div className="detections-panel__empty">
              <CameraOutlined className="detections-panel__empty-icon" />
              <div className="detections-panel__empty-title">
                {t('detection.empty.title', 'No frame captured yet')}
              </div>
              <div className="detections-panel__empty-hint">
                {t('detection.empty.hint', 'Pick the target window above, then press Capture.')}
              </div>
            </div>
          )}
        </div>

        {/* ----- right pane: editor ----- */}
        <div className="detections-panel__editor">
          <div className="detections-panel__editor-header">
            <span>{t('detection.editor.title', 'Editor')}</span>
            {draft && (
              <CompactSpace size={4}>
                {draft.kind === 'progressBar' && (
                  <Tooltip title={t('detection.editor.train.tip', 'Drop labeled samples and let the trainer derive color, tolerance, and direction.')}>
                    <CompactButton
                      size="small"
                      icon={<ExperimentOutlined />}
                      onClick={() => setTrainOpen(true)}
                    >
                      {t('detection.editor.train', 'Train')}
                    </CompactButton>
                  </Tooltip>
                )}
                <CompactButton
                  size="small"
                  icon={<PlayCircleOutlined />}
                  loading={testing}
                  disabled={!capture}
                  onClick={onTest}
                >
                  {t('detection.editor.test', 'Test')}
                </CompactButton>
                <CompactPrimaryButton
                  size="small"
                  icon={<SaveOutlined />}
                  loading={saving}
                  onClick={onSave}
                >
                  {t('detection.editor.save', 'Save')}
                </CompactPrimaryButton>
              </CompactSpace>
            )}
          </div>
          {draft ? (
            <div className="detections-panel__editor-body">
              <DetectionEditor
                draft={draft}
                siblingDetections={detections}
                templates={templates}
                onChange={setDraft}
              />
              {draftResult && <DetectionResultBanner result={draftResult} />}
            </div>
          ) : (
            <div className="detections-panel__editor-empty">
              {t('detection.editor.empty', 'Pick a detection from the list, or add a new one to start editing.')}
            </div>
          )}
        </div>
      </div>

      {/* Training is now driven from the Detections tab via TrainingPanel — see DetectionsView. */}
      {trainOpen && (() => { setTrainOpen(false); message.info('Use the Detections tab → Train new for the wizard.'); return null; })()}
    </div>
  );
};

// ---------- list row ----------

const DetectionRow: React.FC<{
  def: DetectionDefinition;
  active: boolean;
  result: DetectionResult | undefined;
  onSelect: () => void;
  onDelete: () => void;
}> = ({ def, active, result, onSelect, onDelete }) => {
  const { t } = useTranslation();
  return (
    <div
      className={classNames('detection-row', { 'detection-row--active': active, 'detection-row__disabled': !def.enabled })}
      onClick={onSelect}
    >
      <div className="detection-row__main">
        <span className="detection-row__name">{def.name || def.id}</span>
        <span className="detection-row__kind">
          {t(`detection.kind.${def.kind}`, DETECTION_KIND_LABEL[def.kind])}
          {result && ' · ' + summarizeResult(result)}
        </span>
      </div>
      <CompactSpace size={2}>
        <Tooltip title={t('detection.selectThis', 'Open in editor')}>
          <CompactButton size="small" type="text" icon={<AimOutlined />} onClick={(e) => { e.stopPropagation(); onSelect(); }} />
        </Tooltip>
        <Popconfirm
          title={t('detection.deleteConfirm', 'Delete this detection?')}
          okText={t('common.delete')}
          cancelText={t('common.cancel')}
          okButtonProps={{ danger: true }}
          onConfirm={(e) => { e?.stopPropagation(); onDelete(); }}
          onCancel={(e) => e?.stopPropagation()}
        >
          <CompactDangerButton size="small" type="text" icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()} />
        </Popconfirm>
      </CompactSpace>
    </div>
  );
};

const DetectionResultBanner: React.FC<{ result: DetectionResult }> = ({ result }) => {
  const tone = result.found ? 'ok' : 'off';
  return (
    <div className={classNames('detection-result-banner', `detection-result-banner--${tone}`)}>
      {summarizeResult(result)} · {result.durationMs.toFixed(1)}ms
    </div>
  );
};

function summarizeResult(r: DetectionResult): string {
  if (!r.found) return 'no match';
  if (r.kind === 'progressBar' && r.value != null) return `fill ${(r.value * 100).toFixed(1)}%`;
  if (r.kind === 'effect') return r.triggered ? `triggered (${(r.value ?? 0).toFixed(2)})` : `quiet (${(r.value ?? 0).toFixed(2)})`;
  if (r.kind === 'colorPresence') return `${r.value ?? r.blobs?.length ?? 0} blobs`;
  if (r.kind === 'region' && r.match) return `region ${r.match.w}×${r.match.h} @ (${r.match.x}, ${r.match.y})`;
  if (r.match) return `(${r.match.cx}, ${r.match.cy}) ${(r.confidence ?? 0).toFixed(2)}`;
  return 'matched';
}

// ---------- canvas overlay ----------

function drawOverlay(
  ctx: CanvasRenderingContext2D,
  capture: CaptureState,
  draft: DetectionDefinition | undefined,
  result: DetectionResult | undefined,
) {
  // ROI dashed in blue
  if (draft?.roi && draft.roi.w > 0 && draft.roi.h > 0) {
    ctx.strokeStyle = '#1890ff';
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 4]);
    ctx.strokeRect(draft.roi.x + 0.5, draft.roi.y + 0.5, draft.roi.w, draft.roi.h);
    ctx.fillStyle = 'rgba(24, 144, 255, 0.10)';
    ctx.fillRect(draft.roi.x, draft.roi.y, draft.roi.w, draft.roi.h);
  }
  if (!result) return;

  const label = draft?.output?.overlay?.label || draft?.name || result.kind;
  const color = draft?.output?.overlay?.color || (result.found ? '#52c41a' : '#fa8c16');

  if (result.kind === 'progressBar') {
    if (result.match) outline(ctx, result.match, '#722ed1', 'dashed');
    if (result.strip) labeledRect(ctx, result.strip, color, `${label}: ${(result.value ?? 0 * 100).toFixed(1)}% — fill ${((result.value ?? 0) * 100).toFixed(1)}%`);
  } else if (result.kind === 'colorPresence' && result.blobs) {
    for (const b of result.blobs) {
      labeledRect(ctx, b, color, `${label}`);
    }
  } else if (result.match) {
    const conf = result.confidence != null ? ` · ${result.confidence.toFixed(3)}` : '';
    const triggered = result.triggered != null ? (result.triggered ? ' · TRIGGERED' : '') : '';
    labeledRect(ctx, result.match, color, `${label}${conf}${triggered}`);
  }
}

function outline(ctx: CanvasRenderingContext2D, r: { x: number; y: number; w: number; h: number }, color: string, style: 'solid' | 'dashed') {
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.setLineDash(style === 'dashed' ? [6, 3] : []);
  ctx.strokeRect(r.x + 0.5, r.y + 0.5, r.w, r.h);
}

function labeledRect(ctx: CanvasRenderingContext2D, r: { x: number; y: number; w: number; h: number }, color: string, label: string) {
  ctx.strokeStyle = color;
  ctx.lineWidth = 3;
  ctx.setLineDash([]);
  ctx.strokeRect(r.x + 0.5, r.y + 0.5, r.w, r.h);
  ctx.fillStyle = withAlpha(color, 0.15);
  ctx.fillRect(r.x, r.y, r.w, r.h);
  ctx.font = '14px sans-serif';
  const tw = ctx.measureText(label).width;
  ctx.fillStyle = withAlpha(color, 0.9);
  ctx.fillRect(r.x, Math.max(0, r.y - 18), tw + 8, 18);
  ctx.fillStyle = '#ffffff';
  ctx.fillText(label, r.x + 4, Math.max(12, r.y - 5));
}

function withAlpha(hex: string, alpha: number): string {
  // Accept 3 or 6 digit hex; default to opaque if parse fails.
  const m = hex.replace('#', '');
  if (m.length !== 3 && m.length !== 6) return hex;
  const expand = m.length === 3 ? m.split('').map((c) => c + c).join('') : m;
  const r = parseInt(expand.slice(0, 2), 16);
  const g = parseInt(expand.slice(2, 4), 16);
  const b = parseInt(expand.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
