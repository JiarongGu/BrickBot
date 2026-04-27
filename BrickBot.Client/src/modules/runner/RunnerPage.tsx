import React, { useEffect } from 'react';
import { Empty, List, Tag, Typography } from 'antd';
import { PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSelect,
  CompactSpace,
} from '@/shared/components/compact';
import { useRunner } from './hooks/useRunner';
import type { RunnerStatus } from './types';

const STATUS_COLORS: Record<RunnerStatus, string> = {
  idle: 'default',
  running: 'green',
  stopping: 'orange',
  faulted: 'red',
};

/**
 * Runner — pick a window, pick a main script, hit Start.
 * No code editor here; scripts live on the Scripts tab. The selected main runs after every
 * library/*.js in the active profile is preloaded into the engine.
 */
export const RunnerPage: React.FC = () => {
  const { t } = useTranslation();
  const r = useRunner();

  useEffect(() => { r.refreshWindows().catch(() => undefined); }, []);

  const isRunning = r.state.status === 'running' || r.state.status === 'stopping';
  const canStart = !!(r.selectedWindow && r.activeProfileId && r.selectedMain && !isRunning);

  return (
    <div
      className="runner-page"
      style={{
        display: 'grid',
        gridTemplateColumns: '340px 1fr',
        gap: 16,
        padding: 16,
        height: 'calc(100vh - var(--layout-header-height) - var(--layout-statusbar-height))',
      }}
    >
      <CompactSpace direction="vertical" style={{ width: '100%' }}>
        <CompactCard extraCompact title={t('runner.targetWindow', 'Target window')}>
          <CompactSpace direction="vertical" style={{ width: '100%' }}>
            <CompactSelect
              showSearch
              placeholder={t('runner.pickWindow', 'Pick a window')}
              value={r.selectedWindow?.handle}
              onChange={(handle) => r.selectWindow(r.windows.find((w) => w.handle === handle))}
              filterOption={(input, opt) => String(opt?.label ?? '').toLowerCase().includes(input.toLowerCase())}
              options={r.windows.map((w) => ({
                value: w.handle,
                label: `${w.title} (${w.processName}) ${w.width}x${w.height}`,
              }))}
              style={{ width: '100%' }}
            />
            <CompactButton onClick={() => r.refreshWindows()} block size="small">
              {t('runner.refreshWindows', 'Refresh windows')}
            </CompactButton>
          </CompactSpace>
        </CompactCard>

        <CompactCard extraCompact title={t('runner.mainScript', 'Main script')}>
          {r.availableMains.length === 0 ? (
            <CompactAlert
              type="info"
              showIcon
              message={t('runner.noMain', 'No main scripts in this profile yet.')}
              description={t('runner.noMainHint', 'Open the Scripts tab to create one.')}
            />
          ) : (
            <CompactSelect
              value={r.selectedMain}
              onChange={(name) => r.setSelectedMain(name)}
              options={r.availableMains.map((name) => ({ value: name, label: `${name}.js` }))}
              style={{ width: '100%' }}
            />
          )}
        </CompactCard>

        <CompactCard extraCompact title={t('runner.templateRoot', 'Template root folder')}>
          <CompactInput
            placeholder={String.raw`e.g. C:\Users\me\BrickBot\templates`}
            value={r.templateRoot}
            onChange={(e) => r.setTemplateRoot(e.target.value)}
          />
        </CompactCard>

        <CompactCard extraCompact title={t('runner.run', 'Run')}>
          <CompactSpace direction="vertical" style={{ width: '100%' }}>
            <Tag color={STATUS_COLORS[r.state.status]}>{r.state.status.toUpperCase()}</Tag>
            {r.state.errorMessage && <Typography.Text type="danger">{r.state.errorMessage}</Typography.Text>}
            <CompactSpace>
              <CompactPrimaryButton icon={<PlayCircleOutlined />} disabled={!canStart} onClick={() => r.start()}>
                {t('common.start')}
              </CompactPrimaryButton>
              <CompactDangerButton icon={<StopOutlined />} disabled={!isRunning} onClick={() => r.stop()}>
                {t('common.stop')}
              </CompactDangerButton>
              <CompactButton onClick={() => r.clearLog()}>{t('runner.clearLog', 'Clear log')}</CompactButton>
            </CompactSpace>
          </CompactSpace>
        </CompactCard>
      </CompactSpace>

      <CompactCard
        extraCompact
        title={t('runner.log', 'Log')}
        bodyStyle={{ padding: 0, height: '100%', overflow: 'auto' }}
      >
        {r.log.length === 0 ? (
          <Empty description={t('runner.logEmpty', 'No log entries yet.')} style={{ paddingTop: 80 }} />
        ) : (
          <List
            size="small"
            dataSource={r.log}
            renderItem={(entry) => (
              <List.Item style={{ padding: '4px 12px' }}>
                <Typography.Text
                  type={entry.level === 'error' ? 'danger' : entry.level === 'warn' ? 'warning' : undefined}
                >
                  [{new Date(entry.timestamp).toLocaleTimeString()}] {entry.message}
                </Typography.Text>
              </List.Item>
            )}
          />
        )}
      </CompactCard>
    </div>
  );
};
