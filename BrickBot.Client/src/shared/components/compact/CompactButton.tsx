import React from 'react';
import classNames from 'classnames';
import { Button, type ButtonProps } from 'antd';
import './CompactButton.css';

/**
 * CompactButton — AntD Button with consistent height (24/32/40) and theme-aware
 * disabled/hover states. Default size is 'medium' (32px).
 *
 * Convenience variants: Primary, Text, Link, Danger, Warning, Success.
 * Use the compound form `<CompactButton.Danger>` or named exports.
 */
export type CompactButtonSize = 'small' | 'medium' | 'large';

export interface CompactButtonProps extends Omit<ButtonProps, 'size'> {
  size?: CompactButtonSize;
  className?: string;
}

const Base: React.FC<CompactButtonProps> = ({
  size = 'medium',
  className = '',
  children,
  ...rest
}) => {
  const antdSize = size === 'medium' ? 'middle' : size;
  return (
    <Button
      size={antdSize}
      className={classNames('compact-button', `compact-button-${size}`, className)}
      {...rest}
    >
      {children}
    </Button>
  );
};

export const CompactPrimaryButton: React.FC<CompactButtonProps> = (p) => <Base type="primary" {...p} />;
export const CompactTextButton: React.FC<CompactButtonProps> = (p) => <Base type="text" {...p} />;
export const CompactLinkButton: React.FC<CompactButtonProps> = (p) => <Base type="link" {...p} />;
export const CompactDangerButton: React.FC<CompactButtonProps> = (p) => <Base danger {...p} />;
export const CompactWarningButton: React.FC<CompactButtonProps> = ({ className, ...p }) => (
  <Base type="primary" className={classNames('compact-button-warning', className)} {...p} />
);
export const CompactSuccessButton: React.FC<CompactButtonProps> = ({ className, ...p }) => (
  <Base type="primary" className={classNames('compact-button-success', className)} {...p} />
);

// Compound component: <CompactButton.Danger>...</CompactButton.Danger> as well as named imports.
export const CompactButton = Object.assign(Base, {
  Primary: CompactPrimaryButton,
  Text: CompactTextButton,
  Link: CompactLinkButton,
  Danger: CompactDangerButton,
  Warning: CompactWarningButton,
  Success: CompactSuccessButton,
});

export default CompactButton;
