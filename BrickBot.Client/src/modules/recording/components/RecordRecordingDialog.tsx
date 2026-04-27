import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Slider, message } from 'antd';
import {
  CaretRightOutlined,
  CheckCircleOutlined,
  PauseOutlined,
  PlayCircleOutlined,
  StepBackwardOutlined,
  StepForwardOutlined,
  StopOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactButton,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
} from '@/shared/components/compact';
import { SlideInScreen, WindowSelector } from '@/shared/components/common';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import { recordingService } from '../services/recordingService';
import type { RecordingInfo } from '../types';
import './RecordRecordingDialog.css';

interface Props {
  open: boolean;
  onCancel: () => void;
  onSaved: (recording: RecordingInfo) => void;
}

interface FrameRow {
  imageBase64: string;
  capturedAt: number;
}

type Step = 'configure' | 'record' | 'review' | 'save';

const STEPS: { key: Step; titleKey: string; titleFallback: string }[] = [
  { key: 'configure', titleKey: 'recording.dialog.step.configure', titleFallback: 'Configure' },
  { key: 'record', titleKey: 'recording.dialog.step.record', titleFallback: 'Record' },
  { key: 'review', titleKey: 'recording.dialog.step.review', titleFallback: 'Review' },
  { key: 'save', titleKey: 'recording.dialog.step.save', titleFallback: 'Save' },
];

/**
 * Slide-in screen with 4-step wizard for capturing a new recording.
 *   1. Configure — pick window + duration/interval.
 *   2. Record    — start/stop the capture loop with a live preview.
 *   3. Review    — scrub through captured frames before committing.
 *   4. Save      — name + description; commit via CREATE.
 *
 * Frames stay client-side in memory until step 4. Backend persists everything atomically.
 */
export const RecordRecordingDialog: React.FC<Props> = ({ open, onCancel, onSaved }) => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [step, setStep] = useState<Step>('configure');
  const [windowInfo, setWindowInfo] = useState<WindowInfo | undefined>();
  const windowHandle = windowInfo?.handle;
  const [intervalMs, setIntervalMs] = useState(500);
  const [durationS, setDurationS] = useState(15);
  const [recording, setRecording] = useState(false);
  const [frames, setFrames] = useState<FrameRow[]>([]);
  const [progress, setProgress] = useState(0);
  const [reviewIndex, setReviewIndex] = useState(0);
  const [reviewPlaying, setReviewPlaying] = useState(false);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [saving, setSaving] = useState(false);
  const [previewPng, setPreviewPng] = useState<string | undefined>();
  const [previewLoading, setPreviewLoading] = useState(false);

  const captureTimerRef = useRef<number | null>(null);
  const progressTimerRef = useRef<number | null>(null);
  const reviewTimerRef = useRef<number | null>(null);
  const startedAtRef = useRef(0);
  const endsAtRef = useRef(0);

  const stopRecording = useCallback(() => {
    if (captureTimerRef.current) { clearInterval(captureTimerRef.current); captureTimerRef.current = null; }
    if (progressTimerRef.current) { clearInterval(progressTimerRef.current); progressTimerRef.current = null; }
    setRecording(false);
  }, []);

  // Reset everything when the screen closes.
  useEffect(() => {
    if (!open) {
      stopRecording();
      setStep('configure');
      setFrames([]);
      setName('');
      setDescription('');
      setProgress(0);
      setReviewIndex(0);
      setReviewPlaying(false);
      setPreviewPng(undefined);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // When recording finishes naturally, advance the user to Review.
  useEffect(() => {
    if (!recording && step === 'record' && frames.length > 0) {
      setStep('review');
      setReviewIndex(0);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [recording]);

  // Snap one frame whenever the user picks a different window so the Configure step has a
  // visible preview to verify against. Skipped while a recording is active (capture pipeline
  // is busy) and when the dialog is closed.
  useEffect(() => {
    if (!open || !windowHandle || recording) return;
    let cancelled = false;
    setPreviewLoading(true);
    void (async () => {
      try {
        const r = await captureService.grabPng(windowHandle);
        if (!cancelled) setPreviewPng(r.pngBase64);
      } catch { if (!cancelled) setPreviewPng(undefined); }
      finally { if (!cancelled) setPreviewLoading(false); }
    })();
    return () => { cancelled = true; };
  }, [open, windowHandle, recording]);

  const startRecording = () => {
    if (!windowHandle) { message.warning(t('recording.pickWindow', 'Pick a window first.')); return; }
    setFrames([]);
    setProgress(0);
    startedAtRef.current = Date.now();
    endsAtRef.current = startedAtRef.current + durationS * 1000;
    setRecording(true);

    const tick = async () => {
      if (Date.now() >= endsAtRef.current) { stopRecording(); return; }
      try {
        const r = await captureService.grabPng(windowHandle);
        setFrames((prev) => [...prev, { imageBase64: r.pngBase64, capturedAt: Date.now() }]);
      } catch { /* keep going — transient capture failures shouldn't kill the recording */ }
    };
    void tick();
    captureTimerRef.current = window.setInterval(() => { void tick(); }, intervalMs);
    progressTimerRef.current = window.setInterval(() => {
      const total = durationS * 1000;
      const elapsed = Date.now() - startedAtRef.current;
      setProgress(Math.max(0, Math.min(1, elapsed / total)));
    }, 100);
  };

  // Review playback loop — drives reviewIndex when reviewPlaying is true.
  useEffect(() => {
    if (!reviewPlaying || frames.length === 0) {
      if (reviewTimerRef.current) { clearInterval(reviewTimerRef.current); reviewTimerRef.current = null; }
      return;
    }
    reviewTimerRef.current = window.setInterval(() => {
      setReviewIndex((i) => {
        const next = i + 1;
        if (next >= frames.length) { setReviewPlaying(false); return i; }
        return next;
      });
    }, intervalMs > 0 ? intervalMs : 100);
    return () => { if (reviewTimerRef.current) clearInterval(reviewTimerRef.current); };
  }, [reviewPlaying, frames.length, intervalMs]);

  const onSave = async () => {
    if (!profileId || frames.length === 0) return;
    if (!name.trim()) { message.warning(t('recording.nameRequired', 'Give the recording a name.')); return; }
    setSaving(true);
    try {
      const saved = await recordingService.create(profileId, {
        name: name.trim(),
        description: description.trim() || undefined,
        windowTitle: windowInfo?.title,
        intervalMs,
        frames: frames.map((f) => ({ imageBase64: f.imageBase64, capturedAt: new Date(f.capturedAt).toISOString() })),
      });
      message.success(t('recording.saved', 'Recording saved.'));
      onSaved(saved);
    } catch (e) { message.error(String(e)); }
    finally { setSaving(false); }
  };

  // ---- step gating ----
  const canAdvance = (() => {
    switch (step) {
      case 'configure': return !!windowHandle;
      case 'record': return frames.length > 0 && !recording;
      case 'review': return frames.length > 0;
      case 'save': return !!name.trim() && !saving && !recording;
    }
  })();

  const stepIndex = STEPS.findIndex((s) => s.key === step);
  const goNext = () => {
    if (step === 'save') { void onSave(); return; }
    const next = STEPS[stepIndex + 1];
    if (next) setStep(next.key);
  };
  const goBack = () => {
    const prev = STEPS[stepIndex - 1];
    if (prev) setStep(prev.key);
  };

  const expectedFrames = Math.max(1, Math.round((durationS * 1000) / intervalMs));
  const liveLastFrame = frames.length > 0 ? frames[frames.length - 1] : null;
  const reviewFrame = frames[Math.min(reviewIndex, Math.max(0, frames.length - 1))];

  return (
    <SlideInScreen
      open={open}
      onClose={onCancel}
      bodyClassName="record-dialog__body"
      title={
        <span className="record-dialog__title">
          <VideoCameraOutlined />
          <span>{t('recording.dialog.title', 'New recording')}</span>
        </span>
      }
    >
      <div className="record-dialog">
        {/* Stepper header — mirrors the TrainingPanel pill design (tiny pill + › separator). */}
        <div className="record-dialog__stepper">
          {STEPS.map((s, idx) => {
            const isActive = s.key === step;
            const isDone = idx < stepIndex;
            return (
              <React.Fragment key={s.key}>
                {idx > 0 && <span className="record-dialog__step-arrow">›</span>}
                <span
                  className={classNames('record-dialog__step', {
                    'record-dialog__step--active': isActive,
                    'record-dialog__step--done': isDone,
                    'record-dialog__step--disabled': recording,
                  })}
                  onClick={() => {
                    if (recording) return;
                    if (idx <= stepIndex) setStep(s.key);
                  }}
                >
                  {isDone && <CheckCircleOutlined />}
                  {t(s.titleKey, s.titleFallback)}
                </span>
              </React.Fragment>
            );
          })}
        </div>

        {/* Content area — one step rendered at a time */}
        <div className="record-dialog__content">
          {step === 'configure' && (
            <div className="record-dialog__split">
              <div className="record-dialog__split-left">
                <div className="record-dialog__field">
                  <span className="record-dialog__field-label">{t('recording.dialog.window', 'Window')}</span>
                  <WindowSelector
                    value={windowHandle}
                    onChange={(_, info) => setWindowInfo(info)}
                    minWidth={0}
                    style={{ width: '100%' }}
                  />
                </div>
                <label className="record-dialog__field">
                  <span className="record-dialog__field-label">{t('recording.duration', 'Duration')}</span>
                  <CompactInput
                    value={durationS}
                    onChange={(e) => {
                      const v = parseInt(e.target.value.replace(/[^\d]/g, ''), 10);
                      setDurationS(Number.isFinite(v) ? Math.max(1, Math.min(3600, v)) : 1);
                    }}
                    addonAfter="s"
                  />
                </label>
                <label className="record-dialog__field">
                  <span className="record-dialog__field-label">{t('recording.interval', 'Interval')}</span>
                  <CompactInput
                    value={intervalMs}
                    onChange={(e) => {
                      const v = parseInt(e.target.value.replace(/[^\d]/g, ''), 10);
                      setIntervalMs(Number.isFinite(v) ? Math.max(50, Math.min(10000, v)) : 50);
                    }}
                    addonAfter="ms"
                  />
                </label>
                <div className="record-dialog__field">
                  <span className="record-dialog__field-label">{t('recording.expectedFrames', 'Expected frames')}</span>
                  <span className="record-dialog__field-readout">{expectedFrames}</span>
                </div>
              </div>

              <div className="record-dialog__split-right">
                <div className="record-dialog__preview-large">
                  {previewPng ? (
                    <img src={`data:image/png;base64,${previewPng}`} alt="" />
                  ) : (
                    <div className="record-dialog__preview-empty">
                      {previewLoading
                        ? t('recording.dialog.previewLoading', 'Capturing preview…')
                        : windowHandle
                          ? t('recording.dialog.previewFailed', 'Could not capture a preview frame.')
                          : t('recording.dialog.previewPickWindow', 'Pick a window to see a live preview.')}
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {step === 'record' && (
            <div className="record-dialog__step-panel">
              <div className="record-dialog__record-toolbar">
                <div className="record-dialog__record-status">
                  <span className="record-dialog__frame-count">
                    {t('recording.framesCaptured', '{{n}} frames captured', { n: frames.length })}
                  </span>
                  {recording && (
                    <span className="record-dialog__live-badge">
                      <span className="record-dialog__live-dot" />
                      {t('recording.recordingNow', 'recording...')}
                    </span>
                  )}
                </div>
                {!recording ? (
                  <CompactPrimaryButton
                    icon={<PlayCircleOutlined />}
                    onClick={startRecording}
                    disabled={!windowHandle}
                  >
                    {frames.length > 0
                      ? t('recording.startAgain', 'Re-record')
                      : t('recording.start', 'Record')}
                  </CompactPrimaryButton>
                ) : (
                  <CompactDangerButton icon={<StopOutlined />} onClick={stopRecording}>
                    {t('recording.stop', 'Stop ({{pct}}%)', { pct: Math.round(progress * 100) })}
                  </CompactDangerButton>
                )}
              </div>

              <div className="record-dialog__progress">
                <div
                  className="record-dialog__progress-fill"
                  style={{
                    width: `${(progress * 100).toFixed(1)}%`,
                    background: recording ? 'var(--color-primary)' : 'var(--color-success)',
                  }}
                />
              </div>

              <div className="record-dialog__preview-large">
                {liveLastFrame ? (
                  <img src={`data:image/png;base64,${liveLastFrame.imageBase64}`} alt="" />
                ) : (
                  <div className="record-dialog__preview-empty">
                    {recording
                      ? t('recording.dialog.waitingFirstFrame', 'Waiting for first frame…')
                      : t('recording.dialog.recordHint', 'Click Record to start capturing frames from the selected window.')}
                  </div>
                )}
              </div>
            </div>
          )}

          {step === 'review' && (
            <div className="record-dialog__step-panel">
              <div className="record-dialog__review-toolbar">
                <CompactButton
                  size="small"
                  icon={<StepBackwardOutlined />}
                  disabled={reviewIndex === 0}
                  onClick={() => setReviewIndex(Math.max(0, reviewIndex - 1))}
                />
                <CompactButton
                  size="small"
                  icon={reviewPlaying ? <PauseOutlined /> : <CaretRightOutlined />}
                  onClick={() => setReviewPlaying((p) => !p)}
                />
                <CompactButton
                  size="small"
                  icon={<StepForwardOutlined />}
                  disabled={reviewIndex >= frames.length - 1}
                  onClick={() => setReviewIndex(Math.min(frames.length - 1, reviewIndex + 1))}
                />
                <Slider
                  min={0}
                  max={Math.max(0, frames.length - 1)}
                  value={reviewIndex}
                  onChange={(v) => setReviewIndex(v as number)}
                  style={{ flex: 1, margin: '0 8px' }}
                />
                <span className="record-dialog__frame-info">
                  {t('recording.player.frame', 'frame {{i}} / {{n}}', { i: reviewIndex + 1, n: frames.length })}
                </span>
              </div>

              <div className="record-dialog__preview-large">
                {reviewFrame && (
                  <img src={`data:image/png;base64,${reviewFrame.imageBase64}`} alt="" />
                )}
              </div>
            </div>
          )}

          {step === 'save' && (
            <div className="record-dialog__step-panel">
              <div className="record-dialog__hint">
                {t('recording.dialog.saveHint', 'Give your recording a name so you can find it later when training detections.')}
              </div>
              <label className="record-dialog__field">
                <span className="record-dialog__field-label">{t('recording.dialog.nameField', 'Name')}</span>
                <CompactInput
                  placeholder={t('recording.namePlaceholder', 'e.g. fishing-session-1') as string}
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                />
              </label>
              <label className="record-dialog__field">
                <span className="record-dialog__field-label">
                  {t('recording.dialog.descriptionField', 'Description')}
                  <span className="record-dialog__field-optional"> · {t('common.optional', 'optional')}</span>
                </span>
                <CompactInput
                  placeholder={t('recording.descriptionPlaceholder', 'Notes about this recording') as string}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                />
              </label>
              <div className="record-dialog__summary">
                <div><strong>{frames.length}</strong> {t('recording.dialog.framesLabel', 'frames')}</div>
                {windowInfo?.title && <div className="record-dialog__summary-meta">{windowInfo.title}</div>}
                <div className="record-dialog__summary-meta">
                  {durationS}s · {intervalMs}ms
                </div>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="record-dialog__footer">
          <CompactButton
            onClick={stepIndex === 0 ? onCancel : goBack}
            disabled={recording || saving}
          >
            {stepIndex === 0
              ? t('common.cancel')
              : t('common.back', 'Back')}
          </CompactButton>
          <CompactPrimaryButton
            onClick={goNext}
            disabled={!canAdvance}
            loading={step === 'save' && saving}
          >
            {step === 'save'
              ? t('recording.dialog.save', 'Save recording')
              : t('common.next', 'Next')}
          </CompactPrimaryButton>
        </div>
      </div>
    </SlideInScreen>
  );
};
