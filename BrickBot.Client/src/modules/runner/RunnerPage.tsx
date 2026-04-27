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
import { WindowSelector } from '@/shared/components/common';
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
            <WindowSelector
              value={r.selectedWindow?.handle}
              onChange={(_, info) => r.selectWindow(info)}
              windows={r.windows}
              showRefresh={false}
              autoSelectFirst={false}
              style={{ width: '100%' }}
              minWidth={0}
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

        <CompactCard extraCompact title={t('runner.stopWhen', 'Auto-stop conditions')}>
          <CompactSpace direction="vertical" style={{ width: '100%' }}>
            <CompactSelect
              placeholder={t('runner.stopWhen.timeout', 'Timeout (none)')}
              value={r.stopWhen.timeoutMs}
              onChange={(v) => r.setStopWhen({ timeoutMs: v as number | undefined })}
              allowClear
              options={[
                { value: 60_000, label: '1 minute' },
                { value: 5 * 60_000, label: '5 minutes' },
                { value: 15 * 60_000, label: '15 minutes' },
                { value: 30 * 60_000, label: '30 minutes' },
                { value: 60 * 60_000, label: '1 hour' },
                { value: 4 * 60 * 60_000, label: '4 hours' },
              ]}
              style={{ width: '100%' }}
            />
            <CompactInput
              placeholder={t('runner.stopWhen.event', 'Stop on event (e.g. goalReached)') as string}
              value={r.stopWhen.onEvent ?? ''}
              onChange={(e) => r.setStopWhen({ onEvent: e.target.value || undefined })}
            />
            <CompactSpace style={{ width: '100%' }}>
              <CompactInput
                placeholder={t('runner.stopWhen.ctxKey', 'ctx key') as string}
                value={r.stopWhen.ctxKey ?? ''}
                onChange={(e) => r.setStopWhen({ ctxKey: e.target.value || undefined })}
                style={{ flex: 1 }}
              />
              <CompactSelect
                value={r.stopWhen.ctxOp ?? 'eq'}
                onChange={(v) => r.setStopWhen({ ctxOp: v as string })}
                options={[
                  { value: 'eq', label: '=' },
                  { value: 'neq', label: '≠' },
                  { value: 'gt', label: '>' },
                  { value: 'gte', label: '≥' },
                  { value: 'lt', label: '<' },
                  { value: 'lte', label: '≤' },
                ]}
                style={{ width: 70 }}
              />
              <CompactInput
                placeholder={t('runner.stopWhen.ctxValue', 'value') as string}
                value={r.stopWhen.ctxValue ?? ''}
                onChange={(e) => r.setStopWhen({ ctxValue: e.target.value || undefined })}
                style={{ flex: 1 }}
              />
            </CompactSpace>
          </CompactSpace>
        </CompactCard>

        <CompactCard extraCompact title={t('runner.run', 'Run')}>
          <CompactSpace direction="vertical" style={{ width: '100%' }}>
            <CompactSpace size={4}>
              <Tag color={STATUS_COLORS[r.state.status]}>{r.state.status.toUpperCase()}</Tag>
              {r.state.stoppedReason && r.state.stoppedReason !== 'none' && r.state.status === 'idle' && (
                <Tag color={r.state.stoppedReason === 'faulted' ? 'red' : 'blue'}>
                  {t('runner.stopReason', 'stopped: {{reason}}', { reason: r.state.stoppedReason })}
                  {r.state.stoppedDetail ? ` · ${r.state.stoppedDetail}` : ''}
                </Tag>
              )}
            </CompactSpace>
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
