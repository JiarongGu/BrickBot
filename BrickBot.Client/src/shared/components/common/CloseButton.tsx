import React from 'react';
import { CloseOutlined } from '@ant-design/icons';
import './CloseButton.css';

/**
 * CloseButton — square 24/32/40 close button with theme-aware styling and keyboard support.
 * Use inside dialogs / overlays where AntD's default close icon is too small.
 */
export interface CloseButtonProps {
  onClick?: () => void;
  size?: 'small' | 'medium' | 'large';
  className?: string;
  ariaLabel?: string;
}

export const CloseButton: React.FC<CloseButtonProps> = ({
  onClick,
  size = 'medium',
  className = '',
  ariaLabel = 'Close',
}) => (
  <div
    className={`close-button close-button-${size} ${className}`.trim()}
    onClick={onClick}
    role="button"
    tabIndex={0}
    aria-label={ariaLabel}
    onKeyDown={(e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onClick?.();
      }
    }}
  >
    <CloseOutlined />
  </div>
);

export default CloseButton;
