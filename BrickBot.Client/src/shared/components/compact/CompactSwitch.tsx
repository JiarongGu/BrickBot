import React from 'react';
import { Switch, type SwitchProps } from 'antd';
import './CompactSwitch.css';

/**
 * CompactSwitch — rectangular themed switch (vs AntD's pill shape).
 * BEM: `.compact-switch` block.
 */
export interface CompactSwitchProps extends SwitchProps {
  className?: string;
}

export const CompactSwitch: React.FC<CompactSwitchProps> = ({ className, ...rest }) => (
  <Switch className={className ? `compact-switch ${className}` : 'compact-switch'} {...rest} />
);
