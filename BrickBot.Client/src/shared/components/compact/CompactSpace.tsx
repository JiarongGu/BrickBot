import React from 'react';
import { Space, type SpaceProps } from 'antd';

/**
 * CompactSpace — AntD Space defaulting to 'small' size for tighter layouts.
 * Supports legacy `vertical` boolean shorthand.
 */
export interface CompactSpaceProps extends SpaceProps {
  size?: SpaceProps['size'];
  vertical?: boolean;
}

export const CompactSpace: React.FC<CompactSpaceProps> = ({
  size = 'small',
  vertical,
  children,
  ...rest
}) => {
  const direction = vertical ? 'vertical' : rest.direction;
  return (
    <Space size={size} {...rest} direction={direction}>
      {children}
    </Space>
  );
};
