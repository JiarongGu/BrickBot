import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { App as AntdApp, ConfigProvider, Layout, Spin, theme as antdTheme } from 'antd';
import '@/shared/i18n';
import { AppHeader, type AppTab } from '@/modules/core/components/layout/AppHeader';
import { AppFooter } from '@/modules/core/components/layout/AppFooter';
import { RunnerPage } from '@/modules/runner/RunnerPage';
import { ScriptsView } from '@/modules/script';
import { ToolsView } from '@/modules/tool';
import { initProfiles } from '@/modules/profile';
import { SettingsPanel, initSettings, useSettingsStore } from '@/modules/setting';
import './styles/theme-colors.css';
import './App.css';

const { Content } = Layout;

export const App: React.FC = () => {
  const [tab, setTab] = useState<AppTab>('runner');
  const [booting, setBooting] = useState(true);

  const resolvedTheme = useSettingsStore((s) => s.resolvedTheme);

  const algorithm = useMemo(
    () => (resolvedTheme === 'dark' ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm),
    [resolvedTheme],
  );

  // Drive [data-theme] on <html> so theme-colors.css picks up the right palette.
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', resolvedTheme);
  }, [resolvedTheme]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        await Promise.all([initSettings(), initProfiles()]);
      } catch (err) {
        console.error('Boot failed', err);
      } finally {
        if (!cancelled) setBooting(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // "Manage profiles" in the header dropdown routes to the Tools tab where profile mgmt lives.
  const handleManageProfiles = useCallback(() => setTab('tools'), []);

  return (
    <ConfigProvider theme={{ algorithm }} componentSize="middle">
      <AntdApp notification={{ maxCount: 1, stack: false }}>
        <Layout className="app-main-layout">
          <AppHeader selectedTab={tab} onTabChange={setTab} onManageProfiles={handleManageProfiles} />
          <Content className="app-content">
            {booting ? (
              <div className="app-boot">
                <Spin size="large" />
              </div>
            ) : tab === 'runner' ? (
              <RunnerPage />
            ) : tab === 'scripts' ? (
              <ScriptsView />
            ) : tab === 'tools' ? (
              <ToolsView />
            ) : (
              <SettingsPanel />
            )}
          </Content>
          <AppFooter />
        </Layout>
      </AntdApp>
    </ConfigProvider>
  );
};
