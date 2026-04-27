import React from 'react';
import { CompactTitle } from './CompactText';
import { CompactSpace } from './CompactSpace';
import './CompactSection.css';

/**
 * CompactSection — semantic block with optional title + auto-spaced children.
 */
export interface CompactSectionProps {
  title?: React.ReactNode;
  titleLevel?: 1 | 2 | 3 | 4 | 5;
  children: React.ReactNode;
  className?: string;
  style?: React.CSSProperties;
  spacing?: 'small' | 'middle' | 'large' | number;
}

export const CompactSection: React.FC<CompactSectionProps> = ({
  title,
  titleLevel = 4,
  children,
  className,
  style,
  spacing = 'small',
}) => (
  <div className={className} style={style}>
    {title && <CompactTitle level={titleLevel}>{title}</CompactTitle>}
    <CompactSpace direction="vertical" className="compact-section-space" size={spacing}>
      {children}
    </CompactSpace>
  </div>
);
