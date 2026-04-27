import React from 'react';
import { Input, type InputProps } from 'antd';
import type { TextAreaProps } from 'antd/es/input';
import './CompactInput.css';

/**
 * CompactInput — AntD Input with consistent compact heights. Default: medium (32px).
 * Re-exports CompactTextArea + CompactPassword for the common Input.* surfaces.
 */
export type CompactInputSize = 'small' | 'medium' | 'large';

export interface CompactInputProps extends Omit<InputProps, 'size'> {
  size?: CompactInputSize;
}

export const CompactInput: React.FC<CompactInputProps> = ({
  size = 'medium',
  className = '',
  ...rest
}) => {
  const antdSize = size === 'medium' ? 'middle' : size;
  return <Input size={antdSize} className={`compact-input compact-input-${size} ${className}`.trim()} {...rest} />;
};

export interface CompactTextAreaProps extends Omit<TextAreaProps, 'size'> {
  size?: CompactInputSize;
}

export const CompactTextArea: React.FC<CompactTextAreaProps> = ({
  size = 'medium',
  className = '',
  ...rest
}) => (
  <Input.TextArea className={`compact-textarea compact-textarea-${size} ${className}`.trim()} {...rest} />
);

export interface CompactPasswordProps extends Omit<InputProps, 'size'> {
  size?: CompactInputSize;
}

export const CompactPassword: React.FC<CompactPasswordProps> = ({
  size = 'medium',
  className = '',
  ...rest
}) => {
  const antdSize = size === 'medium' ? 'middle' : size;
  return (
    <Input.Password size={antdSize} className={`compact-input compact-input-${size} ${className}`.trim()} {...rest} />
  );
};

export default CompactInput;
