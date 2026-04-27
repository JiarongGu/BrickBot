import React, { useEffect } from 'react';
import classNames from 'classnames';
import { CloseOutlined } from '@ant-design/icons';
import { Spin } from 'antd';
import { CompactButton } from '../compact';
import './SlideInScreen.css';

/**
 * SlideInScreen — a slide-in-from-right panel with a blur backdrop on the left,
 * a header strip, and ESC-to-close. Drop-in replacement for AntD's `<Drawer>`,
 * Use this anywhere we used to use Drawer for an
 * "open a side panel mid-flow" UX (e.g. the Capture panel from the Scripts editor).
 *
 * API is intentionally simple: open/onClose are controlled by the caller (no
 * provider/stack required). If we ever need multi-level stacking + imperative
 * `openScreen()`, we'll port the  provider on top of this.
 */
export interface SlideInScreenProps {
  open: boolean;
  title: React.ReactNode;
  children: React.ReactNode;
  /**
   * Stacking level. Controls the **left blur-strip width** (the indent),
   *   level 1 →  5%   (default — single panel)
   *   level 2 →  8%   (panel-on-panel)
   *   level 3+ → 11%+ (deeper stacks)
   * The panel itself fills the remaining width via flex:1 (so a level-1 screen takes ~95% of the viewport).
   */
  level?: number;
  /**
   * Optional cap on the panel width (e.g. '900px'). When set, the panel won't grow past this
   * even though it's `flex: 1`. Use sparingly — most tools want the full remaining width.
   */
  width?: string;
  /** Optional loading overlay (semi-transparent dim with spinner). */
  loading?: boolean;
  loadingText?: string;
  onClose: () => void;
  /** Optional CSS class on the content body for view-specific spacing. */
  bodyClassName?: string;
}

export const SlideInScreen: React.FC<SlideInScreenProps> = ({
  open,
  title,
  children,
  level = 1,
  width,
  loading = false,
  loadingText,
  onClose,
  bodyClassName,
}) => {
  // Track the previous `open` so we play the slide-out animation on close.
  const [mounted, setMounted] = React.useState(open);
  const [closing, setClosing] = React.useState(false);

  useEffect(() => {
    if (open) {
      setMounted(true);
      setClosing(false);
    } else if (mounted) {
      setClosing(true);
      const t = setTimeout(() => {
        setMounted(false);
        setClosing(false);
      }, 200); // matches CSS animation
      return () => clearTimeout(t);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // ESC closes (skipped while loading, to avoid stranding mid-operation).
  useEffect(() => {
    if (!mounted) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !closing && !loading) onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [mounted, closing, loading, onClose]);

  if (!mounted) return null;

  // blur strip is FIXED width by stacking level; panel takes the rest via flex:1.
  const blurWidth = level === 1 ? '5%' : `${5 + (level - 1) * 3}%`;
  const panelStyle: React.CSSProperties = width ? { maxWidth: width } : {};

  return (
    <div className={classNames('slide-in-screen-container', `slide-in-screen-level-${level}`, { closing })}>
      <div
        className="slide-in-screen-blur-backdrop"
        style={{ width: blurWidth, cursor: loading ? 'default' : 'pointer' }}
        onClick={() => !closing && !loading && onClose()}
      >
        <div className="slide-in-screen-blur-edge" />
      </div>

      <div className="slide-in-screen-panel" style={panelStyle}>
        {loading && (
          <div className="slide-in-screen-loading-overlay">
            <Spin size="large" />
            {loadingText && <div className="slide-in-screen-loading-text">{loadingText}</div>}
          </div>
        )}

        <div className="slide-in-screen-header">
          <h2 className="slide-in-screen-title">{title}</h2>
          <CompactButton
            type="text"
            size="small"
            icon={<CloseOutlined />}
            disabled={loading}
            onClick={() => !closing && !loading && onClose()}
            className="slide-in-screen-close-btn"
          />
        </div>

        <div className={classNames('slide-in-screen-content', bodyClassName)}>
          {children}
        </div>
      </div>
    </div>
  );
};

export default SlideInScreen;
