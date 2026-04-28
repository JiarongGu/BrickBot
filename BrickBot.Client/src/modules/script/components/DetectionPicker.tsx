import React, { useEffect, useMemo, useState } from 'react';
import { Empty, List, Tag, message } from 'antd';
import { useTranslation } from 'react-i18next';
import { CompactButton, CompactInput } from '@/shared/components/compact';
import { useProfileStore } from '@/modules/profile';
import { detectionService, useDetectionStore } from '@/modules/detection';
import type { DetectionDefinition, DetectionKind } from '@/modules/detection';
import { useEditorBridgeStore } from '../store/editorBridgeStore';
import './DetectionPicker.css';

/**
 * Picker rendered in a SlideInScreen above the Monaco editor. Lists every saved
 * detection for the active profile and lets the user insert a kind-appropriate
 * `detect.run(...)` snippet at the cursor.
 *
 * Replaces the old CapturePanel that lived in this slot — capture/template
 * authoring now lives only in the Tools tab. Scripts authors deal in detection
 * names, not template files.
 */

const KIND_TAG_COLOR: Record<DetectionKind, string> = {
  tracker: 'magenta',
  pattern: 'blue',
  text: 'gold',
  bar: 'green',
  composite: 'purple',
};

/** Snippet inserted at the cursor — picks the most useful field per detection
 *  kind so the user can chain into a script without remembering the result shape. */
function snippetFor(def: DetectionDefinition): string {
  const access = (() => {
    switch (def.kind) {
      case 'bar':       return '.value';   // 0..1 fill ratio
      case 'tracker':   return '.match';   // {x,y,w,h,cx,cy} — current position
      case 'text':      return '.text';    // OCR string
      case 'pattern':   return '.match';   // matched bbox
      case 'composite': return '.found';   // boolean AND/OR
    }
  })();
  return `detect.run('${def.name}')${access}`;
}

export const DetectionPicker: React.FC<{ onClose: () => void }> = ({ onClose }) => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);
  const detections = useDetectionStore((s) => s.detections);
  const [filter, setFilter] = useState('');
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!profileId) return;
    setLoading(true);
    detectionService.list(profileId)
      .then((r) => useDetectionStore.getState().setDetections(r.detections))
      .catch(() => undefined)
      .finally(() => setLoading(false));
  }, [profileId]);

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return detections;
    return detections.filter((d) =>
      d.name.toLowerCase().includes(q) ||
      (d.group ?? '').toLowerCase().includes(q) ||
      d.kind.toLowerCase().includes(q),
    );
  }, [detections, filter]);

  const insert = (def: DetectionDefinition) => {
    const snippet = snippetFor(def);
    const ok = useEditorBridgeStore.getState().insertAtCursor(snippet);
    if (ok) {
      message.success(t('script.detectionPicker.inserted', 'Inserted {{snippet}}', { snippet }));
      onClose();
    } else {
      message.warning(t('script.detectionPicker.noEditor', 'Open a script first.'));
    }
  };

  return (
    <div className="detection-picker">
      <div className="detection-picker__hint">
        {t('script.detectionPicker.hint', 'Click a detection to insert a `detect.run(...)` reference at the cursor.')}
      </div>
      <CompactInput
        placeholder={t('script.detectionPicker.search', 'Search by name, group, or kind') as string}
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        allowClear
      />
      {filtered.length === 0 ? (
        <Empty
          description={
            detections.length === 0
              ? t('script.detectionPicker.empty', 'No detections trained yet. Train one in the Detections tab first.')
              : t('script.detectionPicker.noMatch', 'No detections match the filter.')
          }
        />
      ) : (
        <List
          loading={loading}
          dataSource={filtered}
          renderItem={(d) => (
            <List.Item
              className="detection-picker__item"
              onClick={() => insert(d)}
              actions={[
                <CompactButton
                  key="insert"
                  size="small"
                  type="primary"
                  onClick={(e) => { e.stopPropagation(); insert(d); }}
                >
                  {t('script.detectionPicker.insert', 'Insert')}
                </CompactButton>,
              ]}
            >
              <List.Item.Meta
                title={
                  <span>
                    <Tag color={KIND_TAG_COLOR[d.kind]}>{d.kind}</Tag>
                    {d.name}
                    {d.group && <span className="detection-picker__group"> · {d.group}</span>}
                  </span>
                }
                description={<code className="detection-picker__snippet">{snippetFor(d)}</code>}
              />
            </List.Item>
          )}
        />
      )}
    </div>
  );
};
