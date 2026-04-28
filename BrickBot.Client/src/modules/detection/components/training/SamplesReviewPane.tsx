import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Checkbox, Tooltip } from 'antd';
import { CameraOutlined, ClearOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactButton,
  CompactDangerButton,
  CompactSpace,
} from '@/shared/components/compact';
import type { DetectionKind } from '../../types';
import { SampleLabelWidget } from './SampleLabelWidget';
import { SampleRow } from './SampleRow';
import { useEventCallback } from './useEventCallback';
import type { SampleFilter } from './types';

/** Minimum data shape this pane needs from each sample. */
export interface ReviewSample {
  id: string;
  imageBase64: string;
  width: number;
  height: number;
  label: string;
  /** True when the sample has an object-box annotation. Drives the green badge + filter. */
  hasBox?: boolean;
  /** True when the sample is the tracker init frame. Drives the blue badge. */
  isInit?: boolean;
}

export interface SamplesReviewPaneProps {
  kind: DetectionKind;
  samples: ReviewSample[];
  selected: number;
  selectedIds: Set<string>;
  toolbar?: React.ReactNode;
  labelHint: string;
  onSelect: (i: number) => void;
  onToggleId: (id: string) => void;
  onSetSelectionIds: (ids: string[]) => void;
  onClearSelection: () => void;
  onLabel: (i: number, label: string) => void;
  onRemove: (i: number) => void;
  onRemoveSelected: () => void;
  onClearAll: () => void;
  onBulkLabels: (labels: string[]) => void;
  onApplyLabelToSelection: (label: string) => void;
}

export const SamplesReviewPane: React.FC<SamplesReviewPaneProps> = ({
  kind, samples, selected, selectedIds, toolbar, labelHint,
  onSelect, onToggleId, onSetSelectionIds, onClearSelection,
  onLabel, onRemove, onRemoveSelected, onClearAll,
  onBulkLabels, onApplyLabelToSelection,
}) => {
  const { t } = useTranslation();
  const previewRef = useRef<HTMLCanvasElement | null>(null);
  const lastClickIdxRef = useRef<number | null>(null);
  const [filter, setFilter] = useState<SampleFilter>('all');
  const current = samples[selected];

  const visible = useMemo(
    () => samples.map((s, i) => ({ s, i })).filter(({ s }) => {
      if (filter === 'all') return true;
      const labeled = s.label.trim().length > 0;
      if (filter === 'labeled') return labeled;
      if (filter === 'unlabeled') return !labeled;
      if (filter === 'unboxed') return !s.hasBox;
      return true;
    }),
    [samples, filter],
  );

  const labeledCount = useMemo(
    () => samples.filter((s) => s.label.trim().length > 0).length,
    [samples],
  );
  const unlabeledCount = samples.length - labeledCount;
  const unboxedCount = samples.filter((s) => !s.hasBox).length;
  const someSelected = selectedIds.size > 0;
  const allVisibleSelected = visible.length > 0 && visible.every(({ s }) => selectedIds.has(s.id));

  useEffect(() => {
    if (!current || !previewRef.current) return;
    const c = previewRef.current;
    const ctx = c.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      c.width = current.width;
      c.height = current.height;
      ctx.drawImage(img, 0, 0);
    };
    img.src = `data:image/png;base64,${current.imageBase64}`;
  }, [current?.id, current?.imageBase64, current?.width, current?.height]);

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.target instanceof HTMLInputElement) return;
    if (e.key === 'ArrowDown' || e.key === 'j') { e.preventDefault(); onSelect(Math.min(samples.length - 1, selected + 1)); }
    else if (e.key === 'ArrowUp' || e.key === 'k') { e.preventDefault(); onSelect(Math.max(0, selected - 1)); }
    else if ((e.key === 'Delete' || (e.key === 'Backspace' && (e.ctrlKey || e.metaKey))) && someSelected) {
      e.preventDefault();
      onRemoveSelected();
    }
    else if (e.key >= '0' && e.key <= '9') {
      e.preventDefault();
      const n = Number(e.key);
      if (kind === 'bar') onLabel(selected, (n / 10).toFixed(2));
    }
  };

  const stableRowClick = useEventCallback((e: React.MouseEvent, idx: number, id: string) => {
    if (e.ctrlKey || e.metaKey) {
      onToggleId(id);
      lastClickIdxRef.current = idx;
    } else if (e.shiftKey && lastClickIdxRef.current !== null) {
      const anchor = lastClickIdxRef.current;
      const lo = Math.min(anchor, idx);
      const hi = Math.max(anchor, idx);
      const ids: string[] = [];
      for (let i = lo; i <= hi; i++) ids.push(samples[i].id);
      onSetSelectionIds(ids);
    } else {
      onSelect(idx);
      if (someSelected) onClearSelection();
      lastClickIdxRef.current = idx;
    }
  });
  const stableToggle = useEventCallback((id: string) => onToggleId(id));
  const stableRemove = useEventCallback((idx: number) => onRemove(idx));

  const onToggleSelectAll = (checked: boolean) => {
    if (checked) onSetSelectionIds(visible.map(({ s }) => s.id));
    else onClearSelection();
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

  const filterPills: { value: SampleFilter; label: string; count: number }[] = [
    { value: 'all', label: t('detection.train.filter.all', 'All'), count: samples.length },
    { value: 'unlabeled', label: t('detection.train.filter.unlabeled', 'Unlabeled'), count: unlabeledCount },
    { value: 'labeled', label: t('detection.train.filter.labeled', 'Labeled'), count: labeledCount },
    { value: 'unboxed', label: t('detection.train.filter.unboxed', 'No box'), count: unboxedCount },
  ];

  return (
    <div className="samples-review" tabIndex={0} onKeyDown={onKeyDown}>
      <div className="samples-review__left">
        {toolbar && <div className="samples-review__toolbar">{toolbar}</div>}
        <div className="samples-review__strip">
          <div className="samples-review__strip-header">
            <Checkbox
              checked={allVisibleSelected}
              indeterminate={someSelected && !allVisibleSelected}
              disabled={visible.length === 0}
              onChange={(e) => onToggleSelectAll(e.target.checked)}
            />
            <span className="samples-review__count">
              {someSelected
                ? t('detection.train.samples.selectedCount', '{{n}} selected', { n: selectedIds.size })
                : t('detection.train.samples.summary', '{{n}} samples · {{m}} labeled', { n: samples.length, m: labeledCount })}
            </span>
            {someSelected ? (
              <Tooltip title={t('detection.train.deleteSelected.tip', 'Remove the selected samples (Del)')}>
                <CompactDangerButton
                  size="small"
                  type="text"
                  icon={<DeleteOutlined />}
                  onClick={onRemoveSelected}
                >
                  {t('detection.train.deleteSelected', 'Delete')}
                </CompactDangerButton>
              </Tooltip>
            ) : samples.length > 0 ? (
              <Tooltip title={t('detection.train.clearAll.tip', 'Remove every sample')}>
                <CompactButton size="small" type="text" icon={<ClearOutlined />} onClick={onClearAll} />
              </Tooltip>
            ) : null}
          </div>
          {samples.length > 0 && (
            <div className="samples-review__filter-bar">
              {filterPills.map((p) => (
                <CompactButton
                  key={p.value}
                  size="small"
                  type={filter === p.value ? 'primary' : 'text'}
                  onClick={() => setFilter(p.value)}
                >
                  {p.label} ({p.count})
                </CompactButton>
              ))}
            </div>
          )}
          <div className="samples-review__strip-body">
            {visible.length === 0 ? (
              <div className="samples-review__strip-empty">
                {samples.length === 0
                  ? t('detection.train.noSamplesYet', 'No samples yet.')
                  : t('detection.train.noFilterMatch', 'No samples match the filter.')}
              </div>
            ) : (
              visible.map(({ s, i }) => (
                <SampleRow
                  key={s.id}
                  id={s.id}
                  index={i}
                  imageBase64={s.imageBase64}
                  label={s.label}
                  isActive={i === selected}
                  isSelected={selectedIds.has(s.id)}
                  hasBox={!!s.hasBox}
                  isInit={!!s.isInit}
                  onClick={stableRowClick}
                  onToggle={stableToggle}
                  onRemove={stableRemove}
                />
              ))
            )}
          </div>
        </div>
      </div>

      <div className="samples-review__preview">
        {current ? (
          <>
            <div className="samples-review__preview-header">
              <span className="samples-review__preview-title">
                #{selected + 1} of {samples.length} · {current.width}×{current.height}
                <span className="samples-review__preview-kind"> · {t('detection.train.labelFormat', 'label')}: <b>{labelHint}</b></span>
              </span>
              <span className="samples-review__preview-kbhint">
                {t('detection.train.kbHint', '↑↓ cycle · 0–9 label · Ctrl/Shift-click select · Del remove')}
              </span>
              <CompactSpace size={4}>
                {kind === 'bar' && (
                  <Tooltip title={t('detection.train.autoDistribute.tip', 'Spread labels evenly from 0 to 1 across samples (capture order).')}>
                    <CompactButton size="small" onClick={autoDistribute} disabled={samples.length < 2}>
                      {t('detection.train.autoDistribute', 'Auto 0→1')}
                    </CompactButton>
                  </Tooltip>
                )}
                {someSelected ? (
                  <Tooltip title={t('detection.train.applySel.tip', 'Set the current label on every selected sample.')}>
                    <CompactButton
                      size="small"
                      onClick={() => onApplyLabelToSelection(current.label)}
                      disabled={!current.label.trim()}
                    >
                      {t('detection.train.applySel', 'Apply to {{n}} selected', { n: selectedIds.size })}
                    </CompactButton>
                  </Tooltip>
                ) : (
                  <Tooltip title={t('detection.train.applyAll.tip', 'Set every sample\'s label to the current value.')}>
                    <CompactButton size="small" onClick={applyToAll} disabled={!current.label.trim()}>
                      {t('detection.train.applyAll', 'Apply to all')}
                    </CompactButton>
                  </Tooltip>
                )}
              </CompactSpace>
            </div>
            <div className="samples-review__canvas-wrap">
              <canvas ref={previewRef} className="samples-review__canvas" />
            </div>
            <SampleLabelWidget kind={kind} value={current.label} onChange={(v) => onLabel(selected, v)} />
          </>
        ) : (
          <div className="samples-review__empty">
            <CameraOutlined className="samples-review__empty-icon" />
            <div className="samples-review__empty-title">
              {t('detection.train.empty.title', 'No samples yet')}
            </div>
            <div className="samples-review__empty-hint">
              {t('detection.train.empty.hint', 'Snapshot the live window, record a session, load a saved recording, or drop image files. You need at least 2 to train.')}
              <br />
              {t('detection.train.empty.labelHint', 'Label format:')} <b>{labelHint}</b>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
