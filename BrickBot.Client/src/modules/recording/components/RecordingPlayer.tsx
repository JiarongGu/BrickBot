import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Slider } from 'antd';
import {
  CaretRightOutlined,
  PauseOutlined,
  StepBackwardOutlined,
  StepForwardOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { CompactButton, CompactSpace } from '@/shared/components/compact';
import { useProfileStore } from '@/modules/profile';
import { recordingService } from '../services/recordingService';
import type { RecordingInfo } from '../types';

interface Props {
  recording: RecordingInfo;
}

/**
 * Plays back a recording's frames with a scrubber. Frame images are loaded lazily on demand
 * — list call returns metadata only; this component fetches each frame's bytes via GET_FRAME
 * when scrubbed to. For the playback loop, frames are pre-fetched into a small ring buffer
 * to keep playback smooth without loading the entire recording into memory.
 */
export const RecordingPlayer: React.FC<Props> = ({ recording }) => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [frameIndex, setFrameIndex] = useState(0);
  const [imageSrc, setImageSrc] = useState<string | undefined>();
  const [playing, setPlaying] = useState(false);
  const playTimerRef = useRef<number | null>(null);
  const cacheRef = useRef<Map<number, string>>(new Map());

  const loadFrame = useCallback(async (idx: number): Promise<string | undefined> => {
    if (!profileId) return undefined;
    const cached = cacheRef.current.get(idx);
    if (cached) return cached;
    const r = await recordingService.getFrame(profileId, recording.id, idx);
    if (r?.imageBase64) {
      cacheRef.current.set(idx, r.imageBase64);
      // Keep cache bounded — drop oldest entries past 30 frames.
      if (cacheRef.current.size > 30) {
        const firstKey = cacheRef.current.keys().next().value;
        if (firstKey !== undefined) cacheRef.current.delete(firstKey);
      }
    }
    return r?.imageBase64;
  }, [profileId, recording.id]);

  // Load the current frame's bytes into state — render uses an <img> with object-fit: contain
  // so the full frame is always visible (the prior canvas approach trimmed when the recording's
  // aspect ratio didn't match the container).
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const b64 = await loadFrame(frameIndex);
      if (!cancelled && b64) setImageSrc(`data:image/png;base64,${b64}`);
    })();
    return () => { cancelled = true; };
  }, [frameIndex, loadFrame]);

  // Reset cache + frame when recording changes.
  useEffect(() => {
    cacheRef.current.clear();
    setFrameIndex(0);
    setImageSrc(undefined);
    setPlaying(false);
  }, [recording.id]);

  // Playback loop.
  useEffect(() => {
    if (!playing) {
      if (playTimerRef.current) { clearInterval(playTimerRef.current); playTimerRef.current = null; }
      return;
    }
    const interval = recording.intervalMs > 0 ? recording.intervalMs : 100;
    playTimerRef.current = window.setInterval(() => {
      setFrameIndex((i) => {
        const next = i + 1;
        if (next >= recording.frameCount) { setPlaying(false); return i; }
        return next;
      });
    }, interval);
    return () => { if (playTimerRef.current) clearInterval(playTimerRef.current); };
  }, [playing, recording.intervalMs, recording.frameCount]);

  return (
    <div className="recording-player">
      <div className="recording-player__canvas-wrap">
        {imageSrc && (
          <img src={imageSrc} alt="" className="recording-player__canvas" />
        )}
      </div>
      <div className="recording-player__controls">
        <CompactButton size="small" icon={<StepBackwardOutlined />}
          disabled={frameIndex === 0}
          onClick={() => setFrameIndex(Math.max(0, frameIndex - 1))} />
        <CompactButton size="small"
          icon={playing ? <PauseOutlined /> : <CaretRightOutlined />}
          onClick={() => setPlaying((p) => !p)} />
        <CompactButton size="small" icon={<StepForwardOutlined />}
          disabled={frameIndex >= recording.frameCount - 1}
          onClick={() => setFrameIndex(Math.min(recording.frameCount - 1, frameIndex + 1))} />
        <Slider
          min={0}
          max={Math.max(0, recording.frameCount - 1)}
          value={frameIndex}
          onChange={(v) => setFrameIndex(v as number)}
          style={{ flex: 1, margin: '0 8px' }}
        />
        <span className="recording-player__frame-info">
          {t('recording.player.frame', 'frame {{i}} / {{n}}', { i: frameIndex + 1, n: recording.frameCount })}
        </span>
      </div>
    </div>
  );
};
