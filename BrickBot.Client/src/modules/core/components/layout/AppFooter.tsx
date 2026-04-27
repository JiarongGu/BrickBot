import React, { useEffect, useRef, useState } from 'react';
import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import { CompactSpace } from '@/shared/components/compact';
import { useRunnerStore } from '@/modules/runner/store/runnerStore';
import './AppFooter.css';

// Backend injects `window.__APP_METADATA__` via WebViewInitializer.InjectAppMetadata
// (when wired). Fallback to the package version baked into the bundle.
interface AppMetadata {
  name: string;
  version: string;
}

declare global {
  interface Window {
    __APP_METADATA__?: AppMetadata;
  }
}

const FALLBACK_NAME = 'BrickBot';
const FALLBACK_VERSION = '0.1.0';

const STATUS_LABEL: Record<string, string> = {
  idle: 'Idle',
  running: 'Running',
  stopping: 'Stopping',
  faulted: 'Faulted',
};

const STATUS_COLORS: Record<string, string> = {
  idle: 'default',
  running: 'green',
  stopping: 'orange',
  faulted: 'red',
};

/**
 * AppFooter — 32px status bar at the bottom:
 * runner state on the left, app name + version on the right (clickable for help once added).
 * Progress strip / task popover are stubbed out for now — wire when we add a task store.
 */
export const AppFooter: React.FC<{ onAboutClick?: () => void }> = ({ onAboutClick }) => {
  const { t } = useTranslation();
  const barRef = useRef<HTMLDivElement>(null);

  const [appName, setAppName] = useState(FALLBACK_NAME);
  const [appVersion, setAppVersion] = useState(FALLBACK_VERSION);

  // Read backend-injected metadata once. Avoids reactivity since the value is set before
  // the bundle even runs.
  useEffect(() => {
    const meta = window.__APP_METADATA__;
    if (meta?.name) setAppName(meta.name);
    if (meta?.version) setAppVersion(meta.version);
  }, []);

  const runnerStatus = useRunnerStore((s) => s.state.status);

  return (
    <div className="app-footer" ref={barRef}>
      <div className="app-footer-main">
        {/* Left: progress / runner indicator. Empty progress track for visual rhythm. */}
        <div className="app-footer-task-area">
          <div className="app-footer-progress-track">
            {runnerStatus === 'running' && <div className="app-footer-progress-indeterminate" />}
          </div>
        </div>

        {/* Right: status tag + app version. */}
        <CompactSpace size="middle" style={{ marginLeft: 'auto' }}>
          <Tag color={STATUS_COLORS[runnerStatus] ?? 'default'}>
            {STATUS_LABEL[runnerStatus] ?? runnerStatus}
          </Tag>
          <span
            className="app-footer-version"
            onClick={onAboutClick}
            title={t('footer.aboutTooltip', 'Click to view about')}
          >
            {appName} v{appVersion}
          </span>
        </CompactSpace>
      </div>
    </div>
  );
};
