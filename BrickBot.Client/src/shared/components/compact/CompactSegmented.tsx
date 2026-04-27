import React from 'react';
import { Segmented, type SegmentedProps } from 'antd';

/**
 * CompactSegmented — AntD Segmented styled for sidebar / inspector use:
 *   - Always block-width so it fills the parent card.
 *   - Font 14px / weight 500 — matches CompactCard's denseHeader tier so a card with a
 *     dense header above and a Segmented inside reads as one consistent type ladder.
 *   - Built-in bottom gap so the Segmented isn't visually glued to the form inputs below.
 *
 * Drop in wherever you'd reach for raw <Segmented> inside a card-shaped layout.
 *
 * @example
 *   <CompactCard denseHeader title="Detect">
 *     <CompactSegmented value={method} onChange={setMethod} options={[...]} />
 *     <YourFormInputs />
 *   </CompactCard>
 */
export interface CompactSegmentedProps extends Omit<SegmentedProps, 'size' | 'block' | 'style'> {
  style?: React.CSSProperties;
  /** Bottom margin to separate the Segmented from the panel content below. Default 12px. */
  bottomGap?: number;
}

export const CompactSegmented: React.FC<CompactSegmentedProps> = ({
  style,
  bottomGap = 12,
  ...rest
}) => (
  <Segmented
    {...rest}
    size="small"
    block
    style={{ marginBottom: bottomGap, fontWeight: 500, fontSize: 14, ...style }}
  />
);
