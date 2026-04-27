import React from 'react';
import './CountBadge.css';

/** CountBadge — small numeric pill. Hidden when count is 0 unless `showZero`. */
export interface CountBadgeProps {
  count: number;
  showZero?: boolean;
  className?: string;
}

export const CountBadge: React.FC<CountBadgeProps> = ({
  count,
  showZero = false,
  className,
}) => {
  if (count === 0 && !showZero) return null;
  return (
    <span className={className ? `count-badge ${className}` : 'count-badge'}>{count}</span>
  );
};
