import React from 'react';
import { Card, type CardProps } from 'antd';

/**
 * CompactCard — AntD Card with reduced default margins/padding for tighter layouts.
 *, but lighter: default body padding is 12 (was 16)
 * to give breathing room without over-padding tightly-packed dashboards.
 *
 * Three ways to control padding (precedence: bodyStyle wins over padding wins over defaults):
 *   <CompactCard>                         → 12px body padding
 *   <CompactCard extraCompact>            → 8px body padding (use in dense toolbars/lists)
 *   <CompactCard padding={4}>             → explicit override
 *   <CompactCard bodyStyle={{ padding: 0 }}> → escape hatch (e.g. embedded Monaco editor)
 */
export interface CompactCardProps extends Omit<CardProps, 'style' | 'styles'> {
  style?: React.CSSProperties;
  bodyStyle?: React.CSSProperties;
  /** Shortcut for `bodyStyle.padding`. Use this for one-off padding tweaks. */
  padding?: number | string;
  /** When true, uses 8px body padding + 12px bottom margin. */
  extraCompact?: boolean;
}

export const CompactCard: React.FC<CompactCardProps> = ({
  children,
  style,
  bodyStyle,
  padding,
  extraCompact = false,
  ...rest
}) => {
  const defaultStyle: React.CSSProperties = {
    marginBottom: extraCompact ? 12 : 16,
    ...style,
  };

  // Padding precedence: bodyStyle > padding prop > extraCompact-aware default.
  const resolvedPadding = bodyStyle?.padding ?? padding ?? (extraCompact ? 8 : 12);

  const defaultBodyStyle: React.CSSProperties = {
    padding: resolvedPadding,
    ...bodyStyle,
  };

  return (
    <Card style={defaultStyle} styles={{ body: defaultBodyStyle }} {...rest}>
      {children}
    </Card>
  );
};
