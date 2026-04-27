import React from 'react';
import { Select, type SelectProps } from 'antd';
import './CompactSelect.css';

/**
 * CompactSelect — AntD Select with consistent compact heights (24/32/40).
 * Default size: medium (32px).
 */
export type CompactSelectSize = 'small' | 'medium' | 'large';

export interface CompactSelectProps<T = unknown> extends Omit<SelectProps<T>, 'size'> {
  size?: CompactSelectSize;
}

export function CompactSelect<T = unknown>({
  size = 'medium',
  className = '',
  ...rest
}: CompactSelectProps<T>) {
  const antdSize = size === 'medium' ? 'middle' : size;
  const cls = `compact-select compact-select-${size} ${className}`.trim();
  return <Select<T> size={antdSize} className={cls} {...rest} />;
}

export default CompactSelect;
