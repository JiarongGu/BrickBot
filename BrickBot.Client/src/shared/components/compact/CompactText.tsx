import React from 'react';
import { Typography } from 'antd';

const { Title, Paragraph, Text } = Typography;

/** CompactTitle — Typography.Title with `0 0 8px 0` margin. */
export interface CompactTitleProps {
  level?: 1 | 2 | 3 | 4 | 5;
  children: React.ReactNode;
  style?: React.CSSProperties;
  className?: string;
}

export const CompactTitle: React.FC<CompactTitleProps> = ({
  level = 4,
  children,
  style,
  ...rest
}) => (
  <Title level={level} style={{ margin: '0 0 8px 0', ...style }} {...rest}>
    {children}
  </Title>
);

/** CompactParagraph — Typography.Paragraph with `0 0 8px 0` margin. */
export interface CompactParagraphProps {
  children: React.ReactNode;
  style?: React.CSSProperties;
  className?: string;
  type?: 'secondary' | 'success' | 'warning' | 'danger';
}

export const CompactParagraph: React.FC<CompactParagraphProps> = ({
  children,
  style,
  ...rest
}) => (
  <Paragraph style={{ margin: 0, marginBottom: 8, ...style }} {...rest}>
    {children}
  </Paragraph>
);

/** Re-export of AntD Text for surface consistency. */
export const CompactText = Text;
