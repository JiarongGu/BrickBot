import React from 'react';
import { Alert, type AlertProps } from 'antd';
import './CompactAlert.css';

/**
 * CompactAlert — AntD Alert with reduced padding and smaller icon.
 * Pass `extraCompact` for very tight spaces.
 */
export interface CompactAlertProps extends Omit<AlertProps, 'style' | 'className'> {
  style?: React.CSSProperties;
  className?: string;
  extraCompact?: boolean;
}

export const CompactAlert: React.FC<CompactAlertProps> = ({
  style,
  className = '',
  extraCompact = false,
  ...rest
}) => {
  const cls = `compact-alert ${extraCompact ? 'compact-alert-extra' : ''} ${className}`.trim();
  return <Alert className={cls} style={style} {...rest} />;
};
