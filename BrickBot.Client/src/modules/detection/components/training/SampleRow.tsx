import React from 'react';
import { Checkbox } from 'antd';
import { DeleteOutlined } from '@ant-design/icons';
import classNames from 'classnames';
import { CompactDangerButton } from '@/shared/components/compact';

/**
 * One row in the samples strip. `React.memo`'d so flipping a single row's
 * `selected` / `active` / `label` state doesn't re-render every sibling.
 *
 * The parent passes stable callbacks (via `useEventCallback`) so memo's
 * prop-equality check actually short-circuits — without that, every parent
 * render hands out fresh closures and memo re-renders all rows anyway.
 *
 * `onClick` receives the raw event so the parent can read modifier keys
 * (Ctrl/Shift) for multi-select; remove + toggle stop-propagation locally.
 */
export interface SampleRowProps {
  id: string;
  index: number;
  imageBase64: string;
  label: string;
  isActive: boolean;
  isSelected: boolean;
  onClick: (e: React.MouseEvent, index: number, id: string) => void;
  onToggle: (id: string) => void;
  onRemove: (index: number) => void;
}

export const SampleRow = React.memo(function SampleRow(props: SampleRowProps) {
  const { id, index, imageBase64, label, isActive, isSelected, onClick, onToggle, onRemove } = props;
  const isUnlabeled = !label.trim();
  return (
    <div
      className={classNames('samples-review__row', {
        'samples-review__row--active': isActive,
        'samples-review__row--selected': isSelected,
        'samples-review__row--unlabeled': isUnlabeled,
      })}
      onClick={(e) => onClick(e, index, id)}
    >
      <Checkbox
        checked={isSelected}
        onClick={(e) => e.stopPropagation()}
        onChange={() => onToggle(id)}
      />
      <img className="samples-review__thumb" src={`data:image/png;base64,${imageBase64}`} alt="" />
      <div className="samples-review__row-info">
        <div className="samples-review__row-name">#{index + 1}</div>
        <div className="samples-review__row-label">{label || '—'}</div>
      </div>
      <CompactDangerButton
        size="small"
        type="text"
        icon={<DeleteOutlined />}
        onClick={(e) => { e.stopPropagation(); onRemove(index); }}
      />
    </div>
  );
});
