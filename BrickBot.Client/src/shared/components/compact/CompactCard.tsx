import React from 'react';
import { Card, type CardProps } from 'antd';

/**
 * CompactCard — AntD Card with reduced default margins/padding for tighter layouts.
 *
 * Body padding (precedence: bodyStyle wins over padding wins over defaults):
 *   <CompactCard>                         → 12px body padding
 *   <CompactCard extraCompact>            → 8px body padding (dense toolbars/lists)
 *   <CompactCard padding={4}>             → explicit override
 *   <CompactCard bodyStyle={{ padding: 0 }}> → escape hatch (e.g. embedded Monaco editor)
 *
 * Header (title bar) tightening:
 *   <CompactCard denseHeader>             → 32px min-height, 6/12 padding, 14px title.
 *                                            Use for sidebar cards where the default
 *                                            48px AntD title bar wastes vertical space.
 */
export interface CompactCardProps extends Omit<CardProps, 'style' | 'styles'> {
  style?: React.CSSProperties;
  bodyStyle?: React.CSSProperties;
  /** Shortcut for `bodyStyle.padding`. Use this for one-off padding tweaks. */
  padding?: number | string;
  /** When true, uses 8px body padding + 12px bottom margin. */
  extraCompact?: boolean;
  /** When true, tighten the title bar to 32px min-height + smaller font + 6/12 padding. */
  denseHeader?: boolean;
}

export const CompactCard: React.FC<CompactCardProps> = ({
  children,
  style,
  bodyStyle,
  padding,
  extraCompact = false,
  denseHeader = false,
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

  const headerStyle: React.CSSProperties | undefined = denseHeader
    ? {
        minHeight: 32,
        padding: '6px 12px',
        fontSize: 14,
        fontWeight: 600,
      }
    : undefined;

  return (
    <Card style={defaultStyle} styles={{ body: defaultBodyStyle, header: headerStyle }} {...rest}>
      {children}
    </Card>
  );
};
