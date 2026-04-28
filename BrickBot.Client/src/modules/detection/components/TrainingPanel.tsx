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
  CompositeOp,
  DetectionDefinition,
  DetectionKind,
  DetectionModel,
  DetectionResult,
  DetectionRoi,
  TrainingResult,
  TrainingSample,
} from '../types';
import { useDetectionStore } from '../store/detectionStore';
import { AnnotationCanvas, DiagnosticThumb, SamplesReviewPane } from './training';
import type { SampleRecord } from './training';
import './TrainingPanel.css';

type Step = 'setup' | 'samples' | 'annotate' | 'search' | 'train' | 'save' | 'compose';

const TRAINING_KINDS: { kind: DetectionKind; title: string; desc: string; labelHint: string }[] = [
  {
    kind: 'tracker',
    title: 'Tracker (moving element / character)',
    desc: 'Follow an element as it moves. Pick one frame as the init, drag a rectangle on the element, choose KCF / CSRT / MIL.',
    labelHint: 'one-shot (no labels)',
  },
  {
    kind: 'pattern',
    title: 'Pattern (visual feature)',
    desc: 'Detect element appearance via ORB descriptors. Train with positives (visible) and negatives (absent). Each positive needs its own object box.',
    labelHint: 'true / false',
  },
  {
    kind: 'text',
    title: 'Text (OCR)',
    desc: 'OCR the contents of a region — buff names, status banners. Drag a rectangle marking the text on one sample.',
    labelHint: 'one-shot (no labels)',
  },
  {
    kind: 'bar',
    title: 'Bar (HP / MP / cooldown)',
    desc: 'Track meter fill. Annotate each sample with the bar bbox + label the fill ratio (0..1).',
    labelHint: 'fill 0..1',
  },
  {
    kind: 'composite',
    title: 'Composite (AND / OR)',
    desc: 'Combine other detections with AND / OR. No samples needed — pick the operands and a boolean op.',
    labelHint: 'no samples',
  },
];

interface Props {
  onCancel: () => void;
  onSaved: (saved: DetectionDefinition) => void;
  /** Pre-load this detection's saved samples into the wizard for re-training. */
  reTrainDetection?: DetectionDefinition;
}

/**
 * Training wizard — v3.
 *
 * Steps (kind-dependent):
 *   tracker / text   → 1 setup · 2 samples · 3 annotate · 4 save
 *   bar              → 1 setup · 2 samples · 3 annotate · 4 train · 5 save
 *   pattern          → 1 setup · 2 samples · 3 annotate · 4 search ROI · 5 train · 6 save
 *
 * Domain split: TRAINING outputs a paired (DetectionDefinition, DetectionModel). Definition
 * holds runtime knobs, Model holds compiled artifacts (descriptors, init frame, ref patch).
 * Both must be persisted for the runner to use the trained detection.
 */
export const TrainingPanel: React.FC<Props> = ({ onCancel, onSaved, reTrainDetection }) => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);
  const allDetections = useDetectionStore((s) => s.detections);

  const [step, setStep] = useState<Step>(reTrainDetection ? 'samples' : 'setup');
  const [kind, setKind] = useState<DetectionKind>(reTrainDetection?.kind ?? 'pattern');
  const [draft, setDraft] = useState<DetectionDefinition>(() =>
    reTrainDetection ? JSON.parse(JSON.stringify(reTrainDetection)) : newDetection('pattern'),
  );
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [samples, setSamples] = useState<SampleRecord[]>([]);
  const [selected, setSelected] = useState<number>(0);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());

  const [captureMode, setCaptureMode] = useState<'snapshot' | 'record' | 'recording' | 'upload'>('snapshot');
  const [recordings, setRecordings] = useState<{ id: string; name: string; frameCount: number }[]>([]);
  const [pickedRecordingId, setPickedRecordingId] = useState<string | undefined>();
  const [loadingFromRecording, setLoadingFromRecording] = useState(false);
  const [frameRangeStart, setFrameRangeStart] = useState(0);
  const [frameRangeEnd, setFrameRangeEnd] = useState(0);
  const [frameStride, setFrameStride] = useState(1);
  const [recordIntervalMs, setRecordIntervalMs] = useState(500);
  const [recordDurationS, setRecordDurationS] = useState(10);
  const [recording, setRecording] = useState(false);
  const [recordProgress, setRecordProgress] = useState(0);
  const recordTimerRef = useRef<number | null>(null);
  const recordProgressTimerRef = useRef<number | null>(null);
  const recordStartRef = useRef<number>(0);
  const recordEndRef = useRef<number>(0);

  const [trainingResult, setTrainingResult] = useState<TrainingResult | undefined>();
  const [training, setTraining] = useState(false);
  const [tuneTolerance, setTuneTolerance] = useState<number | undefined>();
  const [tuneLineThreshold, setTuneLineThreshold] = useState<number | undefined>();
  const [tuneMinConfidence, setTuneMinConfidence] = useState<number | undefined>();

  const [suggestions, setSuggestions] = useState<{ x: number; y: number; w: number; h: number; score: number; reason: string }[]>([]);
  const [suggestBusy, setSuggestBusy] = useState(false);
  const [saving, setSaving] = useState(false);
  const [samplePredictions, setSamplePredictions] = useState<Record<string, DetectionResult>>({});

  const searchCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);

  const labelHint = TRAINING_KINDS.find((k) => k.kind === kind)?.labelHint ?? 'label';

  // ---------- bootstrap ----------

  useEffect(() => () => {
    if (recordTimerRef.current) clearInterval(recordTimerRef.current);
    if (recordProgressTimerRef.current) clearInterval(recordProgressTimerRef.current);
  }, []);

  useEffect(() => {
    if (samples.length === 0) return;
    setSelected(samples.length - 1);
  }, [samples.length]);

  useEffect(() => {
    if (!profileId) return;
    recordingService.list(profileId)
      .then((r) => setRecordings(r.recordings.map((rec) => ({ id: rec.id, name: rec.name, frameCount: rec.frameCount }))))
      .catch(() => undefined);
  }, [profileId]);

  useEffect(() => {
    if (!pickedRecordingId) return;
    const meta = recordings.find((r) => r.id === pickedRecordingId);
    if (!meta) return;
    setFrameRangeStart(0);
    setFrameRangeEnd(Math.max(0, meta.frameCount - 1));
    setFrameStride(1);
  }, [pickedRecordingId, recordings]);

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

  // Re-train flow: load saved samples + their object boxes.
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
            objectBox: s.objectBox,
            isInit: s.isInit,
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

  // ---------- step transitions (kind-specific shape) ----------

  const stepOrder: Step[] = useMemo(() => {
    if (kind === 'composite') return ['setup', 'compose', 'save'];
    if (kind === 'tracker' || kind === 'text') return ['setup', 'samples', 'annotate', 'save'];
    if (kind === 'bar') return ['setup', 'samples', 'annotate', 'train', 'save'];
    return ['setup', 'samples', 'annotate', 'search', 'train', 'save']; // pattern
  }, [kind]);
  const stepIndex = stepOrder.indexOf(step);

  const annotatedCount = samples.filter((s) => s.objectBox && s.objectBox.w > 0 && s.objectBox.h > 0).length;
  const initCount = samples.filter((s) => s.isInit).length;

  const canGoNext = (): boolean => {
    if (step === 'setup') return draft.name.trim().length > 0;
    if (step === 'compose') return (draft.composite?.detectionIds.length ?? 0) > 0;
    if (step === 'samples') {
      if (kind === 'tracker' || kind === 'text') return samples.length >= 1;
      return samples.length >= 2 && samples.every((s) => s.label.trim().length > 0);
    }
    if (step === 'annotate') {
      if (kind === 'tracker') return initCount === 1 && samples.some((s) => s.isInit && hasValidBox(s.objectBox));
      if (kind === 'text') return samples.some((s) => hasValidBox(s.objectBox));
      if (kind === 'pattern') {
        // Every positive sample needs a box. Negatives don't.
        const posSamples = samples.filter((s) => isPositiveLabel(s.label));
        return posSamples.length > 0 && posSamples.every((s) => hasValidBox(s.objectBox));
      }
      // bar — every sample needs a box.
      return samples.length >= 2 && samples.every((s) => hasValidBox(s.objectBox));
    }
    if (step === 'search') return true; // search ROI is optional
    if (step === 'train') return !!trainingResult?.model;
    return true;
  };

  const nextBlockedReason = (): string | undefined => {
    if (step === 'setup' && !draft.name.trim()) return t('detection.train.nextBlock.name', 'Give the detection a name first.');
    if (step === 'compose' && (draft.composite?.detectionIds.length ?? 0) === 0) {
      return t('detection.train.compose.needsOperands', 'Add at least one operand detection.');
    }
    if (step === 'samples') {
      if ((kind === 'tracker' || kind === 'text') && samples.length < 1) return t('detection.train.nextBlock.oneSample', 'Capture one frame.');
      if (kind === 'pattern' || kind === 'bar') {
        if (samples.length < 2) return t('detection.train.nextBlock.minSamples', 'Capture at least 2 samples.');
        const unlabeled = samples.filter((s) => !s.label.trim()).length;
        if (unlabeled > 0) return t('detection.train.nextBlock.labels', '{{n}} sample(s) still need labels.', { n: unlabeled });
      }
    }
    if (step === 'annotate') {
      if (kind === 'tracker') {
        if (initCount === 0) return t('detection.train.nextBlock.trackerInit', 'Pick exactly one sample as the init frame.');
        if (initCount > 1) return t('detection.train.nextBlock.trackerInitOne', 'Only one sample can be the init frame.');
        const init = samples.find((s) => s.isInit);
        if (!init || !hasValidBox(init.objectBox)) return t('detection.train.nextBlock.trackerBox', 'Drag a box on the init frame.');
      }
      if (kind === 'text') {
        if (!samples.some((s) => hasValidBox(s.objectBox))) return t('detection.train.nextBlock.textBox', 'Drag a box on at least one sample to mark the text region.');
      }
      if (kind === 'pattern') {
        const posSamples = samples.filter((s) => isPositiveLabel(s.label));
        if (posSamples.length === 0) return t('detection.train.nextBlock.patternPos', 'Need at least one positive sample.');
        const missing = posSamples.filter((s) => !hasValidBox(s.objectBox)).length;
        if (missing > 0) return t('detection.train.nextBlock.patternBoxes', '{{n}} positive sample(s) still need an object box.', { n: missing });
      }
      if (kind === 'bar') {
        const missing = samples.filter((s) => !hasValidBox(s.objectBox)).length;
        if (missing > 0) return t('detection.train.nextBlock.barBoxes', '{{n}} sample(s) still need a bar box.', { n: missing });
      }
    }
    if (step === 'train' && !trainingResult?.model)
      return t('detection.train.nextBlock.train', 'Run training first to produce a model.');
    return undefined;
  };

  const next = () => {
    const i = stepIndex;
    const dest = stepOrder[i + 1];
    if (!dest) return;
    setStep(dest);
    if (dest === 'train' && !trainingResult) {
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
    // Switching kinds invalidates per-sample annotations (semantics differ).
    setSamples((prev) => prev.map((s) => ({ ...s, objectBox: undefined, isInit: false })));
  };

  // ---------- step 2: samples (capture / record / load / upload) ----------

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
    setSelectedIds((prev) => {
      const id = samples[i]?.id;
      if (!id || !prev.has(id)) return prev;
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
  };

  // ---------- multi-select / bulk ----------

  const toggleSelectId = useCallback((id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }, []);

  const setSelectionIds = useCallback((ids: string[]) => setSelectedIds(new Set(ids)), []);
  const clearSelection = useCallback(() => setSelectedIds(new Set()), []);

  const removeSelectedSamples = useCallback(() => {
    if (selectedIds.size === 0) return;
    setSamples((prev) => {
      const survivors = prev.filter((s) => !selectedIds.has(s.id));
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

  // ---------- step 3: annotate (per-sample object box) ----------

  const setBoxOn = useCallback((idx: number, box: DetectionRoi | undefined) => {
    setSamples((prev) => prev.map((s, i) => (i === idx ? { ...s, objectBox: box } : s)));
  }, []);

  const setIsInitOn = useCallback((idx: number, init: boolean) => {
    // Tracker init is mutually exclusive across samples.
    setSamples((prev) => prev.map((s, i) => ({ ...s, isInit: i === idx ? init : (init ? false : s.isInit) })));
  }, []);

  const copyBoxFromPrev = useCallback((idx: number) => {
    if (idx === 0) return;
    const prevBox = samples[idx - 1]?.objectBox;
    if (prevBox) setBoxOn(idx, { ...prevBox });
  }, [samples, setBoxOn]);

  const applyBoxToAll = useCallback((idx: number) => {
    const box = samples[idx]?.objectBox;
    if (!box) return;
    setSamples((prev) => prev.map((s) => ({ ...s, objectBox: { ...box } })));
  }, [samples]);

  const clearAllBoxes = useCallback(() => {
    setSamples((prev) => prev.map((s) => ({ ...s, objectBox: undefined, isInit: false })));
  }, []);

  // ---------- step 4: search ROI (pattern only) ----------

  useEffect(() => {
    if (step !== 'search') return;
    const sample = samples[selected];
    if (!sample || !searchCanvasRef.current) return;
    const c = searchCanvasRef.current;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      c.width = sample.width;
      c.height = sample.height;
      ctx.drawImage(img, 0, 0);
      // Search ROI in green dashed
      if (draft.roi && draft.roi.w > 0 && draft.roi.h > 0) {
        ctx.strokeStyle = '#52c41a';
        ctx.lineWidth = 2;
        ctx.setLineDash([6, 4]);
        ctx.strokeRect(draft.roi.x + 0.5, draft.roi.y + 0.5, draft.roi.w, draft.roi.h);
        ctx.fillStyle = 'rgba(82, 196, 26, 0.10)';
        ctx.fillRect(draft.roi.x, draft.roi.y, draft.roi.w, draft.roi.h);
      }
      // Suggestions in orange dashed
      ctx.strokeStyle = '#fa8c16';
      ctx.lineWidth = 1;
      ctx.setLineDash([3, 3]);
      for (const s of suggestions) {
        ctx.strokeRect(s.x + 0.5, s.y + 0.5, s.w, s.h);
      }
    };
    img.src = `data:image/png;base64,${sample.imageBase64}`;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step, samples, selected, draft.roi?.x, draft.roi?.y, draft.roi?.w, draft.roi?.h, suggestions]);

  const searchCanvasToPixel = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const c = searchCanvasRef.current;
    const sample = samples[selected];
    if (!c || !sample) return null;
    const rect = c.getBoundingClientRect();
    const sx = sample.width / rect.width;
    const sy = sample.height / rect.height;
    return {
      x: Math.max(0, Math.min(sample.width - 1, Math.round((e.clientX - rect.left) * sx))),
      y: Math.max(0, Math.min(sample.height - 1, Math.round((e.clientY - rect.top) * sy))),
    };
  };
  const onSearchMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = searchCanvasToPixel(e);
    if (!p) return;
    dragStartRef.current = p;
    setDraft({ ...draft, roi: { x: p.x, y: p.y, w: 0, h: 0 } });
  };
  const onSearchMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!dragStartRef.current) return;
    const p = searchCanvasToPixel(e);
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
  const onSearchMouseUp = () => { dragStartRef.current = null; };

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

  // ---------- step 5: train + diagnostics ----------

  const buildTrainingSamples = (): TrainingSample[] => samples.map((s) => ({
    imageBase64: s.imageBase64,
    label: s.label.trim(),
    objectBox: s.objectBox,
    isInit: s.isInit,
  }));

  const runTraining = useCallback(async () => {
    if (!profileId) return;
    setTraining(true);
    setTrainingResult(undefined);
    try {
      const r = await detectionService.train(profileId, kind, buildTrainingSamples(), draft);
      setTrainingResult(r);
      // Seed tune sliders
      if (r.definition?.bar) {
        setTuneTolerance(r.definition.bar.tolerance);
        setTuneLineThreshold(r.definition.bar.lineThreshold);
      }
      if (r.definition?.pattern) setTuneMinConfidence(r.definition.pattern.minConfidence);
      // Merge trained definition (preserves user-typed identity fields).
      if (r.definition) {
        setDraft((cur) => ({ ...r.definition!, id: cur.id, name: cur.name, group: cur.group, output: cur.output }));
        // Run TEST per sample with the candidate model to populate the live preview overlays.
        if (r.model) {
          const previews: Record<string, DetectionResult> = {};
          for (const s of samples) {
            try {
              const tr = await detectionService.test(profileId, r.definition, s.imageBase64, r.model);
              previews[s.id] = tr;
            } catch { /* skip — diagnostic still shows label/predicted */ }
          }
          setSamplePredictions(previews);
        }
      }
    } catch (e) { message.error(String(e)); }
    finally { setTraining(false); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profileId, samples, kind, draft]);

  const reTest = useCallback(async () => {
    if (!profileId || !trainingResult?.definition || !trainingResult.model) return;
    const tuned: DetectionDefinition = JSON.parse(JSON.stringify(trainingResult.definition));
    if (tuned.bar) {
      if (tuneTolerance !== undefined) tuned.bar.tolerance = tuneTolerance;
      if (tuneLineThreshold !== undefined) tuned.bar.lineThreshold = tuneLineThreshold;
    }
    if (tuned.pattern && tuneMinConfidence !== undefined) tuned.pattern.minConfidence = tuneMinConfidence;

    const previews: Record<string, DetectionResult> = {};
    const newDiagnostics = trainingResult.diagnostics.slice();
    for (let i = 0; i < samples.length; i++) {
      const s = samples[i];
      try {
        const r = await detectionService.test(profileId, tuned, s.imageBase64, trainingResult.model);
        previews[s.id] = r;
        const predicted = r.kind === 'bar' ? (r.value ?? 0).toFixed(3)
          : r.kind === 'pattern' ? `${r.found ? 'true' : 'false'} (${(r.confidence ?? 0).toFixed(2)})`
          : r.kind === 'text' ? (r.text ?? '')
          : (r.found ? 'true' : 'false');
        const labelNum = parseFloat(s.label);
        const predNum = parseFloat(predicted);
        const err = !isNaN(labelNum) && !isNaN(predNum) ? Math.abs(labelNum - predNum) : (predicted.startsWith(s.label.trim().toLowerCase()) ? 0 : 1);
        newDiagnostics[i] = {
          ...newDiagnostics[i],
          label: s.label,
          predicted,
          error: err,
          predictedBox: r.match ? { x: r.match.x, y: r.match.y, w: r.match.w, h: r.match.h } : undefined,
          iou: newDiagnostics[i]?.iou ?? 0,
        };
      } catch { /* keep prior diagnostic */ }
    }
    setSamplePredictions(previews);
    setTrainingResult({ ...trainingResult, definition: tuned, diagnostics: newDiagnostics });
    setDraft((cur) => ({ ...tuned, id: cur.id, name: cur.name, group: cur.group, output: cur.output }));
  }, [profileId, samples, trainingResult, tuneTolerance, tuneLineThreshold, tuneMinConfidence]);

  // ---------- step 6: save (def + model + samples) ----------

  const onSave = async () => {
    if (!profileId || !draft.name.trim()) return;
    setSaving(true);
    try {
      // For one-shot kinds (tracker / text / composite) we may not have run training yet — do it inline.
      // Composite uses no samples; trainer reads operand ids from seed.composite.
      let result = trainingResult;
      if (!result) {
        if (kind === 'tracker' || kind === 'text') {
          result = await detectionService.train(profileId, kind, buildTrainingSamples(), draft);
        } else if (kind === 'composite') {
          result = await detectionService.train(profileId, kind, [], draft);
        } else {
          throw new Error(t('detection.train.nextBlock.train', 'Run training first to produce a model.') as string);
        }
      }
      if (!result.definition || !result.model) {
        throw new Error('Trainer returned no model — check sample annotations.');
      }
      const toSave: DetectionDefinition = {
        ...result.definition,
        name: draft.name,
        group: draft.group,
        output: draft.output,
      };
      const savedDef = await detectionService.save(profileId, toSave);
      // Model id mirrors detection id (assigned by SAVE if blank).
      const modelToSave: DetectionModel = { ...result.model, id: savedDef.id, detectionId: savedDef.id };
      await detectionService.saveModel(profileId, modelToSave);

      // Persist labeled samples for later re-training. Composite has no samples.
      if (kind !== 'composite' && samples.length > 0) {
        try {
          await detectionService.saveSamples(profileId, savedDef.id,
            samples.map((s) => ({
              id: s.id,
              imageBase64: s.imageBase64,
              label: s.label,
              objectBox: s.objectBox,
              isInit: s.isInit,
            })),
            true);
        } catch (e) {
          console.warn('Failed to persist training samples', e);
          message.warning(t('detection.train.samplesNotSaved', 'Detection saved, but training samples failed to persist.'));
        }
      }
      message.success(t('detection.train.saved', 'Detection saved.'));
      onSaved({ ...savedDef, hasModel: true });
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
    annotate: t('detection.train.step.annotate', '3. Annotate'),
    search: t('detection.train.step.search', 'Search ROI'),
    train: t('detection.train.step.train', 'Train & Tune'),
    save: t('detection.train.step.save', 'Save'),
    compose: t('detection.train.step.compose', 'Compose'),
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
                  <div className="training-kind-card__desc">{t(`detection.train.kindDesc.${k.kind}`, k.desc)}</div>
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
          const recordingMeta = recordings.find((r) => r.id === pickedRecordingId);
          const recordingMax = recordingMeta ? Math.max(0, recordingMeta.frameCount - 1) : 0;
          const samplesToolbar = (
            <>
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
                <WindowSelector value={windowHandle} onChange={setWindowHandle} minWidth={240} />
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
                    <span className="training-record-bar__label">{t('detection.train.duration', 'Duration')}:</span>
                    <CompactSelect
                      size="small"
                      value={recordDurationS}
                      onChange={(v) => setRecordDurationS(v as number)}
                      options={[5, 10, 30, 60].map((n) => ({ value: n, label: `${n}s` }))}
                      style={{ width: 80 }}
                    />
                    <span className="training-record-bar__label">{t('detection.train.interval', 'Interval')}:</span>
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
                        <span className="training-range__readout">{frameRangeStart}–{frameRangeEnd}</span>
                        <CompactInput
                          size="small"
                          value={frameStride}
                          onChange={(e) => setFrameStride(Math.max(1, parseInt(e.target.value, 10) || 1))}
                          style={{ width: 64 }}
                          addonAfter={t('detection.train.strideUnit', 'th') as string}
                        />
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

          const reviewSamples = samples.map((s) => ({
            ...s,
            hasBox: hasValidBox(s.objectBox),
          }));

          return (
            <div className="training-step training-step--wide">
              <SamplesReviewPane
                kind={kind}
                samples={reviewSamples}
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

        {step === 'annotate' && (() => {
          const sample = samples[selected];
          const reviewSamples = samples.map((s) => ({ ...s, hasBox: hasValidBox(s.objectBox) }));

          // Sample picker on the left, big annotation canvas on the right.
          return (
            <div className="training-step training-step--wide">
              <div className="samples-review">
                <div className="samples-review__left">
                  <div className="samples-review__strip">
                    <div className="samples-review__strip-header">
                      <span className="samples-review__count">
                        {t('detection.train.annotate.boxedCount', '{{n}} of {{total}} samples annotated', { n: annotatedCount, total: samples.length })}
                      </span>
                    </div>
                    <div className="samples-review__strip-body">
                      {reviewSamples.map((s, i) => (
                        <div
                          key={s.id}
                          className={classNames('samples-review__row', {
                            'samples-review__row--active': i === selected,
                          })}
                          onClick={() => setSelected(i)}
                        >
                          <img className="samples-review__thumb" src={`data:image/png;base64,${s.imageBase64}`} alt="" />
                          <div className="samples-review__row-info">
                            <div className="samples-review__row-name">
                              #{i + 1}
                              {s.isInit && <span className="samples-review__row-badge samples-review__row-badge--init" title="Init frame" />}
                              {s.hasBox && !s.isInit && <span className="samples-review__row-badge" title="Annotated" />}
                            </div>
                            <div className="samples-review__row-label">{s.label || '—'}</div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                <div className="samples-review__preview">
                  {sample ? (
                    <AnnotationCanvas
                      kind={kind}
                      imageBase64={sample.imageBase64}
                      width={sample.width}
                      height={sample.height}
                      box={sample.objectBox}
                      isInit={!!sample.isInit}
                      previousBox={selected > 0 ? samples[selected - 1]?.objectBox : undefined}
                      annotatedCount={annotatedCount}
                      totalCount={samples.length}
                      onChange={(box) => setBoxOn(selected, box)}
                      onSetIsInit={(init) => setIsInitOn(selected, init)}
                      onCopyFromPrev={selected > 0 ? () => copyBoxFromPrev(selected) : undefined}
                      onApplyToAll={kind !== 'tracker' ? () => applyBoxToAll(selected) : undefined}
                      onClearAll={annotatedCount > 0 ? clearAllBoxes : undefined}
                    />
                  ) : (
                    <div className="samples-review__empty">
                      {t('detection.train.noSample', 'Capture a sample first.')}
                    </div>
                  )}
                </div>
              </div>
            </div>
          );
        })()}

        {step === 'compose' && (() => {
          const comp = draft.composite ?? { op: 'and' as CompositeOp, detectionIds: [] };
          const setComposite = (patch: Partial<typeof comp>) => setDraft({ ...draft, composite: { ...comp, ...patch } });
          const operandOptions = allDetections
            .filter((d) => d.id && d.id !== draft.id && d.kind !== 'composite')
            .map((d) => ({ value: d.id, label: `${d.name || d.id} (${d.kind})` }));
          return (
            <div className="training-step">
              <div className="training-step__title">{t('detection.train.compose.title', 'Compose other detections')}</div>
              <div className="training-step__hint">{t('detection.train.compose.hint', 'Pick the operand detections and choose AND (all must match) or OR (any matches).')}</div>
              <div>
                <span style={{ fontSize: 12 }}>{t('detection.train.compose.op', 'Operator')}</span>
                <CompactSegmented
                  value={comp.op}
                  onChange={(v) => setComposite({ op: v as CompositeOp })}
                  options={[
                    { value: 'and', label: t('detection.train.compose.opAnd', 'AND — all must match') },
                    { value: 'or', label: t('detection.train.compose.opOr', 'OR — any one matches') },
                  ]}
                />
              </div>
              <div>
                <span style={{ fontSize: 12 }}>{t('detection.train.compose.operands', 'Operands')}</span>
                <CompactSelect
                  mode="multiple"
                  value={comp.detectionIds}
                  onChange={(v) => setComposite({ detectionIds: v as string[] })}
                  options={operandOptions}
                  placeholder={t('detection.train.compose.needsOperands', 'Add at least one operand detection.') as string}
                  style={{ width: '100%' }}
                />
              </div>
            </div>
          );
        })()}

        {step === 'search' && (
          <div className="training-step">
            <div className="training-step__title">{t('detection.train.search.title', 'Where should the runner look at runtime?')}</div>
            <div className="training-step__hint">
              {t('detection.train.search.hint', 'Optional: cap the runtime search area. Default = whole frame.')}
            </div>
            <div className="training-samples-grid">
              <div>
                <CompactSpace>
                  <CompactButton size="small" loading={suggestBusy} icon={<ThunderboltOutlined />} onClick={() => void onSuggestRois()}>
                    {t('detection.train.suggestRois', 'Suggest dynamic regions')}
                  </CompactButton>
                  <CompactButton size="small" onClick={() => setDraft({ ...draft, roi: undefined })}>
                    {t('detection.train.search.useFull', 'Use whole frame')}
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
                {samples[selected] ? (
                  <canvas
                    ref={searchCanvasRef}
                    className="training-canvas"
                    onMouseDown={onSearchMouseDown}
                    onMouseMove={onSearchMouseMove}
                    onMouseUp={onSearchMouseUp}
                    onMouseLeave={onSearchMouseUp}
                  />
                ) : (
                  <div style={{ color: 'var(--color-text-tertiary)' }}>
                    {t('detection.train.noSample', 'Capture a sample first.')}
                  </div>
                )}
              </div>
            </div>
            {draft.roi ? (
              <div className="training-summary">
                {t('detection.train.search.crop', 'Drag rectangle to crop')}: ({draft.roi.x}, {draft.roi.y}) {draft.roi.w}×{draft.roi.h}
              </div>
            ) : (
              <div className="training-summary">{t('detection.train.search.useFull', 'Use whole frame')}</div>
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

            {trainingResult?.model && (
              <div className="training-model-summary">
                <div className="training-model-summary__title">
                  {t('detection.train.model.summary', 'Trained model')}
                </div>
                <div className="training-model-summary__line">{trainingResult.model.summary}</div>
                <div className="training-model-summary__line">
                  {t('detection.train.model.samples', '{{n}} samples ({{pos}} pos / {{neg}} neg)',
                    { n: trainingResult.model.sampleCount, pos: trainingResult.model.positiveCount, neg: trainingResult.model.negativeCount })}
                  {trainingResult.model.meanIoU > 0 && ` · ${t('detection.train.model.iou', 'mean IoU {{value}}', { value: trainingResult.model.meanIoU.toFixed(2) })}`}
                  {` · ${t('detection.train.model.error', 'mean error {{value}}', { value: trainingResult.model.meanError.toFixed(3) })}`}
                </div>
              </div>
            )}

            {trainingResult && (
              <>
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
              {t('detection.train.save.hint', 'Saving writes the definition + the trained model + your training samples (for re-training later).')}
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
            {trainingResult?.model && (
              <div className="training-model-summary">
                <div className="training-model-summary__title">
                  {t('detection.train.model.summary', 'Trained model')}
                </div>
                <div className="training-model-summary__line">{trainingResult.model.summary}</div>
              </div>
            )}
            <div>
              <span style={{ fontSize: 12, color: 'var(--color-text-secondary)' }}>
                {t('detection.train.scriptUsage', 'Use it in a script:')}
              </span>
              <pre className="detection-script-hint">{`const r = detect.run('${draft.name || 'detection-name'}');\n// r.value, r.found, r.match, r.confidence`}</pre>
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

// ---------- helpers ----------

function hasValidBox(box: DetectionRoi | undefined): boolean {
  return !!box && box.w > 0 && box.h > 0;
}

function isPositiveLabel(label: string): boolean {
  const l = label.trim().toLowerCase();
  return l === 'true' || l === 'yes' || l === '1' || l === '+' || l === 'positive';
}
