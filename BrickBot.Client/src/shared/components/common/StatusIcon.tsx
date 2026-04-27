import React from 'react';
import { CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import './StatusIcon.css';

/** StatusIcon — green check / muted close, used for boolean state in lists. */
export interface StatusIconProps {
  isLoaded: boolean;
}

export const StatusIcon: React.FC<StatusIconProps> = ({ isLoaded }) =>
  isLoaded
    ? <CheckCircleOutlined className="status-icon-loaded" />
    : <CloseCircleOutlined className="status-icon-not-loaded" />;
