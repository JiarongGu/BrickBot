import React, { useCallback, useEffect, useState } from 'react';
import { Popconfirm, Tooltip, message } from 'antd';
import {
  DeleteOutlined,
  EditOutlined,
  PlayCircleOutlined,
  PlusOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactPrimaryButton,
  CompactSpace,
} from '@/shared/components/compact';
import { SlideInScreen } from '@/shared/components/common';
import { useProfileStore } from '@/modules/profile';
import { recordingService } from '../services/recordingService';
import type { RecordingInfo } from '../types';
import { RecordRecordingDialog } from './RecordRecordingDialog';
import { RecordingPlayer } from './RecordingPlayer';
import './RecordingsView.css';

/**
 * Top-level Recordings tab. A recording is a named multi-frame capture authored once and
 * reused across many detection trainings. List + record + play back + delete.
 */
export const RecordingsView: React.FC = () => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [recordings, setRecordings] = useState<RecordingInfo[]>([]);
  const [recordOpen, setRecordOpen] = useState(false);
  const [playing, setPlaying] = useState<RecordingInfo | undefined>();

  const refresh = useCallback(async () => {
    if (!profileId) return;
    const r = await recordingService.list(profileId);
    setRecordings(r.recordings);
  }, [profileId]);

  useEffect(() => { void refresh(); }, [refresh]);

  const onRecordSaved = (rec: RecordingInfo) => {
    setRecordOpen(false);
    void refresh();
    setPlaying(rec);
  };

  const onDelete = async (id: string) => {
    if (!profileId) return;
    try {
      await recordingService.delete(profileId, id);
      setRecordings((rs) => rs.filter((r) => r.id !== id));
      if (playing?.id === id) setPlaying(undefined);
      message.success(t('recording.deleted', 'Recording deleted.'));
    } catch (e) { message.error(String(e)); }
  };

  if (!profileId) {
    return (
      <div className="recordings-view">
        <CompactAlert type="info" message={t('recording.selectProfile', 'Select a profile to manage recordings.')} />
      </div>
    );
  }

  return (
    <div className="recordings-view">
      <div className="recordings-view__header">
        <div className="recordings-view__title">
          <VideoCameraOutlined /> {t('recording.title', 'Recordings')} <span className="recordings-view__count">({recordings.length})</span>
        </div>
        <CompactPrimaryButton icon={<PlusOutlined />} onClick={() => setRecordOpen(true)}>
          {t('recording.new', 'New recording')}
        </CompactPrimaryButton>
      </div>

      <div className="recordings-view__body">
        {recordings.length === 0 ? (
          <div className="recordings-view__empty">
            <VideoCameraOutlined className="recordings-view__empty-icon" />
            <div className="recordings-view__empty-title">{t('recording.empty', 'No recordings yet.')}</div>
            <div className="recordings-view__empty-hint">
              {t('recording.emptyHint', 'Record gameplay once, then reuse the same frames across multiple detection trainings — different ROIs, different labels, no need to capture each detection separately.')}
            </div>
            <CompactPrimaryButton size="large" icon={<PlusOutlined />} onClick={() => setRecordOpen(true)}>
              {t('recording.new', 'New recording')}
            </CompactPrimaryButton>
          </div>
        ) : (
          <div className="recording-grid">
            {recordings.map((rec) => (
              <div key={rec.id} className="recording-card" onClick={() => setPlaying(rec)}>
                <div className="recording-card__header">
                  <span className="recording-card__name">{rec.name}</span>
                  <CompactSpace size={2}>
                    <Tooltip title={t('recording.play', 'Play')}>
                      <CompactButton size="small" type="text" icon={<PlayCircleOutlined />}
                        onClick={(e) => { e.stopPropagation(); setPlaying(rec); }} />
                    </Tooltip>
                    <Popconfirm
                      title={t('recording.deleteConfirm', 'Delete this recording?')}
                      okText={t('common.delete')}
                      cancelText={t('common.cancel')}
                      okButtonProps={{ danger: true }}
                      onConfirm={(e) => { e?.stopPropagation(); void onDelete(rec.id); }}
                      onCancel={(e) => e?.stopPropagation()}
                    >
                      <CompactDangerButton size="small" type="text" icon={<DeleteOutlined />}
                        onClick={(e) => e.stopPropagation()} />
                    </Popconfirm>
                  </CompactSpace>
                </div>
                <div className="recording-card__meta">
                  <span>{rec.frameCount} {t('recording.frames', 'frames')}</span>
                  <span>·</span>
                  <span>{rec.width}×{rec.height}</span>
                  {rec.intervalMs > 0 && <><span>·</span><span>every {rec.intervalMs}ms</span></>}
                  {rec.windowTitle && <><span>·</span><span>{rec.windowTitle}</span></>}
                </div>
                {rec.description && (
                  <div style={{ fontSize: 12, color: 'var(--color-text-tertiary)', marginTop: 4 }}>{rec.description}</div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      <RecordRecordingDialog
        open={recordOpen}
        onCancel={() => setRecordOpen(false)}
        onSaved={onRecordSaved}
      />

      <SlideInScreen
        open={!!playing}
        title={playing?.name ?? ''}
        bodyClassName="detections-view__slide-body"
        onClose={() => setPlaying(undefined)}
      >
        {playing && <RecordingPlayer recording={playing} />}
      </SlideInScreen>
    </div>
  );
};
