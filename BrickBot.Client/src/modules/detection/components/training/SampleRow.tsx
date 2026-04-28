import React from 'react';
import { Checkbox, Tooltip } from 'antd';
import { DeleteOutlined } from '@ant-design/icons';
import classNames from 'classnames';
import { CompactDangerButton } from '@/shared/components/compact';

/**
 * One row in the samples strip. `React.memo`'d so flipping a single row's
 * `selected` / `active` / `label` state doesn't re-render every sibling.
 *
 * Badges:
 *   • green dot   → sample has an object-box annotation
 *   • blue dot    → sample is the tracker init frame
 */
export interface SampleRowProps {
  id: string;
  index: number;
  imageBase64: string;
  label: string;
  isActive: boolean;
  isSelected: boolean;
  hasBox: boolean;
  isInit: boolean;
  onClick: (e: React.MouseEvent, index: number, id: string) => void;
  onToggle: (id: string) => void;
  onRemove: (index: number) => void;
}

export const SampleRow = React.memo(function SampleRow(props: SampleRowProps) {
  const { id, index, imageBase64, label, isActive, isSelected, hasBox, isInit, onClick, onToggle, onRemove } = props;
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
        <div className="samples-review__row-name">
          #{index + 1}
          {isInit && (
            <Tooltip title="Tracker init frame">
              <span className="samples-review__row-badge samples-review__row-badge--init" />
            </Tooltip>
          )}
          {hasBox && !isInit && (
            <Tooltip title="Annotated">
              <span className="samples-review__row-badge" />
            </Tooltip>
          )}
        </div>
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
