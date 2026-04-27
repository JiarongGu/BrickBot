import React, { useCallback, useEffect, useState } from 'react';
import { Tooltip } from 'antd';
import { ReloadOutlined, AppstoreOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { CompactSelect, CompactButton } from '@/shared/components/compact';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import './WindowSelector.css';

export interface WindowSelectorProps {
  /** Selected window handle. */
  value?: number;
  onChange: (handle: number | undefined, info?: WindowInfo) => void;
  /** Pre-fetched list. If omitted, the component fetches its own and exposes a refresh button. */
  windows?: WindowInfo[];
  onWindowsChange?: (windows: WindowInfo[]) => void;
  /** Show a refresh button next to the select (default true when self-managing windows). */
  showRefresh?: boolean;
  /** Auto-select the first window once the list loads (default true). */
  autoSelectFirst?: boolean;
  disabled?: boolean;
  style?: React.CSSProperties;
  className?: string;
  /** Min width of the select. Default 320. */
  minWidth?: number;
  placeholder?: string;
}

/**
 * Shared window picker. Renders process icon + title (primary) + process name & size (secondary)
 * in the dropdown. The selected pill shows the icon + title only so it stays compact.
 *
 * Two modes:
 *  - Controlled list: caller passes `windows` + `onWindowsChange`. The component does not fetch.
 *  - Self-managed: caller omits `windows`. The component fetches via captureService and shows
 *    a refresh button. Suitable for places that don't need to share the window list.
 */
export const WindowSelector: React.FC<WindowSelectorProps> = ({
  value,
  onChange,
  windows: externalWindows,
  onWindowsChange,
  showRefresh,
  autoSelectFirst = true,
  disabled,
  style,
  className,
  minWidth = 320,
  placeholder,
}) => {
  const { t } = useTranslation();
  const isControlled = externalWindows !== undefined;
  const [internalWindows, setInternalWindows] = useState<WindowInfo[]>([]);
  const list = isControlled ? externalWindows! : internalWindows;
  const showRefreshBtn = showRefresh ?? !isControlled;

  const refresh = useCallback(async () => {
    const next = await captureService.listWindows();
    if (isControlled) {
      onWindowsChange?.(next);
    } else {
      setInternalWindows(next);
    }
    if (autoSelectFirst && next.length > 0 && !value) {
      onChange(next[0].handle, next[0]);
    }
  }, [isControlled, onWindowsChange, autoSelectFirst, value, onChange]);

  // Always fetch once on mount. In controlled mode the result flows back through
  // onWindowsChange; callers that already have a populated list and don't want to refetch
  // can pass autoFetch={false}. Without this, controlled callers (RecordRecordingDialog)
  // never see a list and the dropdown stays empty.
  useEffect(() => {
    void refresh();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const options = list.map((w) => ({
    value: w.handle,
    label: w.title,
    info: w,
  }));

  return (
    <div className={`window-selector ${className ?? ''}`.trim()} style={style}>
      <CompactSelect
        showSearch
        value={value}
        onChange={(h) => {
          const info = list.find((w) => w.handle === h);
          onChange(h as number | undefined, info);
        }}
        disabled={disabled}
        placeholder={placeholder ?? t('runner.pickWindow', 'Pick a window')}
        style={{ minWidth, flex: 1 }}
        options={options}
        optionFilterProp="label"
        filterOption={(input, option) => {
          const w = (option as unknown as { info?: WindowInfo })?.info;
          if (!w) return false;
          const q = input.toLowerCase();
          return w.title.toLowerCase().includes(q) || w.processName.toLowerCase().includes(q);
        }}
        optionRender={(opt) => {
          const w = (opt.data as unknown as { info: WindowInfo }).info;
          return <WindowOptionRow info={w} />;
        }}
        labelRender={(opt) => {
          const w = list.find((x) => x.handle === opt.value);
          if (!w) return <span>{opt.label}</span>;
          return <WindowOptionRow info={w} compact />;
        }}
      />
      {showRefreshBtn && (
        <Tooltip title={t('runner.refreshWindows', 'Refresh windows')}>
          <CompactButton icon={<ReloadOutlined />} onClick={refresh} disabled={disabled} />
        </Tooltip>
      )}
    </div>
  );
};

interface WindowOptionRowProps {
  info: WindowInfo;
  /** Compact = single-line variant for the selected pill. */
  compact?: boolean;
}

const WindowOptionRow: React.FC<WindowOptionRowProps> = ({ info, compact }) => {
  const iconSrc = info.iconBase64 ? `data:image/png;base64,${info.iconBase64}` : null;
  return (
    <div className={`window-selector__row ${compact ? 'window-selector__row--compact' : ''}`}>
      <div className="window-selector__icon">
        {iconSrc ? (
          <img src={iconSrc} alt="" />
        ) : (
          <AppstoreOutlined />
        )}
      </div>
      <div className="window-selector__text">
        <div className="window-selector__title" title={info.title}>{info.title}</div>
        {!compact && (
          <div className="window-selector__meta">
            <span>{info.processName || '—'}</span>
            <span>·</span>
            <span>{info.width}×{info.height}</span>
          </div>
        )}
      </div>
    </div>
  );
};

export default WindowSelector;
