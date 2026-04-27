import React, { useEffect, useState } from 'react';
import { Empty, List, message } from 'antd';
import { PlayCircleOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactAlert,
  CompactCard,
  CompactPrimaryButton,
} from '@/shared/components/compact';
import { eventBus } from '@/shared/services/eventBus';
import {
  actionService,
  type ActionsChangedPayload,
} from '../services/actionService';

/**
 * Lists every <c>brickbot.action(name, fn)</c> the running script has registered and
 * lets the user fire each one on demand. Subscribes to SCRIPT.ACTIONS_CHANGED so the
 * list stays live as scripts add/remove actions; falls back to LIST_ACTIONS on mount
 * to recover state if a script was registered before this panel opened.
 */
export const ActionsPanel: React.FC = () => {
  const { t } = useTranslation();
  const [actions, setActions] = useState<string[]>([]);
  const [busy, setBusy] = useState<string | undefined>();

  useEffect(() => {
    let cancelled = false;
    void actionService.list().then(({ actions: a }) => {
      if (!cancelled) setActions(a);
    });
    const off = eventBus.onModule('SCRIPT', 'ACTIONS_CHANGED', (payload) => {
      const next = (payload as ActionsChangedPayload | undefined)?.actions ?? [];
      setActions(next);
    });
    return () => {
      cancelled = true;
      off();
    };
  }, []);

  const onInvoke = async (name: string) => {
    setBusy(name);
    try {
      await actionService.invoke(name);
      message.success(t('actions.fired', '{{name}} fired', { name }));
    } catch (err) {
      message.error(String(err));
    } finally {
      setBusy(undefined);
    }
  };

  return (
    <div className="actions-panel">
      <CompactAlert
        type="info"
        message={t(
          'actions.help',
          'Actions are registered from running scripts via brickbot.action(name, fn). Click "Run" to invoke one.',
        )}
      />
      <CompactCard
        extraCompact
        title={
          <span>
            <ThunderboltOutlined /> {t('actions.title', 'Registered actions')}
          </span>
        }
        style={{ marginTop: 12 }}
      >
        {actions.length === 0 ? (
          <Empty
            description={t(
              'actions.empty',
              'No actions registered. Start a Run that calls brickbot.action(...).',
            )}
          />
        ) : (
          <List
            size="small"
            dataSource={actions}
            renderItem={(name) => (
              <List.Item
                actions={[
                  <CompactPrimaryButton
                    key="run"
                    size="small"
                    icon={<PlayCircleOutlined />}
                    loading={busy === name}
                    onClick={() => void onInvoke(name)}
                  >
                    {t('actions.run', 'Run')}
                  </CompactPrimaryButton>,
                ]}
              >
                <span style={{ fontFamily: 'monospace' }}>{name}</span>
              </List.Item>
            )}
          />
        )}
      </CompactCard>
    </div>
  );
};
