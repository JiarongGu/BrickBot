import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Slider, Tooltip, Upload, message } from 'antd';
import {
  CameraOutlined,
  CheckCircleOutlined,
  DeleteOutlined,
  InboxOutlined,
  PlayCircleOutlined,
  ReloadOutlined,
  StopOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactAlert,
  CompactButton,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSegmented,
  CompactSelect,
  CompactSpace,
} from '@/shared/components/compact';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import { detectionService } from '../services/detectionService';
import { newDetection } from '../types';
import type {
  DetectionDefinition,
  DetectionKind,
  DetectionResult,
  DetectionRoi,
  TrainingResult,
  TrainingSample,
} from '../types';
import './TrainingPanel.css';

type Step = 'setup' | 'samples' | 'roi' | 'train' | 'save';

interface BasicSampleRow {
  id: string;
  imageBase64: string;
  width: number;
  height: number;
  label: string;
}

/**
 * Two-pane review of captured samples: thumbnail strip on the left, big preview canvas
 * on the right with the kind-aware label widget. Replaces the old single-row strip where
 * tiny inline inputs made labeling 10+ frames painful and the user couldn't review the
 * actual frame content.
 *
 * Bulk tools live above the preview:
 *   - "Auto-distribute 0..1" (ProgressBar) — evenly spaces fills across samples in capture order
 *   - "Apply to all" — broadcasts the current label to every sample (great for negative-only batches)
 *
 * Keyboard nav: ↑/↓ or J/K cycle samples; 0–9 numeric keys assign labels (×0.1 for ProgressBar).
 */
const SamplesReviewPane: React.FC<{
  kind: DetectionKind;
  samples: BasicSampleRow[];
  selected: number;
  onSelect: (i: number) => void;
  onLabel: (i: number, label: string) => void;
  onRemove: (i: number) => void;
  onBulkLabels: (labels: string[]) => void;
}> = ({ kind, samples, selected, onSelect, onLabel, onRemove, onBulkLabels }) => {
  const { t } = useTranslation();
  const previewRef = useRef<HTMLCanvasElement | null>(null);
  const current = samples[selected];

  // Render the big preview when the selected sample changes.
  useEffect(() => {
    if (!current || !previewRef.current) return;
    const c = previewRef.current;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      // Draw at native resolution; CSS scales to fit the pane.
      c.width = current.width;
      c.height = current.height;
      ctx.drawImage(img, 0, 0);
    };
    img.src = `data:image/png;base64,${current.imageBase64}`;
  }, [current?.id, current?.imageBase64, current?.width, current?.height]);

  // Keyboard nav. Bind to the pane via tabIndex so it focuses on click.
  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.target instanceof HTMLInputElement) return;  // don't steal label input keystrokes
    if (e.key === 'ArrowDown' || e.key === 'j') { e.preventDefault(); onSelect(Math.min(samples.length - 1, selected + 1)); }
    else if (e.key === 'ArrowUp' || e.key === 'k') { e.preventDefault(); onSelect(Math.max(0, selected - 1)); }
    else if (e.key >= '0' && e.key <= '9') {
      e.preventDefault();
      const n = Number(e.key);
      if (kind === 'progressBar') onLabel(selected, (n / 10).toFixed(2));
      else if (kind === 'colorPresence') onLabel(selected, String(n));
    }
  };

  const autoDistribute = () => {
    if (samples.length < 2) return;
    const labels = samples.map((_, i) => (i / (samples.length - 1)).toFixed(2));
    onBulkLabels(labels);
  };

  const applyToAll = () => {
    if (!current) return;
    onBulkLabels(samples.map(() => current.label));
  };

  return (
    <div className="samples-review" tabIndex={0} onKeyDown={onKeyDown}>
      <div className="samples-review__strip">
        <div className="samples-review__strip-header">
          {t('detection.train.samples.count', '{{n}} samples', { n: samples.length })}
        </div>
        <div className="samples-review__strip-body">
          {samples.map((s, i) => (
            <div
              key={s.id}
              className={classNames('samples-review__row', { 'samples-review__row--active': i === selected })}
              onClick={() => onSelect(i)}
            >
              <img className="samples-review__thumb" src={`data:image/png;base64,${s.imageBase64}`} alt="" />
              <div className="samples-review__row-info">
                <div className="samples-review__row-name">#{i + 1}</div>
                <div className="samples-review__row-label">{s.label || '—'}</div>
              </div>
              <CompactDangerButton
                size="small"
                type="text"
                icon={<DeleteOutlined />}
                onClick={(e) => { e.stopPropagation(); onRemove(i); }}
              />
            </div>
          ))}
        </div>
      </div>

      <div className="samples-review__preview">
        {current ? (
          <>
            <div className="samples-review__preview-header">
              <span className="samples-review__preview-title">
                #{selected + 1} of {samples.length} · {current.width}×{current.height}
              </span>
              <CompactSpace size={4}>
                {kind === 'progressBar' && (
                  <Tooltip title={t('detection.train.autoDistribute.tip', 'Spread labels evenly from 0 to 1 across samples (capture order).')}>
                    <CompactButton size="small" onClick={autoDistribute} disabled={samples.length < 2}>
                      {t('detection.train.autoDistribute', 'Auto 0→1')}
                    </CompactButton>
                  </Tooltip>
                )}
                <Tooltip title={t('detection.train.applyAll.tip', 'Set every sample\'s label to the current value.')}>
                  <CompactButton size="small" onClick={applyToAll} disabled={!current.label.trim()}>
                    {t('detection.train.applyAll', 'Apply to all')}
                  </CompactButton>
                </Tooltip>
              </CompactSpace>
            </div>
            <div className="samples-review__canvas-wrap">
              <canvas ref={previewRef} className="samples-review__canvas" />
            </div>
            <SampleLabelWidget kind={kind} value={current.label} onChange={(v) => onLabel(selected, v)} />
            <div className="samples-review__hint">
              {t('detection.train.kbHint', '↑/↓ or J/K to cycle samples · 0–9 to set label')}
            </div>
          </>
        ) : (
          <div className="samples-review__empty">{t('detection.train.noSample', 'Capture a sample first.')}</div>
        )}
      </div>
    </div>
  );
};

/**
 * Kind-aware label editor. Each kind gets the right widget:
 *   - ProgressBar → 0..1 slider + numeric input + 0/25/50/75/100 quick buttons
 *   - Element / FeatureMatch → ✓ Positive / ✗ Negative toggles (writes "true" / "false")
 *   - Effect → Quiet / Trigger toggles
 *   - ColorPresence → integer stepper input (count of matched blobs)
 */
const SampleLabelWidget: React.FC<{
  kind: DetectionKind;
  value: string;
  onChange: (v: string) => void;
}> = ({ kind, value, onChange }) => {
  const { t } = useTranslation();

  if (kind === 'progressBar') {
    const num = Number.isFinite(parseFloat(value)) ? parseFloat(value) : 0;
    return (
      <div className="samples-review__label">
        <span className="samples-review__label-key">{t('detection.train.label', 'Label')}: <b>{value || '—'}</b></span>
        <div className="samples-review__quickbuttons">
          {[0, 0.25, 0.5, 0.75, 1].map((v) => (
            <CompactButton
              key={v}
              size="small"
              type={Math.abs(num - v) < 0.01 ? 'primary' : 'text'}
              onClick={() => onChange(v.toFixed(2))}
            >
              {(v * 100).toFixed(0)}%
            </CompactButton>
          ))}
        </div>
        <Slider
          min={0}
          max={1}
          step={0.01}
          value={num}
          onChange={(v) => onChange((v as number).toFixed(2))}
        />
      </div>
    );
  }

  if (kind === 'template' || kind === 'featureMatch') {
    const isTrue = ['true', 'yes', '1', '+', 'positive'].includes(value.trim().toLowerCase());
    const isFalse = !isTrue && value.trim().length > 0;
    return (
      <div className="samples-review__label">
        <span className="samples-review__label-key">{t('detection.train.label', 'Label')}:</span>
        <CompactSpace size={4}>
          <CompactButton size="small" type={isTrue ? 'primary' : 'text'} onClick={() => onChange('true')}>
            ✓ {t('detection.train.positive', 'Positive (visible)')}
          </CompactButton>
          <CompactButton size="small" type={isFalse ? 'primary' : 'text'} onClick={() => onChange('false')}>
            ✗ {t('detection.train.negative', 'Negative (absent)')}
          </CompactButton>
        </CompactSpace>
      </div>
    );
  }

  if (kind === 'effect') {
    const isTrigger = ['trigger', 'true', '1', '+', 'yes'].includes(value.trim().toLowerCase());
    const isQuiet = !isTrigger && value.trim().length > 0;
    return (
      <div className="samples-review__label">
        <span className="samples-review__label-key">{t('detection.train.label', 'Label')}:</span>
        <CompactSpace size={4}>
          <CompactButton size="small" type={isQuiet ? 'primary' : 'text'} onClick={() => onChange('quiet')}>
            {t('detection.train.quiet', 'Quiet (effect absent)')}
          </CompactButton>
          <CompactButton size="small" type={isTrigger ? 'primary' : 'text'} onClick={() => onChange('trigger')}>
            {t('detection.train.trigger', 'Trigger (effect active)')}
          </CompactButton>
        </CompactSpace>
      </div>
    );
  }

  // ColorPresence: integer count.
  const count = Math.max(0, Math.floor(Number(value) || 0));
  return (
    <div className="samples-review__label">
      <span className="samples-review__label-key">
        {t('detection.train.labelCount', 'Expected count')}: <b>{count}</b>
      </span>
      <CompactSpace size={4}>
        <CompactButton size="small" onClick={() => onChange(String(Math.max(0, count - 1)))}>−</CompactButton>
        <CompactInput
          size="small"
          value={value}
          onChange={(e) => onChange(e.target.value.replace(/[^\d]/g, ''))}
          style={{ width: 80, textAlign: 'center' }}
        />
        <CompactButton size="small" onClick={() => onChange(String(count + 1))}>+</CompactButton>
        <span style={{ color: 'var(--color-text-tertiary)', fontSize: 12 }}>
          {[0, 1, 2, 3, 5].map((v) => (
            <CompactButton
              key={v}
              size="small"
              type={count === v ? 'primary' : 'text'}
              onClick={() => onChange(String(v))}
            >
              {v}
            </CompactButton>
          ))}
        </span>
      </CompactSpace>
    </div>
  );
};

/**
 * Per-sample diagnostic card: thumbnail + label/prediction text + ROI/match overlay.
 * Renders the sample image into a small canvas (capped at 280×160 logical px), then draws
 * the predicted bbox / fill strip / blob list on top so the user can see WHY the trainer
 * scored each sample the way it did.
 */
const DiagnosticThumb: React.FC<{
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
  if (r.kind === 'progressBar') {
    if (r.match) line(r.match.x, r.match.y, r.match.w, r.match.h, true);
    if (r.strip) {
      ctx.fillStyle = color + '33';
      ctx.fillRect(r.strip.x * scale, r.strip.y * scale, r.strip.w * scale, r.strip.h * scale);
      line(r.strip.x, r.strip.y, r.strip.w, r.strip.h);
    }
  } else if (r.kind === 'colorPresence' && r.blobs) {
    for (const b of r.blobs) line(b.x, b.y, b.w, b.h);
  } else if (r.match) {
    line(r.match.x, r.match.y, r.match.w, r.match.h);
  }
}

interface SampleRow {
  id: string;
  imageBase64: string;
  width: number;
  height: number;
  label: string;
  capturedAt: number;
}

interface Props {
  onCancel: () => void;
  onSaved: (saved: DetectionDefinition) => void;
  /** Pre-load this detection's saved samples into the wizard for re-training. */
  reTrainDetection?: DetectionDefinition;
}

const TRAINING_KINDS: { kind: DetectionKind; title: string; desc: string; labelHint: string }[] = [
  {
    kind: 'progressBar',
    title: 'Progress Bar',
    desc: 'Track HP / MP / cooldown fill. Train with screenshots at different fill levels (e.g. 0.1, 0.5, 1.0).',
    labelHint: 'fill 0..1',
  },
  {
    kind: 'template',
    title: 'Element',
    desc: 'Find a UI icon: buff, alert, button, debuff. Train with positives (visible) and negatives (absent).',
    labelHint: 'true / false',
  },
  {
    kind: 'featureMatch',
    title: 'Sprite / Character',
    desc: 'Multi-scale element match — like Element but tolerates UI scaling differences across resolutions.',
    labelHint: 'true / false',
  },
  {
    kind: 'colorPresence',
    title: 'Color Presence',
    desc: 'Count colored markers: loot drops, glowing enemies, marked tiles. Train with screenshots labeled with the count.',
    labelHint: 'integer count',
  },
  {
    kind: 'effect',
    title: 'Visual Effect',
    desc: 'Detect a flash/animation/buff appearing in a fixed spot. Train with quiet samples (effect absent) and trigger samples (effect active).',
    labelHint: 'trigger / quiet',
  },
];

/**
 * 5-step wizard for training a detection from labeled samples.
 *   1. Setup    — pick kind + name
 *   2. Samples  — capture from window OR record multi-frame OR upload, label each
 *   3. ROI      — drag rect on a representative sample (or pick a high-variance suggestion)
 *   4. Train    — run trainer, see suggested config + per-sample diagnostics, tune sliders
 *   5. Save     — output bindings (ctxKey/event/overlay), save detection
 */
export const TrainingPanel: React.FC<Props> = ({ onCancel, onSaved, reTrainDetection }) => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [step, setStep] = useState<Step>(reTrainDetection ? 'samples' : 'setup');
  const [kind, setKind] = useState<DetectionKind>(reTrainDetection?.kind ?? 'progressBar');
  const [draft, setDraft] = useState<DetectionDefinition>(() =>
    reTrainDetection ? JSON.parse(JSON.stringify(reTrainDetection)) : newDetection('progressBar'),
  );
  const [windows, setWindows] = useState<WindowInfo[]>([]);
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [samples, setSamples] = useState<SampleRow[]>([]);
  const [selected, setSelected] = useState<number>(0);

  const [captureMode, setCaptureMode] = useState<'snapshot' | 'record'>('snapshot');
  const [recordIntervalMs, setRecordIntervalMs] = useState(500);
  const [recordDurationS, setRecordDurationS] = useState(10);
  const [recording, setRecording] = useState(false);
  const recordTimerRef = useRef<number | null>(null);
  const recordEndRef = useRef<number>(0);

  const [trainingResult, setTrainingResult] = useState<TrainingResult | undefined>();
  const [training, setTraining] = useState(false);
  const [tuneTolerance, setTuneTolerance] = useState<number | undefined>();
  const [tuneLineThreshold, setTuneLineThreshold] = useState<number | undefined>();
  const [tuneMinConfidence, setTuneMinConfidence] = useState<number | undefined>();

  const [suggestions, setSuggestions] = useState<{ x: number; y: number; w: number; h: number; score: number; reason: string }[]>([]);
  const [suggestBusy, setSuggestBusy] = useState(false);
  const [saving, setSaving] = useState(false);
  /** Per-sample DetectionResult — drives the diagnostic thumbnail overlays. */
  const [samplePredictions, setSamplePredictions] = useState<Record<string, DetectionResult>>({});

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);

  const labelHint = TRAINING_KINDS.find((k) => k.kind === kind)?.labelHint ?? 'label';

  // ---------- bootstrap ----------

  const refreshWindows = useCallback(async () => {
    const list = await captureService.listWindows();
    setWindows(list);
    if (list.length > 0 && !windowHandle) setWindowHandle(list[0].handle);
  }, [windowHandle]);

  useEffect(() => { void refreshWindows(); }, [refreshWindows]);

  // Cleanup record timer on unmount
  useEffect(() => () => { if (recordTimerRef.current) clearInterval(recordTimerRef.current); }, []);

  // Re-train flow: pre-load saved samples for the chosen detection.
  useEffect(() => {
    if (!reTrainDetection?.id || !profileId) return;
    let cancelled = false;
    (async () => {
      try {
        const r = await detectionService.listSamples(profileId, reTrainDetection.id, true);
        if (cancelled) return;
        const rows: SampleRow[] = r.samples
          .filter((s) => s.imageBase64)
          .map((s) => ({
            id: s.id,
            imageBase64: s.imageBase64!,
            width: s.width,
            height: s.height,
            label: s.label ?? '',
            capturedAt: new Date(s.capturedAt).getTime(),
          }));
        setSamples(rows);
        if (rows.length > 0) {
          message.info(t('detection.train.samplesLoaded', 'Loaded {{n}} saved samples.', { n: rows.length }));
        }
      } catch { /* fall through — empty start */ }
    })();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reTrainDetection?.id, profileId]);

  // ---------- step transitions ----------

  const stepOrder: Step[] = ['setup', 'samples', 'roi', 'train', 'save'];
  const stepIndex = stepOrder.indexOf(step);

  const canGoNext = (): boolean => {
    if (step === 'setup') return draft.name.trim().length > 0;
    if (step === 'samples') return samples.length >= 2 && samples.every((s) => s.label.trim().length > 0);
    if (step === 'roi') return !!draft.roi && draft.roi.w > 0 && draft.roi.h > 0;
    if (step === 'train') return !!trainingResult?.suggested;
    return true;
  };

  const next = () => {
    const i = stepIndex;
    if (i < stepOrder.length - 1) setStep(stepOrder[i + 1]);
    if (stepOrder[i + 1] === 'train' && !trainingResult && samples.length >= 2) {
      void runTraining();
    }
  };
  const back = () => { if (stepIndex > 0) setStep(stepOrder[stepIndex - 1]); };

  // ---------- step 1: setup ----------

  const pickKind = (k: DetectionKind) => {
    setKind(k);
    const fresh = newDetection(k);
    setDraft({ ...fresh, name: draft.name });
    setTrainingResult(undefined);
  };

  // ---------- step 2: samples ----------

  const grabFrame = useCallback(async (): Promise<{ b64: string; w: number; h: number } | null> => {
    if (!windowHandle) return null;
    try {
      const r = await captureService.grabPng(windowHandle);
      return { b64: r.pngBase64, w: r.width, h: r.height };
    } catch (e) {
      message.error(String(e));
      return null;
    }
  }, [windowHandle]);

  const addSample = useCallback((b64: string, w: number, h: number) => {
    setSamples((prev) => [
      ...prev,
      {
        id: `s-${Date.now()}-${prev.length}`,
        imageBase64: b64,
        width: w,
        height: h,
        label: '',
        capturedAt: Date.now(),
      },
    ]);
  }, []);

  const onSnapshot = async () => {
    const f = await grabFrame();
    if (f) addSample(f.b64, f.w, f.h);
  };

  const startRecording = () => {
    if (!windowHandle) {
      message.warning(t('detection.train.pickWindowFirst', 'Pick a window first.'));
      return;
    }
    setRecording(true);
    recordEndRef.current = Date.now() + recordDurationS * 1000;
    const tick = async () => {
      if (Date.now() >= recordEndRef.current) {
        if (recordTimerRef.current) clearInterval(recordTimerRef.current);
        recordTimerRef.current = null;
        setRecording(false);
        return;
      }
      const f = await grabFrame();
      if (f) addSample(f.b64, f.w, f.h);
    };
    void tick();
    recordTimerRef.current = window.setInterval(() => { void tick(); }, recordIntervalMs);
  };

  const stopRecording = () => {
    if (recordTimerRef.current) clearInterval(recordTimerRef.current);
    recordTimerRef.current = null;
    setRecording(false);
  };

  const onUpload = async (file: File) => {
    const buf = await file.arrayBuffer();
    const bytes = new Uint8Array(buf);
    let bin = '';
    for (let i = 0; i < bytes.byteLength; i++) bin += String.fromCharCode(bytes[i]);
    const b64 = btoa(bin);
    // Decode dimensions client-side via Image
    await new Promise<void>((resolve) => {
      const img = new Image();
      img.onload = () => { addSample(b64, img.width, img.height); resolve(); };
      img.onerror = () => { resolve(); };
      img.src = `data:image/png;base64,${b64}`;
    });
    return false;
  };

  const setLabel = (i: number, label: string) =>
    setSamples((s) => s.map((row, idx) => (idx === i ? { ...row, label } : row)));

  const removeSample = (i: number) => {
    setSamples((s) => s.filter((_, idx) => idx !== i));
    if (selected >= i && selected > 0) setSelected(selected - 1);
  };

  // ---------- step 3: ROI canvas ----------

  const currentSample = samples[selected];

  useEffect(() => {
    if (step !== 'roi' && step !== 'train') return;
    if (!currentSample || !canvasRef.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      canvas.width = currentSample.width;
      canvas.height = currentSample.height;
      ctx.drawImage(img, 0, 0);
      drawOverlay(ctx);
    };
    img.src = `data:image/png;base64,${currentSample.imageBase64}`;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentSample?.id, draft.roi?.x, draft.roi?.y, draft.roi?.w, draft.roi?.h, step, trainingResult, suggestions]);

  const drawOverlay = (ctx: CanvasRenderingContext2D) => {
    if (draft.roi && draft.roi.w > 0 && draft.roi.h > 0) {
      ctx.strokeStyle = '#1890ff';
      ctx.lineWidth = 2;
      ctx.setLineDash([6, 4]);
      ctx.strokeRect(draft.roi.x + 0.5, draft.roi.y + 0.5, draft.roi.w, draft.roi.h);
      ctx.fillStyle = 'rgba(24, 144, 255, 0.10)';
      ctx.fillRect(draft.roi.x, draft.roi.y, draft.roi.w, draft.roi.h);
    }
    if (step === 'roi') {
      // Draw ROI suggestions in orange.
      ctx.strokeStyle = '#fa8c16';
      ctx.lineWidth = 1;
      ctx.setLineDash([3, 3]);
      for (const s of suggestions) {
        ctx.strokeRect(s.x + 0.5, s.y + 0.5, s.w, s.h);
      }
    }
  };

  const canvasToPixel = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const c = canvasRef.current;
    if (!c || !currentSample) return null;
    const rect = c.getBoundingClientRect();
    const sx = currentSample.width / rect.width;
    const sy = currentSample.height / rect.height;
    return {
      x: Math.max(0, Math.min(currentSample.width - 1, Math.round((e.clientX - rect.left) * sx))),
      y: Math.max(0, Math.min(currentSample.height - 1, Math.round((e.clientY - rect.top) * sy))),
    };
  };

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    dragStartRef.current = p;
    setDraft({ ...draft, roi: { x: p.x, y: p.y, w: 0, h: 0 } });
  };
  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!dragStartRef.current) return;
    const p = canvasToPixel(e);
    if (!p) return;
    const start = dragStartRef.current;
    setDraft({
      ...draft,
      roi: {
        x: Math.min(start.x, p.x), y: Math.min(start.y, p.y),
        w: Math.abs(p.x - start.x), h: Math.abs(p.y - start.y),
      },
    });
  };
  const onMouseUp = () => { dragStartRef.current = null; };

  const onSuggestRois = async () => {
    if (samples.length < 2) {
      message.info(t('detection.train.suggestNeedsMulti', 'Capture at least 2 frames to suggest dynamic regions.'));
      return;
    }
    setSuggestBusy(true);
    try {
      const r = await detectionService.suggestRois(samples.map((s) => s.imageBase64), 5);
      setSuggestions(r.suggestions);
      if (r.suggestions.length === 0) {
        message.info(t('detection.train.noSuggestions', 'No high-variance regions found.'));
      }
    } catch (e) { message.error(String(e)); }
    finally { setSuggestBusy(false); }
  };

  // ---------- step 4: train + tune ----------

  const runTraining = useCallback(async () => {
    if (!profileId) return;
    if (samples.length < 2) {
      message.warning(t('detection.train.needAtLeastTwo', 'Add at least 2 labeled samples.'));
      return;
    }
    setTraining(true);
    setTrainingResult(undefined);
    try {
      const tsamples: TrainingSample[] = samples.map((s) => ({
        imageBase64: s.imageBase64,
        label: s.label.trim(),
        roi: draft.roi,
      }));
      const r = await detectionService.train(profileId, kind, tsamples, draft);
      setTrainingResult(r);
      // Seed tune sliders from suggestion
      if (r.suggested?.progressBar) {
        setTuneTolerance(r.suggested.progressBar.tolerance);
        setTuneLineThreshold(r.suggested.progressBar.lineThreshold);
      }
      if (r.suggested?.template) setTuneMinConfidence(r.suggested.template.minConfidence);
      if (r.suggested?.colorPresence) setTuneTolerance(r.suggested.colorPresence.tolerance);
      // Merge suggestion into draft (preserves the user's name).
      if (r.suggested) {
        setDraft({ ...r.suggested, id: draft.id, name: draft.name, group: draft.group, output: draft.output });
        // Run TEST per sample so the diagnostic thumbnails can render predicted overlays.
        const previews: Record<string, DetectionResult> = {};
        for (const s of samples) {
          try {
            const tr = await detectionService.test(profileId, r.suggested, s.imageBase64);
            previews[s.id] = tr;
          } catch { /* skip — diagnostic row still shows text from trainer */ }
        }
        setSamplePredictions(previews);
      }
    } catch (e) { message.error(String(e)); }
    finally { setTraining(false); }
  }, [profileId, samples, kind, draft, t]);

  const reTest = useCallback(async () => {
    if (!profileId || !trainingResult?.suggested) return;
    // Apply tune values to a copy and ask backend to test against each sample.
    const tuned: DetectionDefinition = JSON.parse(JSON.stringify(trainingResult.suggested));
    if (tuned.progressBar) {
      if (tuneTolerance !== undefined) tuned.progressBar.tolerance = tuneTolerance;
      if (tuneLineThreshold !== undefined) tuned.progressBar.lineThreshold = tuneLineThreshold;
    }
    if (tuned.template && tuneMinConfidence !== undefined) tuned.template.minConfidence = tuneMinConfidence;
    if (tuned.colorPresence && tuneTolerance !== undefined) tuned.colorPresence.tolerance = tuneTolerance;

    // Run TEST per sample to refresh diagnostics + populate prediction previews.
    const newDiagnostics: typeof trainingResult.diagnostics = [];
    const previews: Record<string, DetectionResult> = {};
    for (const s of samples) {
      try {
        const r = await detectionService.test(profileId, tuned, s.imageBase64);
        previews[s.id] = r;
        const predicted = r.kind === 'progressBar' ? (r.value ?? 0).toFixed(3)
          : r.kind === 'template' ? `${r.found ? 'true' : 'false'} (${(r.confidence ?? 0).toFixed(2)})`
          : r.kind === 'colorPresence' ? String(r.value ?? r.blobs?.length ?? 0)
          : r.kind === 'effect' ? (r.triggered ? 'trigger' : 'quiet') + ` (${(r.value ?? 0).toFixed(3)})`
          : (r.found ? 'true' : 'false');
        const labelNum = parseFloat(s.label);
        const predNum = parseFloat(predicted);
        const err = !isNaN(labelNum) && !isNaN(predNum) ? Math.abs(labelNum - predNum) : (predicted.startsWith(s.label.trim().toLowerCase()) ? 0 : 1);
        newDiagnostics.push({ label: s.label, predicted, error: err });
      } catch { newDiagnostics.push({ label: s.label, predicted: 'error', error: 1 }); }
    }
    setSamplePredictions(previews);
    setTrainingResult({ ...trainingResult, suggested: tuned, diagnostics: newDiagnostics });
    setDraft({ ...tuned, id: draft.id, name: draft.name, group: draft.group, output: draft.output });
  }, [profileId, samples, trainingResult, tuneTolerance, tuneLineThreshold, tuneMinConfidence, draft]);

  // ---------- step 5: save ----------

  const onSave = async () => {
    if (!profileId || !draft.name.trim()) return;
    setSaving(true);
    try {
      const saved = await detectionService.save(profileId, draft);
      // Persist labeled samples for re-training later.
      try {
        await detectionService.saveSamples(profileId, saved.id,
          samples.map((s) => ({ id: s.id, imageBase64: s.imageBase64, label: s.label })),
          true);
      } catch (e) {
        // Non-fatal: detection saved even if sample persistence fails.
        console.warn('Failed to persist training samples', e);
        message.warning(t('detection.train.samplesNotSaved', 'Detection saved, but training samples failed to persist.'));
      }
      message.success(t('detection.train.saved', 'Detection saved.'));
      onSaved(saved);
    } catch (e) { message.error(String(e)); }
    finally { setSaving(false); }
  };

  // ---------- render ----------

  if (!profileId) {
    return (
      <div className="training-panel">
        <CompactAlert type="info" message={t('detection.train.selectProfile', 'Select a profile first.')} />
      </div>
    );
  }

  const stepLabels: Record<Step, string> = {
    setup: t('detection.train.step.setup', '1. Setup'),
    samples: t('detection.train.step.samples', '2. Samples'),
    roi: t('detection.train.step.roi', '3. ROI'),
    train: t('detection.train.step.train', '4. Train & Tune'),
    save: t('detection.train.step.save', '5. Save'),
  };

  return (
    <div className="training-panel">
      <div className="training-panel__steps">
        {stepOrder.map((s, i) => (
          <React.Fragment key={s}>
            {i > 0 && <span className="training-panel__step-arrow">›</span>}
            <span
              className={classNames('training-panel__step-dot', {
                'training-panel__step-dot--active': step === s,
                'training-panel__step-dot--done': stepIndex > i,
              })}
              onClick={() => stepIndex >= i && setStep(s)}
            >
              {stepIndex > i && <CheckCircleOutlined />}
              {stepLabels[s]}
            </span>
          </React.Fragment>
        ))}
      </div>

      <div className="training-panel__body">
        {step === 'setup' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.setup.title', 'What kind of detection?')}</div>
            <div className="training-kind-grid">
              {TRAINING_KINDS.map((k) => (
                <div
                  key={k.kind}
                  className={classNames('training-kind-card', { 'training-kind-card--active': kind === k.kind })}
                  onClick={() => pickKind(k.kind)}
                >
                  <div className="training-kind-card__title">{t(`detection.kind.${k.kind}`, k.title)}</div>
                  <div className="training-kind-card__desc">
                    {t(`detection.train.kindDesc.${k.kind}`, k.desc)}
                  </div>
                </div>
              ))}
            </div>
            <div className="training-step__title" style={{ marginTop: 8 }}>{t('detection.field.name', 'Name')}</div>
            <CompactInput
              value={draft.name}
              placeholder={t('detection.placeholder.name', 'e.g. HP Bar') as string}
              onChange={(e) => setDraft({ ...draft, name: e.target.value })}
            />
          </div>
        )}

        {step === 'samples' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.samples.title', 'Capture or upload samples')}</div>
            <div className="training-step__hint">
              {t('detection.train.samples.hint', 'Need at least 2. Label each — for this kind: ')}<b>{labelHint}</b>
            </div>
            <CompactSpace wrap>
              <CompactSelect
                showSearch
                placeholder={t('runner.pickWindow', 'Pick a window')}
                value={windowHandle}
                onChange={setWindowHandle}
                options={windows.map((w) => ({ value: w.handle, label: `${w.title} — ${w.processName}` }))}
                style={{ minWidth: 320 }}
              />
              <Tooltip title={t('runner.refreshWindows', 'Refresh windows')}>
                <CompactButton icon={<ReloadOutlined />} onClick={refreshWindows} />
              </Tooltip>
              <CompactSegmented
                value={captureMode}
                onChange={(v) => setCaptureMode(v as 'snapshot' | 'record')}
                options={[
                  { value: 'snapshot', label: t('detection.train.mode.snapshot', 'Snapshot') },
                  { value: 'record', label: t('detection.train.mode.record', 'Record') },
                ]}
              />
              {captureMode === 'snapshot' ? (
                <CompactPrimaryButton icon={<CameraOutlined />} onClick={() => void onSnapshot()}>
                  {t('detection.train.snap', 'Capture frame')}
                </CompactPrimaryButton>
              ) : recording ? (
                <CompactDangerButton icon={<StopOutlined />} onClick={stopRecording}>
                  {t('detection.train.stop', 'Stop')}
                </CompactDangerButton>
              ) : (
                <CompactPrimaryButton icon={<PlayCircleOutlined />} onClick={startRecording}>
                  {t('detection.train.record', 'Record')}
                </CompactPrimaryButton>
              )}
            </CompactSpace>

            {captureMode === 'record' && (
              <div className="training-record-bar">
                <span style={{ fontSize: 12, color: 'var(--color-text-secondary)' }}>
                  {t('detection.train.duration', 'Duration')}:
                </span>
                <CompactSelect
                  size="small"
                  value={recordDurationS}
                  onChange={(v) => setRecordDurationS(v as number)}
                  options={[5, 10, 30, 60].map((n) => ({ value: n, label: `${n}s` }))}
                  style={{ width: 80 }}
                />
                <span style={{ fontSize: 12, color: 'var(--color-text-secondary)' }}>
                  {t('detection.train.interval', 'Interval')}:
                </span>
                <CompactSelect
                  size="small"
                  value={recordIntervalMs}
                  onChange={(v) => setRecordIntervalMs(v as number)}
                  options={[200, 500, 1000, 2000].map((n) => ({ value: n, label: `${n}ms` }))}
                  style={{ width: 90 }}
                />
                {recording && (
                  <div className="training-record-bar__progress">
                    <div
                      className="training-record-bar__progress-fill"
                      style={{ width: `${Math.min(100, ((Date.now() - (recordEndRef.current - recordDurationS * 1000)) / (recordDurationS * 1000)) * 100)}%` }}
                    />
                  </div>
                )}
              </div>
            )}

            <Upload.Dragger
              accept="image/png,image/jpeg"
              showUploadList={false}
              multiple
              beforeUpload={(file) => { void onUpload(file); return false; }}
            >
              <p className="ant-upload-drag-icon"><InboxOutlined /></p>
              <p>{t('detection.train.upload', 'Drop sample images here or click to upload.')}</p>
            </Upload.Dragger>

            {samples.length > 0 && (
              <SamplesReviewPane
                kind={kind}
                samples={samples}
                selected={selected}
                onSelect={setSelected}
                onLabel={setLabel}
                onRemove={removeSample}
                onBulkLabels={(labels) => setSamples((prev) => prev.map((s, i) => ({ ...s, label: labels[i] ?? s.label })))}
              />
            )}
          </div>
        )}

        {step === 'roi' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.roi.title', 'Where is the thing?')}</div>
            <div className="training-step__hint">
              {t('detection.train.roi.hint', 'Drag a rectangle on the sample, or auto-detect dynamic regions from your recording.')}
            </div>
            <div className="training-samples-grid">
              <div>
                <CompactSpace>
                  <CompactButton size="small" loading={suggestBusy} icon={<ThunderboltOutlined />} onClick={() => void onSuggestRois()}>
                    {t('detection.train.suggestRois', 'Suggest dynamic regions')}
                  </CompactButton>
                </CompactSpace>
                <div className="training-sample-strip" style={{ marginTop: 8 }}>
                  {suggestions.map((s, i) => (
                    <div
                      key={i}
                      className="training-suggest-row"
                      onClick={() => setDraft({ ...draft, roi: { x: s.x, y: s.y, w: s.w, h: s.h } })}
                    >
                      <span style={{ flex: 1 }}>{s.reason}</span>
                      <span style={{ color: 'var(--color-text-tertiary)' }}>
                        ({s.x},{s.y}) {s.w}×{s.h}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
              <div className="training-canvas-wrap">
                {currentSample ? (
                  <canvas
                    ref={canvasRef}
                    className="training-canvas"
                    onMouseDown={onMouseDown}
                    onMouseMove={onMouseMove}
                    onMouseUp={onMouseUp}
                    onMouseLeave={onMouseUp}
                  />
                ) : (
                  <div style={{ color: 'var(--color-text-tertiary)' }}>
                    {t('detection.train.noSample', 'Capture a sample first.')}
                  </div>
                )}
              </div>
            </div>
            {draft.roi && (
              <div className="training-summary">
                ROI: ({draft.roi.x}, {draft.roi.y}) {draft.roi.w}×{draft.roi.h}
              </div>
            )}
          </div>
        )}

        {step === 'train' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.run.title', 'Train & tune')}</div>
            <CompactSpace>
              <CompactPrimaryButton loading={training} icon={<ThunderboltOutlined />} onClick={() => void runTraining()}>
                {t('detection.train.run', 'Run training')}
              </CompactPrimaryButton>
              {trainingResult && (
                <CompactButton onClick={() => void reTest()}>
                  {t('detection.train.retest', 'Re-test with tuning')}
                </CompactButton>
              )}
            </CompactSpace>

            {trainingResult && (
              <>
                <div className="training-summary">{trainingResult.summary}</div>

                {/* Kind-specific tune sliders */}
                {kind === 'progressBar' && tuneTolerance !== undefined && (
                  <>
                    <div>
                      <span style={{ fontSize: 12 }}>{t('detection.field.tolerance', 'Tolerance')}: ±{tuneTolerance}</span>
                      <Slider min={5} max={120} step={1} value={tuneTolerance} onChange={(v) => setTuneTolerance(v)} />
                    </div>
                    <div>
                      <span style={{ fontSize: 12 }}>{t('detection.field.lineThreshold', 'Line threshold')}: {(tuneLineThreshold ?? 0.4).toFixed(2)}</span>
                      <Slider min={0.1} max={0.95} step={0.05} value={tuneLineThreshold ?? 0.4} onChange={(v) => setTuneLineThreshold(v)} />
                    </div>
                  </>
                )}
                {kind === 'template' && tuneMinConfidence !== undefined && (
                  <div>
                    <span style={{ fontSize: 12 }}>{t('detection.field.minConfidence', 'Min confidence')}: {tuneMinConfidence.toFixed(2)}</span>
                    <Slider min={0.5} max={0.99} step={0.01} value={tuneMinConfidence} onChange={(v) => setTuneMinConfidence(v)} />
                  </div>
                )}
                {kind === 'colorPresence' && tuneTolerance !== undefined && (
                  <div>
                    <span style={{ fontSize: 12 }}>{t('detection.field.tolerance', 'Tolerance')}: ±{tuneTolerance}</span>
                    <Slider min={5} max={120} step={1} value={tuneTolerance} onChange={(v) => setTuneTolerance(v)} />
                  </div>
                )}

                <div className="diagnostic-thumbs">
                  {samples.map((sample, i) => {
                    const d = trainingResult.diagnostics[i];
                    if (!d) return null;
                    return (
                      <DiagnosticThumb
                        key={sample.id}
                        sample={sample}
                        index={i}
                        diagnostic={d}
                        prediction={samplePredictions[sample.id]}
                      />
                    );
                  })}
                </div>
              </>
            )}
          </div>
        )}

        {step === 'save' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.save.title', 'Output bindings & save')}</div>
            <div className="training-step__hint">
              {t('detection.train.save.hint', 'Wire the detection result into your scripts. Leave fields blank to skip.')}
            </div>
            <div>
              <span style={{ fontSize: 12 }}>{t('detection.field.name', 'Name')}</span>
              <CompactInput value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} />
            </div>
            <div>
              <span style={{ fontSize: 12 }}>{t('detection.field.group', 'Group')}</span>
              <CompactInput
                value={draft.group ?? ''}
                placeholder={t('detection.placeholder.group', 'optional') as string}
                onChange={(e) => setDraft({ ...draft, group: e.target.value || undefined })}
              />
            </div>
            <div>
              <span style={{ fontSize: 12 }}>{t('detection.output.ctxKey', 'ctx key')}</span>
              <CompactInput
                value={draft.output.ctxKey ?? ''}
                placeholder="hp"
                onChange={(e) => setDraft({ ...draft, output: { ...draft.output, ctxKey: e.target.value || undefined } })}
              />
            </div>
            <div>
              <span style={{ fontSize: 12 }}>{t('detection.output.event', 'brickbot event')}</span>
              <CompactInput
                value={draft.output.event ?? ''}
                placeholder="hpChanged"
                onChange={(e) => setDraft({ ...draft, output: { ...draft.output, event: e.target.value || undefined } })}
              />
            </div>
          </div>
        )}
      </div>

      <div className="training-panel__footer">
        <CompactButton onClick={onCancel}>{t('common.cancel')}</CompactButton>
        <CompactSpace>
          <CompactButton onClick={back} disabled={stepIndex === 0}>{t('detection.train.back', 'Back')}</CompactButton>
          {step === 'save' ? (
            <CompactPrimaryButton loading={saving} onClick={() => void onSave()}>
              {t('detection.train.saveDetection', 'Save detection')}
            </CompactPrimaryButton>
          ) : (
            <CompactPrimaryButton onClick={next} disabled={!canGoNext()}>
              {t('detection.train.next', 'Next')}
            </CompactPrimaryButton>
          )}
        </CompactSpace>
      </div>
    </div>
  );
};
