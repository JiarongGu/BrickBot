import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Slider, Tooltip, Upload, message } from 'antd';
import {
  CameraOutlined,
  CheckCircleOutlined,
  InboxOutlined,
  PlayCircleOutlined,
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
import { WindowSelector } from '@/shared/components/common';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import { detectionService } from '../services/detectionService';
import { recordingService } from '@/modules/recording';
import { newDetection } from '../types';
import type {
  DetectionDefinition,
  DetectionKind,
  DetectionResult,
  DetectionRoi,
  TrainingResult,
  TrainingSample,
} from '../types';
import { SamplesReviewPane, DiagnosticThumb } from './training';
import type { SampleRecord } from './training';
import './TrainingPanel.css';

type Step = 'setup' | 'samples' | 'roi' | 'train' | 'save';


interface Props {
  onCancel: () => void;
  onSaved: (saved: DetectionDefinition) => void;
  /** Pre-load this detection's saved samples into the wizard for re-training. */
  reTrainDetection?: DetectionDefinition;
}

const TRAINING_KINDS: { kind: DetectionKind; title: string; desc: string; labelHint: string }[] = [
  {
    kind: 'tracker',
    title: 'Tracker (moving element / character)',
    desc: 'Follow an element as it moves. Pick one frame, drag a rectangle on the element, choose KCF / CSRT / MIL. One-shot — no labeled samples needed.',
    labelHint: 'one-shot (no labels)',
  },
  {
    kind: 'pattern',
    title: 'Pattern (visual feature)',
    desc: 'Detect element appearance via ORB descriptors. Background-invariant. Train with positives (visible) and negatives (absent).',
    labelHint: 'true / false',
  },
  {
    kind: 'text',
    title: 'Text (OCR)',
    desc: 'OCR the contents of a region — buff names, status banners. One-shot: drag a rectangle, pick language, save.',
    labelHint: 'one-shot (no labels)',
  },
  {
    kind: 'bar',
    title: 'Bar (HP / MP / cooldown)',
    desc: 'Track meter fill. Train with screenshots at different fill levels (e.g. 0.1, 0.5, 1.0).',
    labelHint: 'fill 0..1',
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
  const [kind, setKind] = useState<DetectionKind>(reTrainDetection?.kind ?? 'pattern');
  const [draft, setDraft] = useState<DetectionDefinition>(() =>
    reTrainDetection ? JSON.parse(JSON.stringify(reTrainDetection)) : newDetection('pattern'),
  );
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [samples, setSamples] = useState<SampleRecord[]>([]);
  const [selected, setSelected] = useState<number>(0);
  /** Multi-selected sample IDs for bulk operations (delete, apply-label-to-selection).
   *  Distinct from `selected` (preview cursor). Cleared after every bulk op. */
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const [captureMode, setCaptureMode] = useState<'snapshot' | 'record' | 'recording' | 'upload'>('snapshot');
  const [recordings, setRecordings] = useState<{ id: string; name: string; frameCount: number }[]>([]);
  const [pickedRecordingId, setPickedRecordingId] = useState<string | undefined>();
  const [loadingFromRecording, setLoadingFromRecording] = useState(false);
  /** Frame subset filter — start / end are inclusive frame indices, stride 1 = every frame. */
  const [frameRangeStart, setFrameRangeStart] = useState(0);
  const [frameRangeEnd, setFrameRangeEnd] = useState(0);
  const [frameStride, setFrameStride] = useState(1);
  const [recordIntervalMs, setRecordIntervalMs] = useState(500);
  const [recordDurationS, setRecordDurationS] = useState(10);
  const [recording, setRecording] = useState(false);
  /** 0..1 progress through the active recording, ticking every 100ms so the bar animates. */
  const [recordProgress, setRecordProgress] = useState(0);
  const recordTimerRef = useRef<number | null>(null);
  const recordProgressTimerRef = useRef<number | null>(null);
  const recordStartRef = useRef<number>(0);
  const recordEndRef = useRef<number>(0);
  /** Brief snapshot flash overlay key — bumped each capture so the CSS animation re-fires.
   *  Currently consumed by a small header pulse; could drive a canvas-wrap overlay later. */
  const [flashKey, setFlashKey] = useState(0);

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

  // Cleanup record timers on unmount
  useEffect(() => () => {
    if (recordTimerRef.current) clearInterval(recordTimerRef.current);
    if (recordProgressTimerRef.current) clearInterval(recordProgressTimerRef.current);
  }, []);

  // Auto-select the newest sample so users see what they just captured in the preview pane.
  // The strip auto-scrolls because the active row has a different background — the layout
  // doesn't need an explicit scrollIntoView.
  useEffect(() => {
    if (samples.length === 0) return;
    setSelected(samples.length - 1);
  }, [samples.length]);

  // Load recordings list when entering Samples step or switching modes.
  useEffect(() => {
    if (!profileId) return;
    recordingService.list(profileId)
      .then((r) => setRecordings(r.recordings.map((rec) => ({ id: rec.id, name: rec.name, frameCount: rec.frameCount }))))
      .catch(() => undefined);
  }, [profileId]);

  // Auto-fill the range when the user picks a recording so the inputs reflect "all frames"
  // without forcing them to type the upper bound by hand.
  useEffect(() => {
    if (!pickedRecordingId) return;
    const meta = recordings.find((r) => r.id === pickedRecordingId);
    if (!meta) return;
    setFrameRangeStart(0);
    setFrameRangeEnd(Math.max(0, meta.frameCount - 1));
    setFrameStride(1);
  }, [pickedRecordingId, recordings]);

  /** Indices the load button will pull, given range + stride. Memoized so the live count
   *  preview stays cheap as the user scrubs the inputs. */
  const selectedFrameIndices = useMemo(() => {
    if (!pickedRecordingId) return [];
    const meta = recordings.find((r) => r.id === pickedRecordingId);
    if (!meta) return [];
    const start = Math.max(0, Math.min(meta.frameCount - 1, frameRangeStart));
    const end = Math.max(start, Math.min(meta.frameCount - 1, frameRangeEnd));
    const stride = Math.max(1, frameStride);
    const out: number[] = [];
    for (let i = start; i <= end; i += stride) out.push(i);
    return out;
  }, [pickedRecordingId, recordings, frameRangeStart, frameRangeEnd, frameStride]);

  /** Pull a SUBSET of frames from a saved recording into the samples list. Honors the
   *  start / end / stride filters so a 10-minute recording at 500ms intervals (1200 frames)
   *  can be reduced to e.g. every 30th frame for a 40-sample training set.
   *  Frames are fetched in parallel — sequential `await` on 49 IPC round-trips was the
   *  primary cause of the 5–10s "Load frames" stall users hit on big recordings. */
  const loadFromRecording = useCallback(async (recordingId: string) => {
    if (!profileId) return;
    if (selectedFrameIndices.length === 0) return;
    setLoadingFromRecording(true);
    try {
      const ts = Date.now();
      const fetched = await Promise.all(
        selectedFrameIndices.map(async (i) => {
          const f = await recordingService.getFrame(profileId, recordingId, i).catch(() => null);
          if (!f?.imageBase64) return null;
          return {
            id: `rec-${recordingId}-${i}-${ts}`,
            imageBase64: f.imageBase64,
            width: f.width,
            height: f.height,
            label: '',
            capturedAt: new Date(f.capturedAt).getTime(),
          } as SampleRecord;
        }),
      );
      const rows = fetched.filter((r): r is SampleRecord => r !== null);
      setSamples((prev) => [...prev, ...rows]);
      message.success(t('detection.train.recordingLoaded', 'Loaded {{n}} frames.', { n: rows.length }));
    } catch (e) { message.error(String(e)); }
    finally { setLoadingFromRecording(false); }
  }, [profileId, selectedFrameIndices, t]);

  // Re-train flow: pre-load saved samples for the chosen detection.
  useEffect(() => {
    if (!reTrainDetection?.id || !profileId) return;
    let cancelled = false;
    (async () => {
      try {
        const r = await detectionService.listSamples(profileId, reTrainDetection.id, true);
        if (cancelled) return;
        const rows: SampleRecord[] = r.samples
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

  /** One-shot kinds (tracker, text) skip the labeled-samples + train steps:
   *   • tracker: capture ONE frame → drag bbox on it → save (init frame embedded by trainer).
   *   • text   : capture ONE frame → drag bbox on it → save (region cached, OCR runs at runtime).
   * Multi-sample kinds (pattern, bar) still walk the full 5-step wizard. */
  const isOneShot = kind === 'tracker' || kind === 'text';

  const stepOrder: Step[] = isOneShot
    ? ['setup', 'samples', 'roi', 'save']
    : ['setup', 'samples', 'roi', 'train', 'save'];
  const stepIndex = stepOrder.indexOf(step);

  const canGoNext = (): boolean => {
    if (step === 'setup') return draft.name.trim().length > 0;
    if (step === 'samples') {
      // One-shot kinds need exactly 1 frame and no labels; multi-sample kinds need ≥2 labeled.
      if (isOneShot) return samples.length >= 1;
      return samples.length >= 2 && samples.every((s) => s.label.trim().length > 0);
    }
    if (step === 'roi') return !!draft.roi && draft.roi.w > 0 && draft.roi.h > 0;
    if (step === 'train') return !!trainingResult?.suggested;
    return true;
  };

  /** Human-readable explanation of WHY Next is currently disabled — surfaces in a Tooltip
   *  so users don't stare at a grayed-out button wondering what's missing. */
  const nextBlockedReason = (): string | undefined => {
    if (step === 'setup' && !draft.name.trim()) return t('detection.train.nextBlock.name', 'Give the detection a name first.');
    if (step === 'samples') {
      if (isOneShot && samples.length < 1) return t('detection.train.nextBlock.oneSample', 'Capture one frame to use as the reference.');
      if (!isOneShot) {
        if (samples.length < 2) return t('detection.train.nextBlock.minSamples', 'Capture at least 2 samples.');
        const unlabeled = samples.filter((s) => !s.label.trim()).length;
        if (unlabeled > 0) return t('detection.train.nextBlock.labels', '{{n}} sample(s) still need labels.', { n: unlabeled });
      }
    }
    if (step === 'roi' && (!draft.roi || draft.roi.w <= 0 || draft.roi.h <= 0))
      return t('detection.train.nextBlock.roi', 'Drag a rectangle on the preview to mark the element.');
    if (step === 'train' && !trainingResult?.suggested)
      return t('detection.train.nextBlock.train', 'Run training first to produce a suggested config.');
    return undefined;
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
    if (f) {
      addSample(f.b64, f.w, f.h);
      setFlashKey((k) => k + 1);  // brief visual confirmation flash
    }
  };

  const startRecording = () => {
    if (!windowHandle) {
      message.warning(t('detection.train.pickWindowFirst', 'Pick a window first.'));
      return;
    }
    setRecording(true);
    setRecordProgress(0);
    recordStartRef.current = Date.now();
    recordEndRef.current = recordStartRef.current + recordDurationS * 1000;

    const tick = async () => {
      if (Date.now() >= recordEndRef.current) { stopRecording(); return; }
      const f = await grabFrame();
      if (f) addSample(f.b64, f.w, f.h);
    };
    void tick();
    recordTimerRef.current = window.setInterval(() => { void tick(); }, recordIntervalMs);

    // Independent UI ticker — drives the progress bar smoothly (every 100ms) without
    // coupling to the capture cadence. Without this the bar only updates when a frame
    // lands, which is too coarse at long intervals (1s+).
    recordProgressTimerRef.current = window.setInterval(() => {
      const total = recordDurationS * 1000;
      const elapsed = Date.now() - recordStartRef.current;
      setRecordProgress(Math.max(0, Math.min(1, elapsed / total)));
    }, 100);
  };

  const stopRecording = () => {
    if (recordTimerRef.current) { clearInterval(recordTimerRef.current); recordTimerRef.current = null; }
    if (recordProgressTimerRef.current) { clearInterval(recordProgressTimerRef.current); recordProgressTimerRef.current = null; }
    setRecording(false);
    setRecordProgress(0);
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
    // Drop the removed sample from the multi-select set if it was in there.
    setSelectedIds((prev) => {
      const id = samples[i]?.id;
      if (!id || !prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
  };

  // ---------- multi-select / bulk operations ----------

  const toggleSelectId = useCallback((id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }, []);

  const setSelectionIds = useCallback((ids: string[]) => {
    setSelectedIds(new Set(ids));
  }, []);

  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  /** Remove every multi-selected sample. Re-anchors the preview cursor on the
   *  closest surviving sample so the right pane never goes blank when there's
   *  still data left. */
  const removeSelectedSamples = useCallback(() => {
    if (selectedIds.size === 0) return;
    setSamples((prev) => {
      const survivors = prev.filter((s) => !selectedIds.has(s.id));
      // Pick a sensible new preview index — use the same position when possible.
      setSelected((cur) => Math.max(0, Math.min(survivors.length - 1, cur)));
      return survivors;
    });
    setSelectedIds(new Set());
  }, [selectedIds]);

  const clearAllSamples = useCallback(() => {
    setSamples([]);
    setSelectedIds(new Set());
    setSelected(0);
  }, []);

  const applyLabelToSelection = useCallback((label: string) => {
    if (selectedIds.size === 0) return;
    setSamples((prev) => prev.map((s) => (selectedIds.has(s.id) ? { ...s, label } : s)));
  }, [selectedIds]);

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
      if (r.suggested?.bar) {
        setTuneTolerance(r.suggested.bar.tolerance);
        setTuneLineThreshold(r.suggested.bar.lineThreshold);
      }
      if (r.suggested?.pattern) setTuneMinConfidence(r.suggested.pattern.minConfidence);
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
    if (tuned.bar) {
      if (tuneTolerance !== undefined) tuned.bar.tolerance = tuneTolerance;
      if (tuneLineThreshold !== undefined) tuned.bar.lineThreshold = tuneLineThreshold;
    }
    if (tuned.pattern && tuneMinConfidence !== undefined) tuned.pattern.minConfidence = tuneMinConfidence;

    // Run TEST per sample to refresh diagnostics + populate prediction previews.
    const newDiagnostics: typeof trainingResult.diagnostics = [];
    const previews: Record<string, DetectionResult> = {};
    for (const s of samples) {
      try {
        const r = await detectionService.test(profileId, tuned, s.imageBase64);
        previews[s.id] = r;
        const predicted = r.kind === 'bar' ? (r.value ?? 0).toFixed(3)
          : r.kind === 'pattern' ? `${r.found ? 'true' : 'false'} (${(r.confidence ?? 0).toFixed(2)})`
          : r.kind === 'text' ? (r.text ?? '')
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
      // One-shot kinds (tracker / text) skip the multi-sample train step. They still need
      // the trainer to encode runtime artifacts (tracker.initFramePng, text options) before
      // save — so we run training inline here using the first captured frame as the seed.
      // The seed carries the user-drawn bbox (draft.roi → tracker.initX/Y/W/H).
      let toSave = draft;
      if (isOneShot) {
        if (samples.length === 0 || !draft.roi || draft.roi.w <= 0 || draft.roi.h <= 0) {
          throw new Error(t('detection.train.nextBlock.roi', 'Drag a rectangle on the preview to mark the element.') as string);
        }
        // For tracker, transfer the ROI into the kind-specific options block before training.
        // The trainer reads draft.tracker.InitX/Y/W/H to package the init bbox.
        const seed: DetectionDefinition = JSON.parse(JSON.stringify(draft));
        if (kind === 'tracker') {
          seed.tracker = {
            ...(seed.tracker ?? { algorithm: 'kcf', reacquireOnLost: true, initX: 0, initY: 0, initW: 0, initH: 0 }),
            initX: draft.roi.x,
            initY: draft.roi.y,
            initW: draft.roi.w,
            initH: draft.roi.h,
          };
        }
        const trainingSamples: TrainingSample[] = [{
          imageBase64: samples[0].imageBase64,
          label: '',
          roi: draft.roi,
        }];
        const r = await detectionService.train(profileId, kind, trainingSamples, seed);
        if (!r.suggested) throw new Error('Trainer returned no suggested definition.');
        toSave = { ...r.suggested, name: draft.name, group: draft.group, output: draft.output };
      }

      const saved = await detectionService.save(profileId, toSave);
      // Persist labeled samples for re-training later. Skip for one-shot kinds — there's
      // only one unlabeled init frame, no value in storing it as a "sample".
      if (!isOneShot) {
        try {
          await detectionService.saveSamples(profileId, saved.id,
            samples.map((s) => ({ id: s.id, imageBase64: s.imageBase64, label: s.label })),
            true);
        } catch (e) {
          console.warn('Failed to persist training samples', e);
          message.warning(t('detection.train.samplesNotSaved', 'Detection saved, but training samples failed to persist.'));
        }
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

        {step === 'samples' && (() => {
          /** Toolbar JSX rendered in the LEFT column of SamplesReviewPane, above the strip.
           *  Built here (not inline in the pane) so the parent keeps owning capture-mode
           *  state. Single grid cell means the screen-shaped preview canvas dominates the
           *  RIGHT column at full available width. */
          const recordingMeta = recordings.find((r) => r.id === pickedRecordingId);
          const recordingMax = recordingMeta ? Math.max(0, recordingMeta.frameCount - 1) : 0;
          const samplesToolbar = (
            <>
              {/* No icons — block segmented in a 360px column gives each option ~85px;
                  icon + long label truncated ("Snaps...", "From r..."). Short labels alone
                  fit comfortably. "Library" replaces "Recordings" to free up letters. */}
              <CompactSegmented
                value={captureMode}
                onChange={(v) => setCaptureMode(v as 'snapshot' | 'record' | 'recording' | 'upload')}
                bottomGap={0}
                options={[
                  { value: 'snapshot', label: t('detection.train.mode.snapshot', 'Snapshot') },
                  { value: 'record', label: t('detection.train.mode.record', 'Record') },
                  { value: 'recording', label: t('detection.train.mode.recording', 'Library') },
                  { value: 'upload', label: t('detection.train.mode.upload', 'Upload') },
                ]}
              />

              {(captureMode === 'snapshot' || captureMode === 'record') && (
                <WindowSelector
                  value={windowHandle}
                  onChange={setWindowHandle}
                  minWidth={240}
                />
              )}

              {captureMode === 'snapshot' && (
                <CompactPrimaryButton block icon={<CameraOutlined />} onClick={() => void onSnapshot()}>
                  {samples.length > 0
                    ? t('detection.train.snapWithCount', 'Capture frame ({{n}})', { n: samples.length })
                    : t('detection.train.snap', 'Capture frame')}
                </CompactPrimaryButton>
              )}

              {captureMode === 'record' && (
                <>
                  <div className="training-record-bar">
                    <span className="training-record-bar__label">
                      {t('detection.train.duration', 'Duration')}:
                    </span>
                    <CompactSelect
                      size="small"
                      value={recordDurationS}
                      onChange={(v) => setRecordDurationS(v as number)}
                      options={[5, 10, 30, 60].map((n) => ({ value: n, label: `${n}s` }))}
                      style={{ width: 80 }}
                    />
                    <span className="training-record-bar__label">
                      {t('detection.train.interval', 'Interval')}:
                    </span>
                    <CompactSelect
                      size="small"
                      value={recordIntervalMs}
                      onChange={(v) => setRecordIntervalMs(v as number)}
                      options={[200, 500, 1000, 2000].map((n) => ({ value: n, label: `${n}ms` }))}
                      style={{ width: 90 }}
                    />
                  </div>
                  {recording ? (
                    <CompactDangerButton block icon={<StopOutlined />} onClick={stopRecording}>
                      {t('detection.train.stopRecording', 'Stop ({{pct}}%)', { pct: Math.round(recordProgress * 100) })}
                    </CompactDangerButton>
                  ) : (
                    <CompactPrimaryButton block icon={<PlayCircleOutlined />} onClick={startRecording}>
                      {t('detection.train.record', 'Record')}
                    </CompactPrimaryButton>
                  )}
                  {recording && (
                    <div className="training-record-bar training-record-bar--progress">
                      <div className="training-record-bar__progress">
                        <div
                          className="training-record-bar__progress-fill"
                          style={{ width: `${(recordProgress * 100).toFixed(1)}%` }}
                        />
                      </div>
                      <span className="training-record-bar__label training-record-bar__label--muted training-record-bar__label--minw">
                        {samples.length} {t('detection.train.captured', 'captured')}
                      </span>
                    </div>
                  )}
                </>
              )}

              {captureMode === 'recording' && (
                <>
                  <CompactSelect
                    placeholder={t('detection.train.pickRecording', 'Pick a recording') as string}
                    value={pickedRecordingId}
                    onChange={(v) => setPickedRecordingId(v as string)}
                    options={recordings.map((r) => ({ value: r.id, label: `${r.name} (${r.frameCount} frames)` }))}
                    style={{ width: '100%' }}
                  />
                  {pickedRecordingId && (
                    <>
                      {/* Slim slider row with inline readout + stride. Slider's vertical
                          chrome is forced flat (padding/margin 0) so the whole control
                          collapses to ~24px. Load button stays prominent + block below. */}
                      <div className="training-range__row">
                        <Slider
                          range
                          min={0}
                          max={recordingMax}
                          value={[frameRangeStart, frameRangeEnd]}
                          onChange={(v) => {
                            const [s, e] = v as [number, number];
                            setFrameRangeStart(s);
                            setFrameRangeEnd(e);
                          }}
                          className="training-range__slider"
                        />
                        <span className="training-range__readout">
                          {frameRangeStart}–{frameRangeEnd}
                        </span>
                        <Tooltip title={t('detection.train.strideTip', 'Pick every Nth frame') as string}>
                          <CompactInput
                            size="small"
                            value={frameStride}
                            onChange={(e) => setFrameStride(Math.max(1, parseInt(e.target.value, 10) || 1))}
                            style={{ width: 64 }}
                            addonAfter={t('detection.train.strideUnit', 'th') as string}
                          />
                        </Tooltip>
                      </div>
                      <CompactPrimaryButton
                        block
                        loading={loadingFromRecording}
                        disabled={selectedFrameIndices.length === 0}
                        onClick={() => void loadFromRecording(pickedRecordingId)}
                      >
                        {t('detection.train.loadRecordingN', 'Load {{n}} frames', { n: selectedFrameIndices.length })}
                      </CompactPrimaryButton>
                    </>
                  )}
                </>
              )}

              {captureMode === 'upload' && (
                <Upload.Dragger
                  className="training-upload"
                  accept="image/png,image/jpeg"
                  showUploadList={false}
                  multiple
                  beforeUpload={(file) => { void onUpload(file); return false; }}
                >
                  <p className="ant-upload-drag-icon"><InboxOutlined /></p>
                  <p>{t('detection.train.upload', 'Drop images or click to upload.')}</p>
                </Upload.Dragger>
              )}
            </>
          );

          return (
            <div className="training-step training-step--wide">
              {/* No title or hint row — the steps strip says "2. Samples", and the
                  label-format hint is rendered inline in the preview header / empty
                  state. Saves the full top stripe of vertical space for the canvas. */}
              <SamplesReviewPane
                kind={kind}
                samples={samples}
                selected={selected}
                selectedIds={selectedIds}
                toolbar={samplesToolbar}
                labelHint={labelHint}
                onSelect={setSelected}
                onToggleId={toggleSelectId}
                onSetSelectionIds={setSelectionIds}
                onClearSelection={clearSelection}
                onLabel={setLabel}
                onRemove={removeSample}
                onRemoveSelected={removeSelectedSamples}
                onClearAll={clearAllSamples}
                onBulkLabels={(labels) => setSamples((prev) => prev.map((s, i) => ({ ...s, label: labels[i] ?? s.label })))}
                onApplyLabelToSelection={applyLabelToSelection}
              />
            </div>
          );
        })()}

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
                {kind === 'bar' && tuneTolerance !== undefined && (
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
                {kind === 'pattern' && tuneMinConfidence !== undefined && (
                  <div>
                    <span style={{ fontSize: 12 }}>{t('detection.field.minConfidence', 'Min confidence')}: {tuneMinConfidence.toFixed(2)}</span>
                    <Slider min={0.05} max={0.95} step={0.01} value={tuneMinConfidence} onChange={(v) => setTuneMinConfidence(v)} />
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
            <div className="training-step__title">{t('detection.train.save.title', 'Name your detection & save')}</div>
            <div className="training-step__hint">
              {t('detection.train.save.hint', 'Scripts will read this detection by name via detect.run(\'…\'). Pick a memorable name and group to organize.')}
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
              <span style={{ fontSize: 12, color: 'var(--color-text-secondary)' }}>
                {t('detection.train.scriptUsage', 'Use it in a script:')}
              </span>
              <pre className="detection-script-hint">{`const r = detect.run('${draft.name || 'detection-name'}');\n// r.value, r.found, r.match, r.triggered, r.blobs\nctx.set('${draft.name || 'value'}', r.value ?? r.found);`}</pre>
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
            <Tooltip title={canGoNext() ? '' : nextBlockedReason()}>
              <span>
                <CompactPrimaryButton onClick={next} disabled={!canGoNext()}>
                  {t('detection.train.next', 'Next')}
                </CompactPrimaryButton>
              </span>
            </Tooltip>
          )}
        </CompactSpace>
      </div>
    </div>
  );
};
