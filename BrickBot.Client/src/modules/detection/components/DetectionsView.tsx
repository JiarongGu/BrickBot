import React, { useCallback, useEffect, useState } from 'react';
import { Popconfirm, Tooltip, message } from 'antd';
import {
  AimOutlined,
  DeleteOutlined,
  EditOutlined,
  ExperimentOutlined,
  PlusOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSpace,
} from '@/shared/components/compact';
import { SlideInScreen } from '@/shared/components/common';
import { useProfileStore } from '@/modules/profile';
import { detectionService } from '../services/detectionService';
import type { DetectionDefinition, DetectionKind } from '../types';
import { DETECTION_KIND_LABEL, newDetection } from '../types';
import { useDetectionStore } from '../store/detectionStore';
import { TrainingPanel } from './TrainingPanel';
import { DetectionsPanel } from './DetectionsPanel';
import './DetectionsView.css';

/**
 * Top-level Detections tab. Two main entry points:
 *   - "Train new" → opens TrainingPanel wizard (training-first flow, recommended)
 *   - "Manual" / Edit → opens the form-based DetectionsPanel for fine-tuning existing rules
 *
 * The list shows all per-profile detections grouped by kind, with quick edit / delete /
 * search filtering. Selecting a detection opens the manual editor in a slide-in panel.
 */
export const DetectionsView: React.FC = () => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);
  const detections = useDetectionStore((s) => s.detections);
  const setDetections = useDetectionStore((s) => s.setDetections);
  const removeFromStore = useDetectionStore((s) => s.remove);
  const upsert = useDetectionStore((s) => s.upsert);
  const setDraft = useDetectionStore((s) => s.setDraft);

  const [filter, setFilter] = useState('');
  const [kindFilter, setKindFilter] = useState<DetectionKind | 'all'>('all');
  const [trainingOpen, setTrainingOpen] = useState(false);
  const [editorOpen, setEditorOpen] = useState(false);
  const [reTrainTarget, setReTrainTarget] = useState<DetectionDefinition | undefined>();

  const refresh = useCallback(async () => {
    if (!profileId) return;
    const { detections: defs } = await detectionService.list(profileId);
    setDetections(defs);
  }, [profileId, setDetections]);

  useEffect(() => { void refresh(); }, [refresh]);

  const onTrainNew = () => {
    setDraft(undefined);
    setReTrainTarget(undefined);
    setTrainingOpen(true);
  };

  const onReTrain = (def: DetectionDefinition) => {
    setReTrainTarget(def);
    setTrainingOpen(true);
  };

  const onAddManual = () => {
    setDraft(newDetection('pattern'));
    setEditorOpen(true);
  };

  const onEdit = (def: DetectionDefinition) => {
    setDraft(JSON.parse(JSON.stringify(def)));
    setEditorOpen(true);
  };

  const onDelete = async (def: DetectionDefinition) => {
    if (!profileId) return;
    try {
      await detectionService.delete(profileId, def.id);
      removeFromStore(def.id);
      message.success(t('detection.deleted', 'Deleted'));
    } catch (e) { message.error(String(e)); }
  };

  const onTrainingSaved = (saved: DetectionDefinition) => {
    upsert(saved);
    setTrainingOpen(false);
    void refresh();
  };

  const filtered = detections.filter((d) => {
    if (kindFilter !== 'all' && d.kind !== kindFilter) return false;
    if (filter && !d.name.toLowerCase().includes(filter.toLowerCase()) &&
        !(d.group ?? '').toLowerCase().includes(filter.toLowerCase())) return false;
    return true;
  });

  // Group by kind for display.
  const grouped = (Object.keys(DETECTION_KIND_LABEL) as DetectionKind[])
    .map((k) => ({ kind: k, items: filtered.filter((d) => d.kind === k) }))
    .filter((g) => g.items.length > 0);

  if (!profileId) {
    return (
      <div className="detections-view">
        <CompactAlert type="info" message={t('detection.selectProfile', 'Select a profile to manage detections.')} />
      </div>
    );
  }

  return (
    <div className="detections-view">
      <div className="detections-view__header">
        <div className="detections-view__title">
          <AimOutlined /> {t('detection.list.title', 'Detections')} <span className="detections-view__count">({detections.length})</span>
        </div>
        <CompactSpace>
          <CompactInput
            placeholder={t('detection.search', 'Search...') as string}
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            allowClear
            style={{ width: 200 }}
          />
          <CompactButton onClick={onAddManual} icon={<PlusOutlined />}>
            {t('detection.addManual', 'New manual')}
          </CompactButton>
          <CompactPrimaryButton onClick={onTrainNew} icon={<ExperimentOutlined />}>
            {t('detection.trainNew', 'Train new')}
          </CompactPrimaryButton>
        </CompactSpace>
      </div>

      <div className="detections-view__filters">
        <CompactButton
          size="small"
          type={kindFilter === 'all' ? 'primary' : 'text'}
          onClick={() => setKindFilter('all')}
        >
          {t('detection.filter.all', 'All')} ({detections.length})
        </CompactButton>
        {(Object.keys(DETECTION_KIND_LABEL) as DetectionKind[]).map((k) => {
          const n = detections.filter((d) => d.kind === k).length;
          if (n === 0) return null;
          return (
            <CompactButton
              key={k}
              size="small"
              type={kindFilter === k ? 'primary' : 'text'}
              onClick={() => setKindFilter(k)}
            >
              {t(`detection.kind.${k}`, DETECTION_KIND_LABEL[k])} ({n})
            </CompactButton>
          );
        })}
      </div>

      <div className="detections-view__body">
        {detections.length === 0 ? (
          <div className="detections-view__empty">
            <ThunderboltOutlined className="detections-view__empty-icon" />
            <div className="detections-view__empty-title">
              {t('detection.empty.firstTime', 'No detections yet.')}
            </div>
            <div className="detections-view__empty-hint">
              {t('detection.empty.firstTimeHint', 'Click "Train new" to walk through capturing samples and letting the system figure out the right config.')}
            </div>
            <CompactPrimaryButton size="large" icon={<ExperimentOutlined />} onClick={onTrainNew}>
              {t('detection.trainNew', 'Train new')}
            </CompactPrimaryButton>
          </div>
        ) : grouped.length === 0 ? (
          <div className="detections-view__empty-text">
            {t('detection.list.noMatches', 'No detections match the filter.')}
          </div>
        ) : (
          grouped.map((g) => (
            <CompactCard
              key={g.kind}
              extraCompact
              denseHeader
              title={`${t(`detection.kind.${g.kind}`, DETECTION_KIND_LABEL[g.kind])} (${g.items.length})`}
              style={{ marginBottom: 12 }}
            >
              <div className="detection-grid">
                {g.items.map((d) => (
                  <div
                    key={d.id}
                    className={classNames('detection-card', { 'detection-card--disabled': !d.enabled })}
                    onClick={() => onEdit(d)}
                  >
                    <div className="detection-card__header">
                      <span className="detection-card__name">{d.name}</span>
                      <span className={classNames('detection-trained-badge', { 'detection-trained-badge--untrained': !d.hasModel })}>
                        {d.hasModel
                          ? t('detection.trainedBadge', 'Trained')
                          : t('detection.untrainedBadge', 'Untrained')}
                      </span>
                      <CompactSpace size={2}>
                        <Tooltip title={t('detection.reTrain', 'Re-train from saved samples')}>
                          <CompactButton size="small" type="text" icon={<ExperimentOutlined />}
                            onClick={(e) => { e.stopPropagation(); onReTrain(d); }} />
                        </Tooltip>
                        <Tooltip title={t('detection.editor.title', 'Editor')}>
                          <CompactButton size="small" type="text" icon={<EditOutlined />}
                            onClick={(e) => { e.stopPropagation(); onEdit(d); }} />
                        </Tooltip>
                        <Popconfirm
                          title={t('detection.deleteConfirm', 'Delete this detection?')}
                          okText={t('common.delete')}
                          cancelText={t('common.cancel')}
                          okButtonProps={{ danger: true }}
                          onConfirm={(e) => { e?.stopPropagation(); void onDelete(d); }}
                          onCancel={(e) => e?.stopPropagation()}
                        >
                          <CompactDangerButton size="small" type="text" icon={<DeleteOutlined />}
                            onClick={(e) => e.stopPropagation()} />
                        </Popconfirm>
                      </CompactSpace>
                    </div>
                    <div className="detection-card__meta">
                      {d.group && <span className="detection-card__group">{d.group}</span>}
                      {d.output.ctxKey && <span className="detection-card__binding">ctx.{d.output.ctxKey}</span>}
                      {d.output.event && <span className="detection-card__binding">{d.output.event}</span>}
                    </div>
                  </div>
                ))}
              </div>
            </CompactCard>
          ))
        )}
      </div>

      {/* Training wizard (slide-in full screen) */}
      <SlideInScreen
        open={trainingOpen}
        title={reTrainTarget
          ? t('detection.reTrainTitle', 'Re-train detection: {{name}}', { name: reTrainTarget.name })
          : t('detection.train.title', 'Train detection')}
        bodyClassName="detections-view__slide-body"
        onClose={() => setTrainingOpen(false)}
      >
        {trainingOpen && (
          <TrainingPanel
            onCancel={() => setTrainingOpen(false)}
            onSaved={onTrainingSaved}
            reTrainDetection={reTrainTarget}
          />
        )}
      </SlideInScreen>

      {/* Manual editor (existing form-based panel) */}
      <SlideInScreen
        open={editorOpen}
        title={t('detection.editor.title', 'Detection Editor')}
        bodyClassName="detections-view__slide-body"
        onClose={() => setEditorOpen(false)}
      >
        {editorOpen && <DetectionsPanel />}
      </SlideInScreen>
    </div>
  );
};
