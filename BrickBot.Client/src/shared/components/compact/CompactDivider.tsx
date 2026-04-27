import React from 'react';
import { Divider, type DividerProps } from 'antd';

/**
 * CompactDivider — AntD Divider with reduced 12px (or 8px extra-compact) margins.
 */
export interface CompactDividerProps extends Omit<DividerProps, 'style'> {
  style?: React.CSSProperties;
  extraCompact?: boolean;
}

export const CompactDivider: React.FC<CompactDividerProps> = ({
  style,
  extraCompact = false,
  ...rest
}) => {
  const merged: React.CSSProperties = { margin: extraCompact ? '8px 0' : '12px 0', ...style };
  return <Divider style={merged} {...rest} />;
};
